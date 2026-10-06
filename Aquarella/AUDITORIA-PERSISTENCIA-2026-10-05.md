# Auditoría de persistencia, integridad y aislamiento — 5 de octubre de 2026

Alcance: datos, ownership, sesiones, migraciones y reinicios. Rama inicial `main`, HEAD `7ac5d11`. Al comenzar solo había siete PNG de previsualización sin seguimiento; se conservaron. No se modificaron bases reales, puertos/configuración de ejecución, diseño, esquema, migraciones, paquetes, Docker ni Railway. Los informes anteriores y las correcciones de Perfil, Precios, números y lifecycle permanecen intactos.

**Conclusión:** las pruebas de los flujos auditados respaldan el aislamiento A/B, la integridad de las cargas transaccionales y la persistencia después de reinicios. No se encontró acceso cruzado mediante los IDs probados. No corresponde afirmar que nunca pueda perderse un cambio propio: se reprodujo una sobrescritura de campos de Perfil desde una vista desactualizada. Ese pendiente requiere definir cómo resolver conflictos entre ediciones.

## 1. Modelo de ownership

El modelo real es `User → Business → Products / CalendarEntries / StockIntakeReceipts`. Cada usuario tiene como máximo un negocio, mediante la FK e índice único `Businesses.UserId`. No existe actualmente un modelo de varios negocios por cuenta.

`Business.Profile` es un owned type de EF guardado en la misma fila de `Businesses`. Los precios están dentro de `Products`, junto con cantidad y nombre: no hay una tabla de precios separada. Preferencias IA y tutorial pertenecen directamente a `Users`. Tokens y sesiones tienen FK a `Users`.

`BusinessData` obtiene el usuario mediante `AccountSession.RequireUserAsync`, valida su sesión en el servidor y deriva el negocio consultando `Businesses.UserId`. Las operaciones de productos/notas filtran también por ese negocio. El cliente no elige el propietario al guardar Perfil. Los contextos se crean por operación y se liberan; no se conserva un contexto EF durante todo el circuito Blazor.

## 2. Inventario de persistencia

| Información | Almacenamiento y propietario | Lectura / escritura / eliminación |
|---|---|---|
| Identidad de cuenta, email verificado y hash de contraseña | `Users`, cuenta | `AccountService`, flujos de registro/verificación/recuperación y cuenta; no hay eliminación de cuenta desde la UI actual. Username y email normalizado tienen unicidad. |
| Nombre, logo, descripción, colores, contacto, redes, rubro y horarios | `Businesses.Profile`, negocio del usuario | `BusinessData` y adaptador de `BusinessProfileStore`; guardado de perfil completo. Configuración de página usa los mismos colores, sin duplicar una entidad. Se elimina con el negocio. |
| Producto y cantidad | `Products.BusinessId` | Consultas filtradas, creación/edición, ajuste SQL atómico y eliminación por ID + negocio. |
| Costo, porcentaje, venta y venta manual | Misma fila `Products` | Precios carga solo productos del negocio y aplica los cambios por ID + negocio. Se elimina junto con el producto. |
| Notas, fecha y hora opcional | `CalendarEntries.BusinessId` | `BusinessData`: carga propia, creación/edición validada y eliminación propia. Índice negocio/fecha. |
| Confirmaciones de cargas de Stock | `StockIntakeReceipts.BusinessId` | Transacción de carga y replay por negocio + clave de operación; unicidad de ese par. No existe limpieza de recibos en la UI. Cascada al eliminar el negocio. |
| Preferencias reales de IA | `Users.AiSettingsJson` | `AiSettingsStore` con usuario validado; modelo tipado y recuperación existentes. No se ejecutó IA. |
| Tutorial completado | `Users.HasCompletedOnboarding` | `AccountSession`, lectura/actualización del usuario validado. Solicitud de repetir el tour es estado de circuito, no otro registro. |
| Sesiones y tokens de cuenta | `AccountLoginSessions` / `AccountTokens`, FK usuario | Inicio, validación, expiración, revocación, consumo y recuperación. Cookie protegida por Data Protection; no es una contraseña persistida en texto. |
| Copia legacy del prototipo | `localStorage`, importación antigua | Solo se importa si `LegacyImported` es falso; el original queda como copia de recuperación. Las cuentas nuevas nacen con importación completada. No es el backend actual. |
| Caché de tema e intro | Navegador | Caché persistente del tema: solo tres colores. La marca de intro es de sesión de navegador; no concede autenticación. |
| Correo y enlace de desarrollo | Archivos en `App_Data`, solo Development | Outbox/enlace local, ignorados por Git y fuera del contenido estático. Production no habilita el acceso sin cuenta. |

Avisos y Métricas son proyecciones de productos/precios propios: no existen tablas de historial de ventas o métricas. Conversaciones/chat no guarda un historial en SQLite. Tampoco se persiste el archivo original de una factura en los recibos de Stock.

## 3. Aislamiento A/B

El nuevo `PersistenceVerification` registra y verifica dos cuentas ficticias mediante los servicios reales, con negocios y sesiones independientes. A tiene producto exclusivo, stock **17**, costo **2000,50**, venta **3000,75**, margen **35%**; B tiene stock **31**, costo **20,25**, venta **50,10**, margen **80%**. Se guardaron perfiles, logos, colores, notas, preferencias IA y tutorial diferentes.

Cada cuenta leyó exclusivamente su producto, precio, nota y configuración. Métricas recibió solo su cantidad. Después de las operaciones hostiles de A, el snapshot de Perfil/Stock/Precios/Agenda/tutorial de B quedó idéntico. Las preferencias IA se comprobaron por usuario en lecturas independientes.

Se revisaron servicios, stores, componentes, handlers de cuenta y consultas EF. La protección relevante está en las consultas y sesión del servidor, además de la navegación. Las pruebas no dependen únicamente de que un botón esté oculto.

## 4. Manipulación de IDs

Como A se intentaron editar el producto de B, crear un producto usando su ID, ajustar su stock, cambiar su costo mediante patch, reemplazar sus precios completos, editar una nota de B y asociar una carga de Stock a su producto. Todas las escrituras fueron rechazadas sin alterar B.

Las eliminaciones de producto/nota ajenos devolvieron un no-op según la implementación actual; no eliminaron nada de B. La colisión al intentar guardar una nota ajena se rechaza por PK, porque la búsqueda no obtiene la fila de otro negocio y nunca la sobrescribe. Perfil no recibe BusinessId/UserId: copiar valores de B solo puede escribir la fila propia de A.

También se probó una importación legacy de una tercera cuenta con ProductId de B: la colisión de PK hizo rollback y no modificó ni B ni parcialmente el perfil/notas/flag de importación de esa tercera cuenta.

## 5. Persistencia y reinicios

Se realizaron escrituras y lecturas con contextos independientes y se volvieron a leer datos desde **procesos nuevos**. Sobrevivieron nombre, logo, colores, descripción/rubro/horario, cantidades, decimales exactos, margen, venta manual, fecha/contenido/hora de Agenda, preferencias IA, tutorial y recibos.

Se amplió `ProductionHostingVerification` para comparar snapshots persistentes exactos antes y después de **detener y arrancar de nuevo un servidor ASP.NET Core real**. Incluye dos cuentas y los módulos persistentes. La cookie válida continuó funcionando con las mismas claves de Data Protection. Este verificador usa alojamiento nativo local; no prueba Docker/Linux ni un deployment de Railway.

Se creó un backup coherente de la DB ficticia mediante `SqliteConnection.BackupDatabase` y se comprobó su contenido en otro proceso/contextos nuevos. No se copió ni respaldó la DB real del usuario.

## 6. Logout/Login y cambio de usuario

El verificador de datos revoca sesiones reales en SQLite, vuelve a autenticar y crea nuevos servicios/circuitos de prueba. Cubre sesión expirada, ausente, anónima y par usuario/sesión incorrecto. No concede acceso al negocio con esos estados.

El verificador de hosting ejecuta Login A → logout → Login B → logout → Login A mediante HTTP real, CSRF y cookies reales. `/mi-cuenta` devuelve únicamente la identidad correspondiente. `AccountHttpVerification` cubre además revocación y protección de rutas; `DevelopmentAccessVerification` comprueba reingreso sin duplicar usuarios/negocios ni alterar datos/tutorial.

En `PersistenceVerification` solo el transporte que emite la cookie se sustituye para capturar el principal; registro, hashes, verificación, sesiones, consultas y escrituras usan código real/SQLite. La evidencia de cookies proviene de los verificadores HTTP, no de ese adaptador.

No se realizó una prueba visual de cambio de identidad dentro de un circuito SignalR abierto. Los stores son scoped y las operaciones vuelven a validar sesión; la revalidación visual de autenticación existente es cada 10 segundos. Queda checklist para una pestaña antigua después del logout.

## 7. Eliminaciones y relaciones

Eliminar un producto elimina también su precio, porque están en la misma fila. Las notas no referencian productos; no se borran por esa operación. Avisos/Métricas se recalculan con los productos restantes.

Los recibos conservan resultados históricos serializados aunque se elimine un producto; no tienen FK a cada ProductId del JSON. Se comprobó su retención: mantiene la idempotencia y no constituye una fila de producto huérfana. No se cambió esa política.

Sobre una tercera cuenta ficticia se verificó la cascada declarada: usuario → negocio/productos/notas/recibos y usuario → tokens/sesiones. B permaneció igual. Esta prueba directa de EF no agrega una función de eliminación de cuenta a la aplicación.

## 8. Operaciones parciales, concurrencia y duplicados

La carga de Stock guarda productos/cantidades/costos y recibo dentro de una transacción. Un trigger temporal provocó un fallo SQL real al insertar el recibo, después de preparar cambios de productos. Se confirmó rollback de cantidad, costo, producto nuevo y recibo: no quedó una confirmación de éxito parcial.

La importación legacy también es transaccional. Registro crea usuario y negocio juntos. Precios continúa con la solución anterior de guardados serializados/cambios de campo, sin rehacerla; pasó su regresión.

Ocho incrementos concurrentes de cantidad sobre el mismo producto sumaron exactamente ocho. La carga utiliza coincidencias conservadoras y evita elegir arbitrariamente entre nombres ambiguos; permite selección explícita o crear nuevo. Su clave de confirmación por negocio evita repetir una misma operación. Dos claves distintas representan dos cargas legítimas, no un doble submit deduplicado por nombre.

**Se reprodujo un lost update en Perfil:** dos copias del mismo perfil; la primera cambia nombre y guarda; la segunda, desactualizada, cambia color y guarda. El segundo guardado devuelve el nombre anterior. La prueba restaura después el perfil ficticio original. No hay versión/control de conflicto para el formulario completo. Tampoco se alteró el comportamiento de ediciones absolutas de Stock/Agenda/IA; su política de último guardado se inspeccionó, sin atribuirles pruebas de conflicto que no se realizaron.

## 9. SQLite y backup

En una DB nueva migrada se comprobó `foreign_keys = 1`, `journal_mode = wal`, `integrity_check = ok` y rechazo de una inserción huérfana. Contextos cortos y consultas parametrizadas; no se detectó SQL de entrada de usuario concatenado.

SQLite serializa escritores: es una limitación aceptable del MVP de una instancia, no evidencia de preparación para múltiples réplicas. WAL permite concurrencia de lectura pero mantiene un escritor. [SQLite WAL](https://www.sqlite.org/wal.html).

Una copia del archivo principal mientras hay escrituras/WAL no es una estrategia suficiente de backup coherente. La API online usada en la prueba produce un snapshot consistente. Antes del deployment hace falta definir frecuencia, retención y restauración operativa. [SQLite Online Backup](https://www.sqlite.org/backup.html).

La ruta de desarrollo puede quedar dentro del proyecto en OneDrive: si se sincronizan/copían archivos de una DB activa, hay un riesgo operativo de coherencia. No se verificó que OneDrive esté sincronizando efectivamente esa DB. En Production, mantener volumen y claves de Data Protection persistentes/restringidas sigue siendo necesario; no se cambió el deployment. [Configuración de Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

## 10. Migraciones, índices y modelo

Las **cuatro migraciones** construyeron una DB completamente vacía. `HasPendingModelChanges()` devolvió falso: el snapshot representa el modelo actual. No fue necesario ningún dato manual de la DB local.

Se revisaron PK, FK/cascadas, unicidad de username/email normalizado, relación única usuario-negocio, clave única negocio/operación de Stock, índice negocio/fecha de Agenda y restricción `Quantity >= 0`. No se encontró una ausencia de índice que requiriera cambiar esquema para resolver un fallo demostrado.

No se migró, comparó destructivamente ni reparó la base real. No se crearon migraciones nuevas.

## 11. Integridad numérica y validaciones

Se verificó escritura/lectura exacta de costos **0, 2, 20, 200, 2000, 20000, 2000,50 y 1.000.000.000**. Stock cubrió cero, `int.MaxValue`, rechazo de negativos y overflow. Margen: 10.000 admitido y 10.001 rechazado. Se rechazaron costo negativo y producto sin nombre.

Cantidad es `int`; costo/venta/margen son `decimal`. El modelo declara escala 2, pero SQLite guarda decimal como TEXT y no impone precisión/escala/longitudes como otros motores. La validación de aplicación sigue siendo necesaria. No se introdujo redondeo ni una política nueva para valores con más decimales. [Tipos SQLite en .NET](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types).

La presentación `es-AR` permanece intacta y pasó su verificador anterior. Estas pruebas comprueban igualdad numérica persistida, además de las pruebas existentes de parsing/formato.

## 12. Seguridad de acceso, recuperación y logging

No se encontró un IDOR entre negocios en los flujos auditados. Las FK previenen huérfanos; la autorización por negocio es adicional, no se delega exclusivamente en una FK.

Se detectaron dos debilidades de contrato del servicio de cuenta: logout validaba solo `sid`, y validación de sesión aceptaba claims sin exigir identidad autenticada. Se probaron con principals construidos en el verificador. **No se demostró falsificación de cookies ni un ataque HTTP contra una cookie firmada**; la corrección endurece el límite del servicio.

Recibos persistidos con JSON `null`, roto, `[null]` o `[]` podían devolver un resultado inválido/excepción técnica. Ahora se rechaza el replay de forma controlada, preservando recibo y stock sin repetir la carga. El dato original no se reemplaza al leer. Las estrategias anteriores de Perfil/IA/legacy malformados se conservaron y sus verificadores pasaron.

Se reutiliza `ILogger`: el nuevo warning de recibo no incluye JSON, credenciales ni identificadores personales. La lectura/importación existente registra tipo de error, sin contenido. No se habilitó sensitive-data logging de EF. Los logs HTTP aislados terminaron vacíos con nivel Warning; los fallos SQL controlados pertenecen al verificador.

## 13. Correcciones realizadas

| Hallazgo reproducido | Nivel | Cambio pequeño |
|---|---|---|
| `SaveProfileAsync` permitía persistir un perfil inválido al invocarlo fuera de la validación UI | Medio | Validar con `BusinessProfileStore.IsValid` en la frontera de persistencia, antes de escribir. El perfil anterior queda intacto. |
| Replay de recibo con JSON inválido/nulo | Medio | Validar estructura/rangos del resultado y lanzar `ValidationException` controlada; loguear sin contenido y no reaplicar ni reemplazar datos. |
| Logout con identidad A y sid B podía revocar B desde una llamada directa al servicio | Bajo | Eliminar únicamente sesión con `sid` **y** `UserId` de una identidad autenticada. |
| Principal no autenticado con claims válidos aceptado por validación de sesión | Bajo | Exigir `Identity.IsAuthenticated` antes de consultar la sesión. |

Los probes específicos reprodujeron los fallos antes de la corrección y pasan después. No se cambiaron el modelo de cuentas, autenticación por cookies, permisos ni relaciones.

## 14. Hallazgos no corregidos y prioridades

- **Crítico:** ninguno confirmado en los flujos probados.
- **Alto:** ninguno confirmado pendiente de esta auditoría.
- **Medio, reproducido:** sobrescritura de campos propios de Perfil desde una copia desactualizada. Prioridad siguiente: decidir entre rechazar conflictos con aviso/recarga o aplicar cambios por campo; la combinación de cambios simultáneos del mismo campo también necesita criterio. No se implementó una política sin esa decisión.
- **Medio, operativo:** falta definir/verificar backup periódico y restauración antes de deployment. El mecanismo de backup/restauración ficticia funcionó; no implica que exista retención automática del negocio real.
- **Bajo:** caché global del navegador conserva tres colores y puede mostrar temporalmente el tema previo hasta cargar el perfil actual. No contiene nombre, logo, contactos ni productos persistidos. No se cambió la transición visual.
- **Bajo:** confirmar en navegador cambio de cuenta/circuito viejo y pestañas antiguas tras revocación; no sustituir esa prueba por checks de HTTP estático.

La aceptación de más de dos decimales y el guardado absoluto de formularios son políticas existentes que conviene explicitar; no se cambió su significado. No hay una garantía absoluta sobre todos los caminos futuros ni una auditoría completa de ciberseguridad.

## 15. Pruebas y evidencia

| Verificador | Resultado / cobertura |
|---|---|
| `PersistenceVerification` — nuevo | PASS: A/B, IDs manipulados, FK, rollback SQL real, límites, recibos malformados, sesiones, cascadas, procesos nuevos y backup. Documenta además el conflicto de Perfil reproducido. |
| `DatabaseVerification` | PASS, incluido reintento previo de Perfil. |
| `AiSettingsVerification` | PASS, 20 casos. |
| `NoticesVerification` | PASS, 17 casos. |
| `MetricsVerification` | PASS, 20 casos. |
| `MetricsPresentationVerification` | PASS, 64 casos. |
| `StockIntakeVerification` | PASS: coincidencias, ambigüedad, concurrencia, idempotencia y ownership. |
| `ReliabilityVerification` | PASS: concurrencia Precios, recuperación, lifecycle y números. |
| `ProductionHostingVerification` — ampliado | PASS: Production local, aislamiento de identidad HTTP, datos y cookies tras reinicio real, bypass Development rechazado. |
| `AccountHttpVerification` | PASS: registro, duplicados, verificación, Login, cookies, CSRF, recuperación, logout y rate limiting. |
| `DevelopmentAccessVerification` | PASS: cookie real, rutas, logout/reingreso y datos/tutorial intactos sin duplicar cuentas. |
| `metrics-motion.test.mjs` | PASS. |
| `legacy-recovery.test.mjs` | PASS. |

Todos usan DB ficticia/temporal. Los verificadores son programas ejecutables; no son tests que `dotnet test` descubra. Los dos verificadores HTTP ahora aceptan `AQUARELLA_VERIFICATION_URL`, conservando sus URLs predeterminadas, para ejecutarlos sin ocupar el puerto del usuario. Se usó 18191 exclusivamente durante esas pruebas y luego se cerró esa instancia.

Evidencia conservada fuera de Git, bajo `%TEMP%`:

- `aquarella-data-audit-95341cb3a9ae401cb7fbcf330538fa15`: DB ficticia, backup, claves de prueba y manifiesto de cuenta de desarrollo.
- `aquarella-hosting-1e8ea26d1b6741b69f7664aa1715a038`: prueba nativa de Production/reinicio.
- `aquarella-data-audit-http-ae3fbcccd936452888bb54f93cc125f3`: pruebas HTTP aisladas y logs.

No se abrieron ni editaron las bases reales desde los verificadores. No se detuvieron los procesos del usuario en 7188/18188. No se hizo un recorrido visual de navegador durante esta auditoría.

## 16. Build

El primer build a la salida habitual encontró el ejecutable de Aquarella bloqueado por la instancia abierta desde Visual Studio: MSB3026/MSB3027/MSB3021 de copia, no errores del código. Se mantuvo esa instancia abierta.

La compilación final del mismo proyecto con salida aislada pasó:

```powershell
dotnet build --artifacts-path "$env:TEMP\aquarella-data-audit-build"
```

**0 errores, 0 warnings**. También pasó publish Release a TEMP. Los cambios no introdujeron warnings de compilación. Git informa únicamente conversión LF/CRLF configurada en el repositorio; `git diff --check` no detectó problemas de whitespace.

## 17. Archivos modificados

Código de aplicación, exclusivamente:

- `Aquarella/Services/BusinessData.cs`: validación de Perfil y replay seguro de recibos.
- `Aquarella/Services/Accounts/AccountService.cs`: validación de identidad y ownership de revocación de sesión.

Verificación/documentación:

- `tools/PersistenceVerification/PersistenceVerification.csproj` — nuevo, sin nuevas dependencias.
- `tools/PersistenceVerification/Program.cs` — nuevo verificador.
- `tools/ProductionHostingVerification/Program.cs`: fixtures persistentes A/B, restart y cambio de cuenta.
- `tools/AccountHttpVerification/Program.cs`: URL de prueba configurable.
- `tools/DevelopmentAccessVerification/Program.cs`: URL de prueba configurable.
- `AUDITORIA-PERSISTENCIA-2026-10-05.md`: este informe; no sustituye los anteriores.

No se modificaron componentes Razor, CSS, JS de la aplicación, esquema/migraciones ni infraestructura.

## 18. Estado Git

Rama `main`, HEAD `7ac5d11` conservado. Sin commit, push, staging, pull, merge, rebase ni cambio de rama. Quedan cinco archivos versionados modificados, el verificador nuevo y este informe nuevo. Los siete PNG previos permanecen sin seguimiento y no se descartó ningún cambio del usuario. Los artefactos TEMP no se incluyeron en el repositorio.

## 19. Checklist manual pendiente

Realizar con dos cuentas ficticias en un entorno de prueba, sin borrar/modificar datos reales:

1. Entrar como A, abrir Perfil/Stock/Precios/Agenda y comprobar sus datos. Logout → B: comprobar que no aparezcan nombre, logo, contactos, productos ni notas de A; repetir B → A. Observar también la transición inicial antes de terminar la carga.
2. Mantener una segunda pestaña de A abierta, cerrar sesión desde la primera y entrar como B. En la pestaña vieja intentar navegar y guardar: debe exigir autenticación y no escribir en el negocio de B. Probar botón Atrás y recarga.
3. Recargar/cerrar y volver a abrir durante una sesión válida: comprobar mismos datos, notas editadas y venta manual; cerrar sesión debe impedir volver a rutas internas con la cookie revocada.
4. En la instancia aislada, interrumpir temporalmente la conexión durante una carga confirmada y reconectar; comprobar que no se repita la carga ni se muestre éxito falso. Los tests de rollback/cancelación no equivalen a desconectar SignalR en el navegador.
5. Antes de cambiar la política de Perfil, revisar con dos pestañas el conflicto ya reproducido: una cambia nombre y otra color usando un formulario desactualizado. Definir cómo debe avisar/resolver ese conflicto.

Estos pasos permanecen pendientes; no se presentan como pruebas manuales realizadas.
