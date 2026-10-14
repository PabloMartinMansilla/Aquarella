# Auditoría y protección de migración de correo

Fecha de cierre: 2026-10-09. Rama: `main`. HEAD: `6c78d2c`.

## 1. Riesgo original y alcance

La versión anterior ejecutaba `Database.MigrateAsync()` durante el arranque. Abrir Aquarella después de actualizar podía aplicar `20261008090000_AccountEmailDelivery` sobre la base configurada sin revisión ni respaldo. Una ruta incorrecta podía crear otra SQLite vacía mediante el modo predeterminado ReadWriteCreate.

Se eliminó ese comportamiento. Las pruebas demuestran conservación de los datos de fixtures representativos y de todos los esquemas anteriores reconocidos. **No certifican el estado particular de la base real, que no fue abierta ni migrada.** Antes de migrarla deberá validarse una copia consistente autorizada.

Se preservaron la implementación anterior de correo, sus verificadores, su informe y todos los cambios pendientes. No hubo acceso a servicios externos, SMTP real, Docker ni Railway.

## 2. Causa técnica y bases identificadas

`Aquarella/Program.cs` resolvía la conexión, creaba el directorio y migraba incondicionalmente. No existía un procedimiento operativo separado de validación, respaldo y ejecución.

Development utiliza, salvo configuración explícita, `App_Data/aquarella.db` relativo al content root. El archivo del proyecto es `Aquarella/App_Data/aquarella.db`; se observaron únicamente nombres y tamaños, incluidos WAL y SHM. También existen archivos locales de verificaciones/respaldos anteriores: no se abrieron, borraron ni reutilizaron.

Production ya exige conexión absoluta explícita dentro de `ProductionStorage:Root`, volumen separado del contenido publicado y key ring persistente. No se consultaron secretos ni una base remota para inferir su configuración. Railway futuro utilizaría `/data/aquarella.db`, no una ruta Windows.

No se cambió `launchSettings.json`, el puerto ni la conexión local. Los runners utilizaron exclusivamente directorios TEMP nuevos con nombres aleatorios y cuentas `example.test` ficticias. Ninguna prueba copió la base real.

## 3. Esquemas anteriores y nuevo

Cadena versionada encontrada:

1. `20261004053300_InitialBusinessData`: usuarios, negocios y datos iniciales.
2. `20261004211838_RealAccounts`: cuentas, hashes, email, verificación, sesiones y tokens.
3. `20261005000444_UserAiSettings`: preferencias tipadas serializadas por usuario.
4. `20261005180000_StockIntakeReceipts`: historial de confirmaciones de ingreso de stock.
5. `20261008090000_AccountEmailDelivery`: implementación pendiente de correo de la tarea anterior.

La quinta migración agrega `AccountTokens.CreatedAt` no nullable con default `DateTime.MinValue`, sustituye el índice de UserId por `(UserId, Purpose, CreatedAt)` y crea `AccountEmailNotifications` con su PK, índice UserId y FK hacia Users con Cascade. No actualiza usuarios, hashes, verificación, permisos, negocios, productos, precios, configuraciones ni sesiones. No crea usuarios ni envíos.

La eliminación del índice antiguo es un cambio de índice sin eliminación de datos; el nuevo mantiene UserId como primera columna. La nueva FK no admite notificaciones huérfanas. `Down` eliminaría la tabla de notificaciones y la columna nueva: **la herramienta no ofrece downgrade ni ejecutó Down**.

No se modificaron esta migración ni el modelo/snapshot en esta auditoría. Se revisaron las migraciones anteriores; los cambios de cuentas de versiones anteriores también se probaron desde sus esquemas respectivos. No se encontró un mecanismo manual paralelo de actualización de esquema. La importación legacy de BusinessData opera sobre datos y no reemplaza las migraciones EF.

## 4. Cambios realizados

- Arranque sin migraciones; valida SQLite en modo ReadOnly y exige archivo existente.
- Conexiones de aplicación en ReadWrite, que falla si desaparece la base en lugar de crear otra.
- Inspector de historial, esquema, integridad, relaciones y bloqueo EF.
- Herramienta de mantenimiento separada, sin endpoint web ni fallback a appsettings.
- Respaldo consistente SQLite, restauración a archivo nuevo, comparación de datos y registro privado del resultado.
- Bloqueo de mantenimiento compartido por instancias de aplicación y exclusivo para migrar.
- Verificador reproducible y adaptación del runner Production a preparación explícita de su fixture TEMP.
- Exclusiones de Git/publicación y documentación operativa actualizada.

No se cambiaron diseño, navegación, módulos, autenticación, paquetes ni tecnología de persistencia en esta auditoría.

## 5. Estrategia elegida

La aplicación sirve únicamente un esquema reconocido y actual. El operador prepara una ventana sin escritores, inspecciona y ejecuta explícitamente la herramienta de la misma versión de Aquarella.

`inspect` identifica Missing, Empty, Pending, Current, Incompatible o Locked. Compara el historial con un prefijo consecutivo de las migraciones conocidas; rechaza versiones futuras, huecos, tablas manuales sin historial y diferencias físicas. La referencia de esquema se construye ejecutando las migraciones **en memoria**, nunca sobre el archivo inspeccionado. Incluye columnas, defaults, índices, restricciones, tablas y triggers; conserva literales al normalizar espacios. No adopta ni repara esquemas desconocidos.

`migrate` exige ruta absoluta, directorio privado de respaldo, `--confirm-migrate` y `--confirm-stopped`. Si el archivo falta o está vacío, exige además `--create`, destinado exclusivamente a inicialización aprobada. El parser rechaza argumentos desconocidos/repetidos. El comando `inspect` puede devolver código cero para Pending: el operador debe revisar su State, no tratar el exit code como autorización para iniciar.

Secuencia: validar → respaldo comprobado → registro Prepared → migración EF → validación completa → comparación de datos originales → registro Completed. Reejecutar sobre Current produce AlreadyCurrent sin migración ni duplicados. Ante fallo conserva el respaldo y registra Failed con tipo de excepción, versiones y cantidades; no imprime SQL, hashes, tokens ni valores de filas.

## 6. Protección contra migración accidental

No existe flag que habilite migración automática durante el arranque, en ningún entorno. Missing/Pending/Incompatible/Locked detienen la aplicación antes de iniciar servidor y worker de correo. No se usa EnsureCreated, borrado o recreación como reparación.

`<base>.maintenance.lock` mantiene un handle compartido durante la vida de la aplicación; mantenimiento necesita uno exclusivo. Se verificó tanto dentro de un proceso como contra una instancia real aislada. No se elimina el archivo para desbloquear: el handle se libera al salir. Un bloqueo pendiente en `__EFMigrationsLock` exige revisión, no se borra automáticamente.

Esta coordinación exige que todas las instancias usen esta versión. **No detiene un ejecutable antiguo ni una herramienta SQLite externa que ignore el protocolo.** Por eso la confirmación de detener todos los escritores sigue siendo obligatoria; la herramienta no mata procesos.

## 7. Procedimiento de respaldo

1. Confirmar identidad y ruta de la base, espacio, permisos y destino privado. Para actualizar, detener todos los escritores antes de continuar.
2. Ejecutar `backup` con origen absoluto y destino nuevo con extensión `.db`, `.sqlite` o `.sqlite3`.
3. La herramienta abre el origen ReadOnly, inicia una transacción de lectura y verifica integridad y FKs. Obtiene esquema y sellos SHA-256 de las filas ordenadas, sin publicar contenido.
4. `SqliteConnection.BackupDatabase` incorpora las páginas comprometidas de WAL del snapshot. No se copia solo el archivo `.db` activo.
5. Abre el respaldo, comprueba integrity_check, foreign_key_check, esquema y datos. Solo entonces informa éxito.

CreateNew rechaza destinos existentes y evita sobrescribir el origen; las rutas de mantenimiento rechazan enlaces simbólicos y respaldos bajo wwwroot. El respaldo automático de migrate es único (UTC + GUID). Un respaldo fallido no debe utilizarse aunque haya quedado un archivo parcial: revisar el resultado y repetir con otro destino nuevo.

Los backups contienen información sensible, incluidos hashes y tokens internos: almacenar con permisos privados, limitar retención y utilizar almacenamiento cifrado adecuado. No se suben a Git ni se publican. El key ring y configuración secreta requieren respaldo privado separado; SQLite no los incluye.

## 8. Procedimiento de restauración

`restore` lee el backup y crea **otro archivo nuevo**, usando el mismo mecanismo y comprobaciones. No reemplaza la original ni decide automáticamente deshacer escrituras.

Ante una migración fallida: mantener la aplicación detenida; conservar backup, registro y conjunto original DB/WAL/SHM; inspeccionar el estado y ensayar restore a una ruta temporal nueva. Verificar esquema, integridad y datos, y probar arranque con la versión compatible. Solo después de revisión/autorización se decide cómo volver a apuntar a una base recuperada. No borrar journals, restaurar encima de una base activa ni copiar únicamente el DB de un conjunto activo.

Los backups anteriores conservan el esquema anterior; para abrirlos con esta versión deben migrarse explícitamente después de comprobar la restauración. La prueba de fallo/restauración conservó intacta la base origen.

Ejemplo reproducible, desde la carpeta Aquarella, **solo sobre fixtures TEMP**:

```powershell
dotnet build tools/DatabaseMaintenance --artifacts-path "$env:TEMP\aquarella-migration-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
$taskTool = "$env:TEMP\aquarella-migration-build\bin\DatabaseMaintenance\debug\DatabaseMaintenance.dll"
$taskDir = Join-Path $env:TEMP ('aquarella-manual-fixture-' + [guid]::NewGuid().ToString('N'))
$taskDb = Join-Path $taskDir 'fixture.db'
$taskBackups = Join-Path $taskDir 'backups'
# Inicialización de una base ficticia nueva, nunca la base local del usuario:
dotnet $taskTool migrate --database $taskDb --backup-dir $taskBackups --confirm-migrate --confirm-stopped --create
dotnet $taskTool inspect --database $taskDb
dotnet $taskTool backup --database $taskDb --destination (Join-Path $taskDir 'backup.db')
dotnet $taskTool restore --database (Join-Path $taskDir 'backup.db') --destination (Join-Path $taskDir 'restored.db')
```

Para una base existente autorizada se omite `--create`. Estos comandos son instrucciones de reproducción, no autorización para ejecutarlos contra datos reales.

## 9. Resultados de pruebas

`MigrationSafetyVerification`: **79 comprobaciones correctas**, con publish aislado.

| Escenario | Resultado |
|---|---|
| Archivo ausente/vacío | Detectado sin creación implícita; inicialización solo explícita |
| Cada una de las cuatro versiones anteriores | Reconocida y actualizada; columnas/datos originales conservados |
| Usuarios A/B y cuenta legacy | IDs, hashes, perfiles, preferencias, negocios y ownership conservados |
| Productos/stock/precios/notas/recibos | Cantidades y decimales exactos conservados; sin duplicados |
| Tokens y sesiones anteriores | Campos existentes conservados; sesión del usuario verificado válida |
| WAL con escritor abierto y registros comprometidos | Backup y restauración conservan esos registros |
| Ya migrada / reejecución | AlreadyCurrent, datos e historial sin cambios |
| Fallo después del ALTER de AccountTokens | Rollback de la migración, versión anterior reconocida y arranque bloqueado |
| Fallo / reintento / restauración | Registro Failed, backup íntegro, reintento correcto, restore en nuevo archivo |
| Índice faltante / tabla manual / historial futuro | Rechazo seguro, sin reparación/downgrade |
| FK inválida / bloqueo EF pendiente | Detectados; sin borrado automático |
| Aplicación o mantenimiento en ejecución | Exclusión verificada, incluso con proceso real |
| Arranque sobre Pending/drift/Missing | Termina sin migrar, reparar ni crear SQLite |
| Arranque y segundo arranque tras migrar | Login responde; datos conservados; sin errores del servidor |

Fixtures de esta ejecución: `%TEMP%\aquarella-migration-tests-1bff9faa229b4d26afccae78c3dc7823`. No contienen datos reales.

También pasaron los **14 verificadores anteriores**: AccountMailVerification (83 comprobaciones), AccountHttpVerification, DevelopmentAccessVerification (invocado por el runner HTTP), ProductionHostingVerification, SecurityVerification, DatabaseVerification, PersistenceVerification, ReliabilityVerification, StockIntakeVerification, AiSettingsVerification, MetricsVerification, MetricsPresentationVerification, NoticesVerification y ProductVisualVerification.

Dos pruebas JavaScript correctas: `legacy-recovery.test.mjs` y `metrics-motion.test.mjs`. Las pruebas HTTP utilizan HTTPS temporal o proxy simulado en loopback, keys y DB nuevas. Se comprobaron autenticación, logout/replay, CSRF, rutas protegidas, sesión/cookie/reinicio, correo simulado, validación y aislamiento. Solo se cerraron los procesos propios de prueba. SMTP estuvo deshabilitado y no hubo llamadas a IA/OCR.

Comprobación adicional del ejecutable CLI: `migrate` sin `--confirm-migrate` devuelve código 1 y no crea el archivo TEMP indicado. El publish aislado se revisó por nombres/rutas: cero bases, journals, claves, logs, registros operativos o archivos App_Data privados.

## 10. Integridad y transacciones

Se verifican `PRAGMA integrity_check` y `foreign_key_check`. Los sellos comparan todas las filas y todas las columnas preexistentes de cada tabla de aplicación; al terminar, seleccionan esas mismas columnas, permitiendo únicamente las adiciones de esquema esperadas. Los hashes no se escriben en el registro operativo. La nueva tabla empieza vacía, su FK rechaza usuarios inexistentes y los índices se comparan con la referencia versionada.

EF aplica transacciones por migración. El fallo inyectado después de ALTER y antes de crear notificaciones dejó completa la versión anterior y conservó todos sus datos. **No se promete una transacción única de toda una cadena de varias migraciones**: si una posterior falla, versiones anteriores pueden haber quedado comprometidas como un prefijo válido. Se inspecciona y registra ese estado; Pending sigue impidiendo el arranque. No hay recuperación automática que sobrescriba información.

## 11. Compatibilidad de usuarios existentes

Se preservan UserId/BusinessId, hashes, datos, flags y sesiones. Se probó autenticar con el hash previo y validar una sesión existente de un usuario ya verificado después de migrar. No se verifican arbitrariamente cuentas pendientes ni se vinculan negocios nuevos a usuarios existentes.

La implementación de correo anterior exige verificación y hash para sesiones Production. **La conservación de la fila de sesión no implica habilitar una cuenta antigua no verificada**: esa política de autenticación puede rechazarla independientemente de la migración. La auditoría no cambió esa política. Cuentas legacy sin email/hash permanecen así; no se habilitaron en Production.

Antes de incorporar usuarios reales antiguos hace falta decidir un flujo explícito de vinculación/verificación y recuperación, usando prueba de propiedad. No se hizo una promoción masiva ni se migró la base real para resolverlo. El enlace de vinculación Development existente sigue siendo exclusivamente local y no forma parte de Up.

## 12. Impacto en Development

El arranque local tiene la misma protección: si la base está atrasada, Visual Studio mostrará el error seguro con indicación de la herramienta. **Esta entrega no ejecutó la migración local para que el proyecto arranque.** Una nueva base exige inicialización explícita; una existente requiere respaldo y migración autorizada.

URLs, certificados y puertos quedan iguales. La base no se reinicializa al fallar. Los fixtures de desarrollo se preparan antes de iniciar la aplicación; el runner Production también prepara explícitamente su propia base TEMP.

## 13. Impacto futuro en Railway

Mantener una instancia y un volumen persistente, conexión `/data/aquarella.db` y key ring privado bajo `/data`. No mezclar datos con el publish ni permitir crear una DB porque un mount/ruta esté mal configurado.

Antes de un upgrade: aprobación, ventana sin escritores, confirmación del volumen/ruta, backup verificado y ensayo en copia privada, herramienta de la misma versión, migración explícita, revisión del registro e integridad, y después arranque y comprobación funcional. Un redeploy únicamente valida; no vuelve a crear ni reemplaza datos.

La imagen actual publica la aplicación, **no la nueva herramienta operativa**. Preparar su artefacto/acceso al volumen será un paso previo del despliegue autorizado; no se modificó Dockerfile ni se creó infraestructura para ello. Deben ensayarse permisos/bloqueos/backup en Linux y tener almacenamiento privado de respaldos fuera del único volumen. No se accedió a Railway.

## 14. Archivos de esta auditoría

Nuevos:

- `Aquarella/Data/DatabaseMigrationSafety.cs`.
- `Aquarella/Data/DatabaseMaintenance.cs`.
- `tools/DatabaseMaintenance/DatabaseMaintenance.csproj` y `Program.cs`.
- `tools/MigrationSafetyVerification/MigrationSafetyVerification.csproj` y `Program.cs`.
- Este informe `AUDITORIA-MIGRACION-CORREO.md`.

Modificados sobre los cambios locales anteriores:

- `Aquarella/Program.cs`: guard de esquema, lease y modo ReadWrite; quitar migración de arranque.
- `Aquarella/Aquarella.csproj`: exclusiones de publicación para DB/respaldos/registros/locks.
- `.gitignore`: locks, registros operativos y directorios de backups.
- `tools/ProductionHostingVerification/Program.cs`: preparación explícita del fixture nuevo TEMP.
- `DEPLOYMENT.md`: procedimiento explícito y requisitos de inicialización/backup.

Los demás cambios pendientes de entidades, migración, correo, páginas de cuenta y verificadores corresponden a la tarea anterior y se conservaron. `IMPLEMENTACION-CORREO-AUTENTICACION.md` queda como evidencia histórica; su advertencia de migración automática se resuelve por esta auditoría. Las siete imágenes de preview preexistentes también se conservaron.

## 15. Build y reproducción

Solución: `dotnet build Aquarella.slnx` con artifacts en TEMP, paquetes del caché local y `NuGetAudit=false`: **0 errores y 0 warnings**. Build de los 16 proyectos de herramientas y publish Release también correctos, sin warnings. No se agregaron dependencias ni se actualizaron paquetes; no se hizo consulta de vulnerabilidades externa.

```powershell
dotnet build Aquarella.slnx --artifacts-path "$env:TEMP\aquarella-migration-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet build tools/MigrationSafetyVerification --artifacts-path "$env:TEMP\aquarella-migration-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet publish Aquarella/Aquarella.csproj -c Release --artifacts-path "$env:TEMP\aquarella-migration-build" -o "$env:TEMP\aquarella-migration-publish-review" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet "$env:TEMP\aquarella-migration-build\bin\MigrationSafetyVerification\debug\MigrationSafetyVerification.dll" "$env:TEMP\aquarella-migration-publish-review"
```

El último argumento permite las pruebas reales de arranque/reinicio. Sin publish se informa que se omitieron; no se presenta esa ejecución como prueba del servidor.

## 16. Estado de Git

`main`, HEAD `6c78d2c`, sin cambio de rama. Working tree con cambios locales de ambas tareas y previews conservados, para revisión. Sin staging, commit, push, pull, merge, rebase, reset ni descarte. `git diff --check` correcto; Git avisa normalización LF/CRLF, no errores de compilación.

DB, WAL/SHM/journals, backups, logs, claves, secretos y registros operativos permanecen ignorados; publish aislado sin datos privados. No se agregó ninguno al índice.

## 17. Pasos manuales pendientes

1. Revisar el diff y aprobar un ensayo sobre una copia consistente autorizada de la base real; identificar su esquema sin asumir que coincide con los fixtures.
2. Resolver la política de incorporación de cuentas antiguas no verificadas, sin promoción automática.
3. Confirmar backup privado y key ring, espacio/permisos, versión de herramienta y ausencia de todos los escritores, incluidos ejecutables antiguos.
4. Migrar la base real únicamente en otra tarea autorizada; revisar el registro, integridad, conteos, ownership y operaciones funcionales antes de continuar.
5. Ensayar restore y operación en Linux/volumen previo a Railway. Preparar artefacto operativo y respaldo externo antes del deployment.
6. SMTP/Gmail sigue deshabilitado; activación, credenciales y entrega real requieren su propia autorización y pruebas.

## 18. Riesgos residuales

- Un esquema legítimo pero manual/diferente se rechaza conservadoramente. Requiere investigación, nunca editar el historial para forzar compatibilidad.
- El protocolo de lease no sustituye detener otros escritores ni permisos del sistema; no protege de un administrador o un proceso externo que ignore la herramienta.
- Un fallo de disco/permisos puede impedir completar el backup o escribir Failed y dejar Prepared. Ante cualquier fallo no reabrir por intuición: inspeccionar, conservar evidencia y decidir recuperación. Una discrepancia de datos requiere detener uso aunque el esquema sea Current; no hay restauración automática.
- El plazo de dos minutos pasa CancellationToken a operaciones async, pero la API nativa de backup es síncrona. Bases muy grandes, espacio insuficiente o hardware lento necesitan planificación; no se ensayaron cargas reales grandes.
- integrity_check completa al iniciar agrega costo proporcional al tamaño de SQLite. Se priorizó detectar un estado inseguro; medir sobre una copia real antes de beta, sin eliminar el guard a ciegas.
- La validación de esquema no reemplaza una política de compatibilidad de cuentas, permisos correctos, backups externos ni protección del key ring.
- Ningún test puede garantizar conservación frente a corte eléctrico/hardware defectuoso. Se probaron rollback controlado, reintento y restore; la base real y el hosting siguen pendientes de validación autorizada.

Conclusión: la actualización aditiva conserva datos en los escenarios reproducidos y dejó de dispararse al iniciar Aquarella. La incorporación sobre datos reales queda condicionada a validar una copia autorizada y ejecutar el procedimiento operativo, sin modificar la base real en esta tarea.
