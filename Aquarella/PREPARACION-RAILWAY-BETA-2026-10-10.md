# Preparación de Aquarella para beta privada en Railway

Fecha: 2026-10-10. Rama: `main`. No se hizo commit, push ni despliegue.

## Estado

La primera etapa de preparación de código y pruebas nativas está completa. La
ampliación al final de este informe documenta la segunda revisión y el procedimiento
disponible para cuentas privadas. Todavía NO se considera listo el despliegue.
El perímetro de confianza ya fue aprobado e implementado como modo opt-in.
Falta confirmar Host HTTP/WebSocket real y comprobar la imagen Docker/Linux,
y autorizar/preparar las cuentas reales mediante el procedimiento controlado.
No se creó infraestructura ni se compró dominio. Se utilizará el dominio gratuito
de Railway, una sola instancia y un volumen persistente en `/data`.

Había cambios pendientes de correo, autenticación y mantenimiento al comenzar.
Se conservaron. La base local no fue modificada, borrada, migrada ni exportada.
No se consultaron User Secrets ni se enviaron correos reales.

**Estado actualizado:** Railway confirmó al propietario que no ofrece una allowlist
estable ni autenticación criptográfica del edge. La tercera revisión al final
sustituye las solicitudes anteriores de IP/CIDR. El propietario aprobó después
el entorno dedicado; la cuarta revisión documenta el modo implementado, apagado
por defecto y con rechazo de Host/headers inválidos.

## Cambios implementados

### HTTPS y proxy

- Se conserva el procesamiento temprano de `X-Forwarded-Proto` y `X-Real-IP`
  exclusivamente desde proxies/IP networks configurados; no se acepta host reenviado.
- La lista se valida antes de crear almacenamiento o abrir el servidor. Lista vacía,
  direcciones inválidas y redes `/0` detienen el arranque. Se mantienen deshabilitados
  los forwarded headers indiscriminados del framework.
- Un único salto y headers simétricos: no se acepta un header de esquema aislado.
- HTTP de aplicación en Production sigue rechazado; solo GET `/health` funciona
  por HTTP para Railway. Cookies de autenticación conservan Secure, HttpOnly,
  SameSite=Lax, Path=/ y prefijo __Host, sin Domain. Login/Logout y CSRF no se relajan.
- **Pendiente:** no se obtuvo una IP/CIDR verificada del proxy real del servicio.
  Se solicitó ese dato al propietario. No se eligió un rango privado supuesto ni
  se confió en todo Internet para hacer arrancar la aplicación.

### SQLite y Data Protection

- Se conservan rutas explícitas dentro del volumen, esquema validado en lectura,
  mantenimiento con backup SQLite/WAL e inicialización/migración expresamente autorizada.
- Production adquiere una reserva exclusiva de aplicación sobre la base: una
  segunda instancia o mantenimiento concurrente se rechaza. Development conserva
  las reservas compartidas existentes.
- El volumen y el directorio de claves reciben permisos 0700 en Linux. Se rechaza
  la raíz del filesystem como almacenamiento y una base dentro del directorio de claves.
- Data Protection mantiene el ApplicationName estable `Aquarella` y claves persistentes.
  Las cookies no expiradas y datos sobrevivieron a reinicios de proceso en la prueba.
  Los circuitos Blazor en memoria no sobreviven a reinicios y deben reconectarse.
- El key ring persiste en archivos privados; no se agregó cifrado XML nuevo. Volumen
  y backups deben tratarse como secretos y revisarse sus permisos/cifrado del hosting.
  Backup SQLite no incluye key ring; ambos requieren copia privada fuera del volumen.

### Beta privada

- Nuevo `Accounts:Registration:Enabled`: default false en Production, true en Development.
- La página no ofrece el formulario/link de registro cerrado y rechaza POST directo
  con CSRF válido. El servicio también rechaza creación aunque haya correo disponible.
- Si se reabre Production pero no hay correo disponible, el servicio no crea cuentas.
- No se modifica hashing, verificación, autenticación, logout, sesiones ni ownership.
- No se genera administrador, no se marca EmailVerified manualmente, no hay bypass público.
- Sin correo de Production, recuperación y reenvíos siguen indisponibles. Son restricciones
  reversibles, no funcionalidades eliminadas. Gmail SMTP de Development queda intacto.

### Docker, Linux y health

- Docker multistage SDK/runtime .NET 10, sin SDK en runtime. Se publica también
  DatabaseMaintenance de la misma versión en `/maintenance/DatabaseMaintenance.dll`.
- CMD normal: `dotnet /app/Aquarella.dll`. El comando alternativo de mantenimiento
  es explícito y nunca se ejecuta automáticamente durante build o inicio normal.
- Se excluyen del contexto SQLite/journals, logs, backups, locks y recibos; solo se
  incorpora el código del proyecto y de la herramienta de mantenimiento requerida.
- `PORT` de Railway controla HTTP interno en `0.0.0.0`; no se cambió launchSettings.
- `/health` comprueba lectura del historial EF de la base validada; devuelve 503
  ante una dependencia inválida, sin crear/reparar archivos ni exponer datos.
- Una réplica, sin Serverless/suspensión, healthcheck `/health`. Ajustes de Railway
  se efectuarán manualmente después de aprobación, no desde esta tarea.
- Docker no está disponible en PATH en esta PC. Compatibilidad revisada en fuentes
  y publicación; ejecución real de imagen Linux NO verificada.

## Variables manuales de Railway

```text
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ProductionStorage__Root=/data
ConnectionStrings__Aquarella=Data Source=/data/aquarella.db
DataProtection__KeysPath=/data/data-protection-keys
Accounts__PublicOrigin=https://DOMINIO-REAL.up.railway.app
AllowedHosts=DOMINIO-REAL.up.railway.app;healthcheck.railway.app
Accounts__Registration__Enabled=false
Email__Smtp__Enabled=false
ReverseProxy__RailwayDedicatedEnvironmentEnabled=true
```

La última variable activa expresamente el modo dedicado aprobado; no activarlo
en redes compartidas. Railway confirmó que no proporciona una IP/CIDR estable.
No configurar KnownProxies/KnownNetworks, un placeholder ni el rango 100.64.0.0/10
en este modo. No configurar `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
Railway proporciona `PORT`, `RAILWAY_SERVICE_ID` y `RAILWAY_VOLUME_MOUNT_PATH`;
no inventar ni falsificar esos valores. Agregar volumen real en `/data`.
No copiar la contraseña Gmail, certificados locales o User Secrets a Production.
No se necesita credencial de correo/API en esta etapa.

## Inicialización y actualización

Procedimiento detallado y comandos: `DEPLOYMENT.md`, sección de beta privada.

1. Autorizar imagen/servicio, volumen y dominio. Confirmar proxy y las rutas.
2. Detener la aplicación y demás escritores. Railway no monta el volumen durante
   build ni pre-deploy: NO ejecutar allí la migración.
3. Usar comando alternativo de mantenimiento, con volumen montado, healthcheck
   desactivado y restart Never durante la operación. Inspeccionar primero.
4. Para una base nueva autorizada: migrate con --create, --confirm-migrate y
   --confirm-stopped. Para actualizar una existente: sin --create.
5. Verificar backup/restauración aislada, recibo, integridad y datos conservados.
   No restaurar automáticamente sobre el archivo original si hay un error.
6. Respaldar también las claves de Data Protection y guardar copias fuera del volumen.
7. Restaurar CMD normal, `/health`, una instancia y restart On Failure.

No se ejecutó este procedimiento contra Railway ni la base local.

## Cuentas privadas: estado de la primera etapa

Una base inicializada no contiene cuentas. No se agregó un mecanismo automático
de altas ni se modificó autenticación para evitarlo. Antes de invitar usuarios
hay que autorizar qué cuentas/datos se incluirán y cómo se prepararán.

Alternativa documentada: una base SEPARADA de beta, en Development exclusivamente
LOCAL; el propietario registra/verifica cuentas mediante el flujo actual y Gmail,
detiene la instancia y realiza backup verificado. Luego una operación autorizada
restaura a un archivo nuevo del volumen y lo inspecciona antes del primer arranque.
No copiar la base local actual, trasladar sesiones activas como bypass ni publicar
Development. Queda pendiente el procedimiento posterior de altas/recuperación
privadas hasta habilitar el proveedor HTTPS de correo.

## Pruebas ejecutadas

Todas las regresiones finales ejecutadas pasaron. Se usaron bases/usuarios ficticios
y procesos aislados; solo se terminaron procesos creados por sus verificadores.

| Verificador | Resultado |
|---|---|
| DatabaseVerification | PASS: Perfil/reintento, importación, módulos, cuentas, tokens, aislamiento |
| ReliabilityVerification | PASS: concurrencia, es-AR, datos malformados, navegación/desconexión simulada |
| AiSettingsVerification | PASS: 20 comprobaciones |
| StockIntakeVerification | PASS: integridad, rollback, idempotencia, aislamiento |
| MetricsVerification | PASS: 20 comprobaciones |
| MetricsPresentationVerification | PASS: 64 comprobaciones |
| NoticesVerification | PASS: 17 comprobaciones |
| ProductVisualVerification | PASS: clasificación, assets y renderizado |
| PersistenceVerification | PASS: aislamiento A/B, Perfil/Precios/Stock/Agenda, backup y nuevos procesos |
| AccountMailVerification | PASS: 91 comprobaciones, incluida política privada y error sin correo |
| SmtpProbeVerification | PASS: 31 comprobaciones; matriz de transportes y rechazo seguro; sin envío real |
| AccountHttpVerification | PASS: registro/verificación/reset Development, cookies, CSRF, rate limiting |
| DevelopmentAccessVerification | PASS: cookie real, logout, rutas, datos y tutorial sin duplicación |
| MigrationSafetyVerification | PASS: 83 comprobaciones; backups/WAL, esquema, rechazo del arranque, exclusión e integridad |
| SecurityVerification | PASS: rutas, sesiones, CSRF, XSS, cookies, errores, logs y aislamiento |
| ProductionHostingVerification | PASS: proxy confiable/no confiable, headers incompletos, cookies, registro privado GET/POST, segundo proceso rechazado, health 503/recuperación, reinicios y datos |
| metrics-motion.test.mjs | PASS: 1 test, 0 fallos |
| DatabaseMaintenance publicado, --help | PASS, sin abrir bases |

Build final `dotnet build Aquarella/Aquarella.slnx --no-restore`: **0 errores, 0 warnings**.
Publicación Release de app/herramienta y compilaciones finales de verificadores:
sin errores ni warnings nuevos. No se actualizaron versiones ni se agregaron paquetes.

Incidencias de la ejecución:
- El sandbox de Windows produjo CryptographicException y bloqueó pruebas HTTP locales.
  Las mismas pruebas, con artefactos temporales fuera del sandbox, pasaron.
- NuGet reportó NU1900 por la auditoría de vulnerabilidades inaccesible/cacheada.
  Restauración --force con acceso autorizado resolvió el problema sin suprimir warnings.
- DatabaseVerification incluye una comparación opcional automática de la base local
  con un backup histórico si se ejecuta desde la raíz. Esa comparación difería tras
  ediciones posteriores y no sirve como regresión de esta tarea. No se modificó el
  verificador ni la base: se repitió desde un cwd temporal y pasaron sus fixtures.
  Esa primera comparación fue solo lectura y no imprimió datos.
- Se corrigió la ruta incorrecta al ejecutable auxiliar DevelopmentAccessVerification
  dentro del runner HTTP. Se aisló APPDATA/User Secrets para esa instancia ficticia.

Gmail real no fue probado de nuevo: se conservó su código/configuración, se probó
la selección y TLS con verificadores y no se consultaron credenciales ni se enviaron
emails. SignalR real con el proxy público y Docker/Linux quedan para prueba manual.

## Archivos tocados en ESTA tarea

1. `/Dockerfile`.
2. `/.dockerignore`.
3. `Aquarella/Aquarella/Program.cs`.
4. `Aquarella/Aquarella/Services/ProductionHosting.cs`.
5. `Aquarella/Aquarella/Data/DatabaseMigrationSafety.cs` (ya estaba pendiente, se conservó).
6. `Aquarella/Aquarella/Services/Accounts/AccountService.cs`.
7. Nuevo `Aquarella/Aquarella/Services/Accounts/AccountRegistrationPolicy.cs`.
8. `Aquarella/Aquarella/Pages/Access.cshtml.cs`.
9. `Aquarella/Aquarella/Pages/Access.cshtml`.
10. `Aquarella/tools/AccountMailVerification/Program.cs`.
11. `Aquarella/tools/MigrationSafetyVerification/Program.cs`.
12. `Aquarella/tools/ProductionHostingVerification/Program.cs`.
13. `Aquarella/tools/AccountHttpVerification/Isolated.cs`.
14. `Aquarella/DEPLOYMENT.md`.
15. Este informe.

No se tocaron SMTP, migraciones, modelos de datos, launchSettings, módulos, diseño,
paquetes ni configuración con secretos. Git permanece en `main`, con cambios locales
de esta tarea y los anteriores conservados. `git diff --check` pasó. Sin commit/push.

## Siguiente paso

Revisar los cambios, confirmar con Railway la identidad/rango del proxy inmediato,
probar Docker Linux y acordar las cuentas iniciales verificadas. Solo después pedir
autorización de despliegue y ejecutar el mantenimiento explícito con volumen real.
No publicar afirmando que las validaciones nativas sustituyen esas comprobaciones.

Fuentes:
- [Railway: HTTPS y headers](https://docs.railway.com/networking/public-networking/specs-and-limits).
- [Railway: volumen persistente](https://docs.railway.com/volumes).
- [Railway: healthcheck y downtime con volumen](https://docs.railway.com/deployments/healthchecks).
- [Railway: comandos alternativos](https://docs.railway.com/deployments/start-command).
- [ASP.NET Core: proxies confiables](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

## Ampliación: segunda revisión de los tres pendientes (2026-10-10)

Esta sección conserva la evidencia histórica. Los pendientes que pedían una
allowlist Railway quedan sustituidos por la tercera revisión siguiente.

### Resultado ejecutivo

**Todavía NO está autorizable el primer despliegue.** La preparación nativa está
probada y hay un procedimiento controlado para el primer lote de cuentas, pero
la identidad del proxy real y la ejecución Docker/Linux siguen sin acreditarse.
No se sustituyeron esas comprobaciones por configuraciones permisivas.

Se trabajó en `main`, conservando los cambios previos. No hubo commit, push,
despliegue, envío real, cuenta real creada, acceso a User Secrets o migración de
la base local. No se modificaron autenticación, SMTP, esquema, Dockerfile,
launchSettings, módulos ni diseño en esta segunda revisión.

### 1. Problemas y evidencia

| Pendiente | Hallazgo | Estado actual |
|---|---|---|
| Proxy Railway | Headers documentados no equivalen a una identidad autenticada del peer inmediato de Kestrel. No se encontró allowlist IP/CIDR estable publicada ni alternativa de upstream autenticado sin IP documentada. | Pendiente de confirmación Railway; rechazo seguro conservado. |
| Docker/Linux | Docker no está en PATH ni en su ubicación habitual. La publicación y pruebas Windows no prueban librerías SQLite, locking y permisos dentro del contenedor Linux. | Revisión estática/nativa aprobada; ejecución Linux pendiente. |
| Cuentas privadas | Base nueva vacía, registro Production cerrado y sin correo. Cambiar EmailVerified manualmente alteraría la acreditación existente. | Procedimiento con verificación real disponible; no se creó una puerta trasera administrativa. |

Se revisaron las páginas oficiales de especificaciones, Edge Networking, Edge Rules,
volúmenes, soporte por plan y Forwarded Headers de Microsoft. La arquitectura de
edge distingue entrada pública y recorrido interno, sin dar la allowlist que
necesita esta configuración. No confundir IP anycast, POP, DNS o IP saliente con
RemoteIpAddress del salto inmediato. Las reglas documentadas no ofrecen inyectar
un secreto de origen como solución de este requisito.

Se agregó en DEPLOYMENT una consulta concreta para Central Station y la forma de
medir el peer, únicamente en infraestructura ficticia autorizada, sin cookies,
tokens ni cuerpos. Hobby tiene soporte comunitario sin garantía de respuesta;
no se promete un ticket privado. No se publicó ninguna consulta ni se contactó
a soporte en nombre del propietario.

### 2. Soluciones implementadas

- `DatabaseMaintenance inspect-private-beta`: inspección explícita de SOLO LECTURA,
  sin fallback de ruta/configuración. Primero verifica esquema/integridad y después
  obtiene una instantánea consistente de conteos de usuarios verificados/pendientes,
  cuentas incompletas, sesiones activas, tokens utilizables y correos pendientes.
  No entrega datos personales, hashes, claves o tokens. No crea cuentas, marca
  verificación, cancela correos, borra sesiones o migra. Una base inexistente falla
  sin crearse. Flags de escritura son rechazados. Exit codes: 0 lista, 2 pendientes,
  1 error. Ready no certifica toda la seguridad de la aplicación ni evita cambios
  de otro escritor: detener la instancia antes del control y del respaldo.
- Procedimiento concreto para una base NUEVA e independiente de beta en Development:
  titulares autorizados registran y verifican por Gmail con el flujo actual,
  cierran todas sus sesiones, se detiene la instancia, se verifica preflight y se
  respalda/restaura con SQLite/WAL. La restauración se destina a un archivo NUEVO
  del volumen; Production mantiene registro cerrado, SMTP apagado y key ring nuevo.
  No se traslada la base actual, cookies, certificados ni claves de Development.
- Nuevo verificador de este flujo con cuentas ficticias, correo en memoria y bases
  temporales: el hash y la verificación sobreviven al respaldo/restauración y el
  login existente no depende del key ring local. Incluye ejecución real del CLI
  publicado, lectura sin cambios y rechazo de estados no listos.
- Se amplió el verificador de hosting: rechaza el flag de forwarded headers
  indiscriminado, comprueba que un host reenviado falsificado no controle la
  redirección de login, rechaza esquema HTTP incluso desde proxy confiable y
  contiene comprobaciones de permisos 0700 para cuando se ejecute en Linux.

El propietario confirmó posteriormente (2026-10-10) la elección de verificar
mediante Gmail en una base separada de Development. Queda resuelta la decisión
del procedimiento; la creación de cuentas reales y el envío siguen requiriendo
autorización separada. No se implementó acreditación manual del correo.
No fue necesaria otra herramienta de creación de cuentas
para ese procedimiento: se usa la pantalla de registro existente en una instancia
local separada, nunca una ruta de provisioning en Production.

### 3. Límites del procedimiento privado

La primera cuenta del propietario puede prepararse en esta PC con su correo real
después de una autorización separada. Un invitado remoto no puede abrir el enlace
localhost desde su propio teléfono: el procedimiento requiere participación local
en la instancia de preparación. No es onboarding remoto sin correo de Production.
No se inventaron usuarios o contraseñas reales. Contraseñas se introducen en el
formulario, no en argumentos, configuración, logs o comandos administrativos.

El traslado privado del backup a Railway y las cuentas concretas aún requieren
autorización; no se implementó/probó transferencia ni se exportó la base local.
El procedimiento es para el primer lote, NO para reemplazar una base activa cada
vez que se invite a alguien. Altas posteriores y recuperación remota mientras no
hay correo requieren una decisión independiente o el proveedor HTTPS futuro.
No copiar claves locales para recuperar sesiones: el login de los titulares usa
su contraseña y emite sesiones nuevas. SQLite y sus backups contienen hashes y
datos personales; conservarlos con acceso privado y fuera de Git/logs.

### 4. Docker y Linux

No fue necesario modificar el Dockerfile existente. Se comprobaron estáticamente
net10.0 y las imágenes SDK/ASP.NET 10, project references transitivas, publicación
de app y mantenimiento separados, uso de rutas absolutas, exclusiones del contexto,
PORT, health, almacenamiento y ausencia de migración en CMD. El nuevo verificador
no entra en el contexto; el helper de lectura sí se publica como código normal.

El usuario runtime predeterminado root y chmod 0700 son coherentes con la escritura
actual del volumen; validar esto efectivamente en Linux sigue pendiente. No se
implementó un cambio a usuario sin privilegios que pudiera romper el montaje.
Persistencia de datos, cookies no expiradas y keys pasó en procesos Windows
temporales. La reserva exclusiva pasó con dos procesos Windows. Permisos y locking
Linux, imagen Docker, TLS real del edge y circuito SignalR público NO se ejecutaron.

DEPLOYMENT contiene comandos para un volumen Docker NUEVO, health con Host correcto,
revisión de permisos, reinicio y segundo proceso; también comandos en Linux con
SDK 10 para correr los verificadores sobre una publicación aislada. No se instaló
Docker ni se modificó la configuración global de Windows.

### 5. Pruebas finales

**17 verificadores .NET aprobados**, incluyendo DevelopmentAccessVerification
como hijo del runner HTTP:

DatabaseVerification; ReliabilityVerification; AiSettingsVerification (20);
StockIntakeVerification; MetricsVerification (20); MetricsPresentationVerification
(64); NoticesVerification (17); ProductVisualVerification; PersistenceVerification;
AccountMailVerification (91); SmtpProbeVerification (31); PrivateBetaVerification
(29); AccountHttpVerification; DevelopmentAccessVerification;
MigrationSafetyVerification (83); SecurityVerification; ProductionHostingVerification.

Todos se recompilaron y ejecutaron con fixtures propios. DatabaseVerification se
ejecutó desde cwd temporal para no leer su comparación opcional de la base local.
Se iniciaron y terminaron exclusivamente los servidores temporales de verificadores.
HTTP de Development, registro/verificación/reset, CSRF, cookies, logout, aislamiento,
Precios/Perfil/concurrencia, Stock, Agenda, datos tras reinicios y backups pasaron.

Prueba JavaScript `metrics-motion.test.mjs`: **1 test, 0 fallos**.
Publicación Release de app y DatabaseMaintenance: aprobada. CLI publicado probado
con bases ficticias; comandos de escritura en inspect-private-beta rechazados.
Build final `dotnet build Aquarella/Aquarella.slnx --no-restore`: **0 errores, 0 warnings**.

Incidencias del entorno, resueltas sin cambiar paquetes o suprimir warnings:
- NU1900 al restaurar el nuevo verificador por conectividad del sandbox. Restore
  autorizado con acceso NuGet resolvió la auditoría y todas las compilaciones
  finales quedaron sin warnings.
- MSBuild del proyecto transitivo SmtpProbeVerification falló sin diagnóstico
  dentro del sandbox. La misma compilación fuera de él pasó con 0/0; las pruebas
  criptográficas/HTTP se ejecutaron también en el contexto autorizado aislado.

SMTP conserva su código y configuración previos. Se verificaron selección de
transporte y STARTTLS con simulaciones; NO se probó de nuevo la entrega Gmail real,
no se consultaron credenciales y no se envió correo real.

La base local `App_Data/aquarella.db` conserva longitud 135168 y LastWriteTimeUtc
2026-10-09 03:31:22, iguales al inicio. No se abrió con los verificadores ni se
migró. Esta comprobación de metadatos acompaña el aislamiento de los tests, no es
una auditoría de contenido de la base real.

### 6. Archivos de ESTA segunda revisión

1. Nuevo `Aquarella/Aquarella/Data/PrivateBetaReadiness.cs`.
2. `Aquarella/tools/DatabaseMaintenance/Program.cs` (se conservó la herramienta pendiente).
3. Nuevo `Aquarella/tools/PrivateBetaVerification/PrivateBetaVerification.csproj`.
4. Nuevo `Aquarella/tools/PrivateBetaVerification/Program.cs`.
5. `Aquarella/tools/ProductionHostingVerification/Program.cs` (ampliación sobre lo pendiente).
6. `Aquarella/DEPLOYMENT.md`.
7. Este informe, conservando la evidencia de la etapa anterior.

Git sigue en `main`, con cambios locales anteriores y estos agregados. `git diff
--check` aprobado. No staging, commit, push, pull, merge, rebase o cambio de rama.

### 7. Configuraciones manuales y orden siguiente

Se conservan las variables de la tabla de esta primera etapa. La única alternativa
de ruta de DB es `/data/beta-initial.db` si se restaura el primer lote a ese archivo
nuevo; elegir su ConnectionStrings explícitamente, no sobrescribir otra base.
Faltan dominio real/AllowedHosts, volumen real, rutas, proxy acreditado, una réplica,
health `/health`, sin suspensión y mantenimiento explícito con servicio detenido.
Mantener `Accounts__Registration__Enabled=false`, `Email__Smtp__Enabled=false` y
ambos entornos Production. Railway aporta PORT y variables de volumen; no falsificarlas.
No se necesitan secretos SMTP de Production ni Resend en esta etapa.

Prioridad:
1. Obtener confirmación de Railway del peer/rango o autenticación alternativa; la
   consulta exacta está en DEPLOYMENT. No publicar con trust-all mientras se espera.
2. Ejecutar Docker/Linux en un entorno autorizado, siempre con volumen nuevo y
   datos ficticios. Revisar TLS/SignalR real antes de invitar personas.
3. Autorizar el primer lote/canal privado de traslado, preparar/verificar sus
   cuentas y el respaldo; aceptar las limitaciones de alta/recuperación sin correo.
4. Revisar cambios locales; solo después autorizar Git y posteriormente despliegue.

Fuentes adicionales de esta revisión:
- [Arquitectura edge](https://docs.railway.com/networking/edge-networking).
- [Acciones documentadas de Edge Rules](https://docs.railway.com/networking/edge-rules).
- [Canales de soporte según plan](https://docs.railway.com/platform/support).

## Ampliación: tercera revisión, respuesta de Railway (2026-10-10)

Evidencia histórica: se conservan propuesta, bloqueo y resultados previos. El
propietario autorizó posteriormente ese perímetro bajo condiciones explícitas;
la implementación vigente se describe en la cuarta revisión siguiente.

### Resultado y problema encontrado

**El proxy HTTPS todavía NO quedó configurado para Railway.** El código conserva
el rechazo seguro. La respuesta aportada por el propietario aclara que la
allowlist estable que requería la preparación anterior no existe y que tampoco
hay headers firmados o mTLS del upstream. No corresponde seguir pidiendo esa IP.
100.64.0.0/10 no es utilizable como KnownNetworks, aunque una observación coincida.

La respuesta transcrita acredita el reemplazo de headers por el edge público y
advierte que un servicio del mismo proyecto/entorno puede conectarse directamente
y falsificarlos. La documentación pública revisada confirma TLS, los headers y
el aislamiento por entorno, pero no se afirma que por sí sola publique todos los
detalles de la respuesta privada aportada. No se contactó soporte desde esta tarea.

### Auditoría del código actual

| Área | Evidencia y resultado |
|---|---|
| Forwarded headers | ProductionHosting.Configure exige proxies/redes explícitos antes de crear almacenamiento. Vaciar listas no es una solución segura. Solo procesa Proto e IP, con X-Real-IP como origen de IP, ForwardLimit=1 y simetría. No procesa host reenviado. |
| Orden | UseForwardedHeaders se registra antes de HSTS, control HTTPS, redirecciones, autenticación y rate limiting. No necesita reordenarse. |
| HTTPS | Kestrel recibe HTTP interno. Sin esquema HTTPS aceptado, Production rechaza solicitudes de aplicación con 400 antes de UseHttpsRedirection. No hay solución mediante un bucle de redirects. |
| Cookies | __Host-Aquarella.Auth mantiene Secure=Always, HttpOnly, SameSite=Lax, Path=/ y sin Domain. TempData conserva Secure=Always/HttpOnly. No se reduce seguridad para acomodar HTTP interno. |
| Login/logout | Login correcto redirige a /; logout a /login. El challenge del framework necesita esquema y Host correctos. No se introduce returnUrl externo. |
| URLs absolutas | AccountMailOrigin toma Accounts:PublicOrigin en Production. Valida origen HTTPS sin path/query/fragment/userinfo. No utiliza X-Forwarded-Host para enlaces. |
| Headers necesarios | X-Forwarded-Proto para Request.IsHttps; X-Real-IP para el rate limit existente por cliente. X-Forwarded-For/Host/Prefix y Forwarded no son necesarios como entradas de confianza. |
| Falsificación | El edge sustituye headers según la respuesta suministrada; una conexión directa interna puede copiar esos mismos headers. Formato, simetría, una IP privada o AllowedHosts no acreditan al edge. |
| Health | GET /health precede a la exigencia HTTPS/auth. Consulta SQLite en solo lectura, devuelve 200/503 y no datos de cuentas; no crea ni migra. POST no recibe esa excepción. |
| Persistencia/privacidad | SQLite, key ring y reserva exclusiva se conservan. Registro Production continúa cerrado y Development/SMTP no cambian. |

Archivos inspeccionados: Services/ProductionHosting.cs, Program.cs,
Services/Accounts/AccountMailOptions.cs, AccountEmail.cs, Access.cshtml.cs,
Account.cshtml.cs, componentes de redirección, appsettings.json, Dockerfile y
verificadores de hosting/cuentas. No se leyó User Secrets ni configuración privada.

### Solución propuesta que requiere decisión

Modo reversible específico para un entorno dedicado de Railway en Production,
desactivado por defecto y separado del modo existente de proxies con identidad
IP conocida. Antes de aceptar headers, validar configuración explícita y rechazar
ausencia/multiplicidad/formato inválido. Exigir un único Proto=https y un único
Real-IP válido. No aceptar el host reenviado ni cadenas de múltiples saltos;
conservar origen fijo, AllowedHosts, CSRF y cookies. Health conserva su excepción
limitada de lectura. No agregar paquetes ni alterar autenticación o SQLite.

El límite aprobado sería: un servicio Aquarella/una instancia, entorno privado
sin otros servicios conectados, administración restringida, sin TCP proxy público
del puerto interno. La aplicación NO puede probar automáticamente esas condiciones
con RAILWAY_SERVICE_ID o una variable declarativa. El modo aceptaría técnicamente
headers válidos de cualquier peer que alcance ese puerto; el perímetro de Railway
debe limitar quién puede hacerlo. No se presenta como autenticación del edge.

**Riesgo residual:** un servicio agregado al mismo entorno o un recurso comprometido
con acceso directo podría falsificar HTTPS/IP y alterar rate limits. No obtiene
por ello una cookie válida, pero rompe la suposición de origen de esos headers.
WireGuard protege el transporte sin distinguir el recorrido público del privado.
Hay que revisar este modo ANTES de agregar servicios; aislar servicios no confiables
en otro entorno. No aceptar ese límite significa mantener el rechazo y no desplegar
hasta disponer de upstream autenticado o una restricción de red verificable.

El adjunto exige: “Si la solución requiere aceptar un límite de confianza [...]
consultarme antes de implementarlo”. Por ello la propuesta está documentada pero
NO se cambió el código ni se inventó una configuración activable antes de aprobación.

### Cambios de esta tercera revisión

Solo documentación:

1. DEPLOYMENT.md: estado vigente, retiro del paso de IP/CIDR inexistente, propuesta
   concreta, condiciones/riesgo, variables y verificaciones posteriores.
2. PREPARACION-RAILWAY-BETA-2026-10-10.md: actualización de estado y esta ampliación.

Se conservan las revisiones anteriores como evidencia histórica, identificando
cuáles de sus instrucciones fueron sustituidas. Código, configuración, Dockerfile,
SMTP, datos, migraciones, autenticación y diseño no cambiaron en esta revisión.

### Variables de Railway y consulta restante

Continúan: ASPNETCORE_ENVIRONMENT=Production, DOTNET_ENVIRONMENT=Production,
ProductionStorage__Root=/data, ConnectionStrings__Aquarella con ruta bajo /data,
DataProtection__KeysPath=/data/data-protection-keys, Accounts__PublicOrigin con
dominio HTTPS real, AllowedHosts con dominio real y healthcheck.railway.app,
Accounts__Registration__Enabled=false y Email__Smtp__Enabled=false.
Railway aporta PORT y variables de servicio/montaje. No agregar Gmail/User Secrets.

Estas variables NO resuelven el requisito del proxy del código actual. No hay
una nueva variable de modo implementada. No usar 100.64.0.0/10, IP observada ni
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true. La tabla anterior ya no contiene el
placeholder de IP Railway que no puede configurarse válidamente.

No hace falta volver a consultar IP/CIDR/mTLS: ya fueron respondidos. Sí confirmar
que el Host original público llega al HTTP y al handshake WebSocket; la respuesta
habla de X-Forwarded-Host, no establece explícitamente el Host original. La app
depende de Host/AllowedHosts para routing y challenge. Puede comprobarse en una
prueba de infraestructura autorizada o preguntarse a Railway. Si no se conserva,
requiere evaluar host canónico; no aceptar Host reenviado a ciegas.

### Docker/Linux y siguiente paso

Docker sigue ausente de PATH en esta PC. Se puede continuar con pruebas Linux de
la imagen actual y proxy local controlado, volumen NUEVO y usuarios ficticios.
No se ejecutaron Docker/Linux, TLS público Railway ni SignalR del edge en esta
revisión. Esa prueba local puede avanzar sin inventar una allowlist para Railway.

Siguiente paso concreto: el propietario acepta o rechaza explícitamente el
perímetro descrito. Si lo acepta, implementar el modo limitado y sus tests antes
de desplegar. Verificar spoofing desde cliente público con proxy que reemplaza
headers y documentar que un peer interno que imita el edge es indistinguible.
Después Docker/Linux y, únicamente con autorización posterior, pruebas Railway.
La elección de Gmail en una base separada sigue vigente; no autoriza envíos ni
cuentas reales en esta tarea. No publicar todavía.

### Verificaciones ejecutadas en esta tercera revisión

Se recompilaron los 17 proyectos de verificación sin errores ni warnings, y se
publicaron app/DatabaseMaintenance en Release. Se ejecutaron los 16 runners
principales, con DevelopmentAccessVerification ejecutado como hijo de
AccountHttpVerification: **17 verificadores .NET aprobados**.

DatabaseVerification, ReliabilityVerification, AiSettingsVerification,
StockIntakeVerification, MetricsVerification, MetricsPresentationVerification,
NoticesVerification, ProductVisualVerification, PersistenceVerification,
AccountMailVerification, SmtpProbeVerification, PrivateBetaVerification,
AccountHttpVerification, DevelopmentAccessVerification, MigrationSafetyVerification,
SecurityVerification y ProductionHostingVerification.

Los procesos HTTP se iniciaron con publicaciones aisladas, cuentas ficticias,
bases temporales y configuración de pruebas; solo se cerraron sus propios procesos.
El cwd temporal evitó la lectura opcional de la base real de DatabaseVerification.
No se inició la instancia habitual de Aquarella ni se enviaron correos reales.

ProductionHostingVerification aprobó confianza loopback controlada/rechazo del
peer no autorizado, headers incompletos, esquema HTTP rechazado, host reenviado
falso ignorado, cookies Secure/HttpOnly/SameSite, CSRF, registro cerrado GET/POST,
rate limiting, health 200/503, reserva exclusiva y cookies/datos tras reinicio.
Esto prueba el modo ACTUAL de allowlist; NO prueba un modo Railway aún inexistente.
Development HTTP y selección SMTP/simulador aprobados con transportes de prueba;
la entrega Gmail real no se ejecutó ni se consultaron secretos.

JavaScript: metrics-motion.test.mjs y legacy-recovery.test.mjs, ambos aprobados.
Build de solución: dotnet build Aquarella/Aquarella.slnx --no-restore,
**0 errores y 0 warnings**. Las compilaciones/publishes no migran bases.

git diff --check aprobado; rama main, sin staging/commit/push ni cambio de rama.
Las advertencias de Git sobre LF/CRLF no son warnings de compilación. Los cambios
pendientes anteriores se conservaron. La base local sigue con longitud 135168 y
LastWriteTimeUtc 2026-10-09 03:31:22, iguales a la comprobación previa; no se abrió
desde las pruebas. Esta comprobación de metadatos no sustituye un hash de contenido.
Los únicos archivos de proyecto editados en ESTA revisión son los dos documentos.
La ejecución fuera del sandbox fue necesaria para los verificadores HTTP/Data
Protection; no se desactivó criptografía ni se alteró configuración del sistema.

Fuentes:
- [Railway: headers/TLS](https://docs.railway.com/networking/public-networking/specs-and-limits).
- [Railway: aislamiento de red privada](https://docs.railway.com/networking/private-networking).
- [Microsoft: proxy y middleware](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).
- [Implementación de ForwardedHeadersMiddleware en .NET 10](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs).

## Ampliación: cuarta revisión, implementación autorizada del proxy dedicado

### Resultado

Se implementó el modo opt-in solicitado, conservando el modo de proxies conocidos
y todo Development. El propietario aceptó expresamente el límite del entorno
dedicado a un servicio/una instancia, sin otros servicios privados.
**Código preparado para pruebas Docker/Linux; todavía NO acreditar el despliegue
Railway real.** Host público HTTP/WebSocket, imagen Linux y TLS/SignalR del edge
siguen pendientes. No hubo despliegue, commit, push, correo real o base local abierta.

### Qué se implementó

1. `ReverseProxy:RailwayDedicatedEnvironmentEnabled`, false por defecto si falta.
   Development ignora el flag; fuera de Production no puede habilitarse.
2. Validación previa de PORT y metadatos de proyecto/entorno/servicio/volumen/dominio
   Railway. Dominio gratuito *.up.railway.app debe coincidir con PublicOrigin
   HTTPS de puerto estándar y AllowedHosts limitado al dominio y hostname de health.
   Metadatos son una comprobación de configuración, NO identidad autenticada.
3. Rechazo de mezcla con KnownProxies/KnownNetworks, del flag global inseguro y de
   metadatos TCP proxy público. No se inventan rangos ni se vacían listas para
   hacer funcionar el middleware general del framework.
4. Middleware temprano propio para el modo autorizado: exige Host público original
   conocido, un único Proto=https y un único X-Real-IP válido. Acepta IPv4/IPv6
   estándar; rechaza duplicados/listas, ausencias, IP con puerto/zona/brackets,
   formas IPv4 abreviadas/enteras/hex y unspecified/multicast. Normaliza IPv4-mapped
   IPv6 para no dividir rate limits entre dos representaciones del mismo cliente.
5. Solo actualiza esquema/IP tras validar. No reescribe Host ni procesa
   X-Forwarded-Host/For/Prefix o Forwarded. Host inválido/otro puerto devuelve 400
   genérico, sin fallback. Cookie de autenticación Secure/HttpOnly/SameSite y
   prefijo __Host, CSRF, registro cerrado, sesiones y ownership se conservan.
6. GET /health conserva la excepción HTTP de solo lectura: Host público o hostname
   de health (sin puerto o con el puerto interno real). Ese hostname no habilita
   login ni rutas de aplicación. SQLite/Data Protection y mantenimiento no cambian.
7. Aviso fijo al arrancar sobre el perímetro y necesidad de nueva revisión si
   se agrega otro servicio. No imprime configuración, IDs, headers o secretos.

El modo habitual sigue procesando únicamente esquema/IP desde su allowlist válida.
Desactivar el modo nuevo en Railway sin disponer de proxy conocido detiene el
arranque: no degrada automáticamente la validación. No se actualizaron paquetes,
no se instalaron dependencias ni se modificó Gmail SMTP.

### Host: investigación y manejo seguro

Se revisaron especificaciones, variables y healthchecks oficiales. Documentan
X-Forwarded-Host como host original y healthcheck.railway.app para sondeos, pero
NO se encontró garantía explícita del valor exacto de Host original HTTP ni del
handshake WebSocket entregado a Kestrel. La respuesta aportada tampoco lo establece.
Por ello no se da ese contrato por confirmado.

La implementación conserva validación estricta del Host recibido: dominio real
sin puerto o con :443; un Host interno reescrito sería rechazado con 400. Esto
evita una solución insegura; podría revelar un bloqueo operativo al probar Railway.
Confirmar antes del despliegue: «¿Se preserva Host/:authority público hasta Kestrel
en HTTP y WebSocket, y con qué puerto?». Una prueba ficticia autorizada podría
resolverlo, pero no se creó infraestructura ni se publicó nada aquí. Si no se
preserva, detenerse y revisar host canónico u otra política antes de ampliar confianza.

### Riesgo residual autorizado

Cualquier peer que alcance el puerto y copie Host/Proto/IP válidos sigue siendo
indistinguible del edge. El verificador reproduce esa aceptación deliberadamente;
no se afirma que el modo bloquee spoofing de peers internos. Puede falsear esquema
o eludir presupuestos por IP, aunque no crea por eso una sesión autenticada.
El cifrado WireGuard y formato válido no acreditan que pasó por el proxy público.

Mantener entorno dedicado, un servicio/una instancia, administración restringida
y ningún TCP proxy público. **Agregar otro servicio requiere nueva revisión de
seguridad antes de conectarlo.** Variables presentes no prueban la topología ni
reemplazan controles operativos del propietario. No se confía en un header de POP
o Request-ID como autenticación.

### Archivos modificados en ESTA revisión

- Aquarella/Aquarella/Services/ProductionHosting.cs: selección y validación de
  modo, middleware limitado, normalización de IP y aviso del riesgo.
- Aquarella/Aquarella/Program.cs: reemplazo de la llamada temprana directa por
  ProductionHosting.UseProxy(app), manteniendo su posición en el pipeline.
- Aquarella/tools/ProductionHostingVerification/Program.cs: configuración adversa,
  segunda ejecución HTTP del modo Railway y proxy loopback que simula reemplazo.
- Aquarella/DEPLOYMENT.md: estado actual, variable de activación, restricciones,
  Host pendiente y procedimiento de pruebas.
- Este informe: ampliación y conservación de las revisiones anteriores.

No se modificó appsettings.json: default false está en la lectura tipada de
configuración. No se cambiaron secretos, DB, migraciones, SMTP, Dockerfile,
launchSettings, autenticación, módulos ni diseño en esta revisión.

### Variables y siguiente paso

Configurar manualmente ReverseProxy__RailwayDedicatedEnvironmentEnabled=true
solo en el entorno dedicado autorizado, junto a las variables públicas de la
tabla actual. No configurar KnownProxies/KnownNetworks ni el flag global.
Railway aporta PORT, RAILWAY_PROJECT_ID, RAILWAY_ENVIRONMENT_ID, RAILWAY_SERVICE_ID,
RAILWAY_PUBLIC_DOMAIN y RAILWAY_VOLUME_MOUNT_PATH: no fabricarlos para desplegar.
Accounts__PublicOrigin y AllowedHosts deben coincidir con ese dominio real.
Production, registro false, SMTP false, volumen /data y claves persistentes siguen.
Una migración/base inicial requiere el mantenimiento explícito ya documentado.

Próximo paso: Docker/Linux con volumen NUEVO y confirmar Host real por soporte
o prueba de infraestructura autorizada. Mantener pendiente TLS/SignalR Railway.
Después preparar las cuentas privadas con Gmail en una base SEPARADA bajo
autorización de correos/cuentas, revisar cambios locales y pedir autorización de
despliegue. Esta tarea no incluye ninguna publicación.

Fuentes específicas:
- [Railway: headers y TLS](https://docs.railway.com/networking/public-networking/specs-and-limits).
- [Railway: variables proporcionadas](https://docs.railway.com/variables/reference).
- [Railway: hostname/puerto de healthcheck](https://docs.railway.com/deployments/healthchecks).

### Verificación final de la cuarta revisión

Se recompilaron los 17 proyectos de verificación: 0 errores y 0 warnings.
Publicaciones Release de app y DatabaseMaintenance aprobadas. Regresión completa:
**17 verificadores .NET aprobados** (16 runners principales; DevelopmentAccess
se ejecuta desde AccountHttpVerification).

DatabaseVerification; ReliabilityVerification; AiSettingsVerification;
StockIntakeVerification; MetricsVerification; MetricsPresentationVerification;
NoticesVerification; ProductVisualVerification; PersistenceVerification;
AccountMailVerification; SmtpProbeVerification; PrivateBetaVerification;
AccountHttpVerification; DevelopmentAccessVerification; MigrationSafetyVerification;
SecurityVerification; ProductionHostingVerification.

Hosting ejecutó las mismas comprobaciones de login/cookies/CSRF/rutas/registro/
rate limits/health/persistencia/logout en procesos aislados para AMBOS modos.
El runner Railway añadió la matriz de configuración/headers/Host y el proxy
loopback de reemplazo. Se verificó deliberadamente que un peer interno con headers
válidos es aceptado: evidencia del riesgo autorizado, no prueba de autenticación
del edge. Health con el puerto interno real pasó y no habilita login con ese Host.

JavaScript: metrics-motion.test.mjs y legacy-recovery.test.mjs, ambos aprobados.
Build final dotnet build Aquarella/Aquarella.slnx --no-restore:
**0 errores, 0 warnings**. No se actualizaron paquetes ni se suprimieron warnings.
El aviso de arranque del modo dedicado es intencional, distinto de los warnings
de compilación; informa el riesgo aceptado sin exponer datos.

La ejecución HTTP/Data Protection/MSBuild requirió el contexto fuera del sandbox,
como en las revisiones anteriores. Solo se usaron procesos/bases/usuarios ficticios.
El primer montaje del proxy de laboratorio necesitó agregar su fuente de
configuración en memoria tras retirar proveedores externos; se corrigió el
verificador y se repitió completamente. No fue un error de la instancia real.
La compilación inicial señaló posible Host nulo; la comparación ahora lo rechaza
de forma segura, sin desreferenciarlo. Los builds finales quedaron en 0/0.

La base local conserva longitud 135168 y LastWriteTimeUtc 2026-10-09 03:31:22,
iguales al inicio. No se abrió, migró, exportó ni modificó en esta revisión. Son
metadatos, no una certificación por hash de contenido. SMTP se comprobó mediante
los verificadores existentes; NO se hizo entrega Gmail real ni se leyeron secretos.

git diff --check aprobado, rama main y cambios locales anteriores conservados.
Sin staging, commit, push, pull, merge, rebase o cambio de rama. Solo se editaron
los cinco archivos enumerados de esta cuarta revisión. Builds/publishes quedan
en directorios bin/obj ignorados; las pruebas conservan evidencia en TEMP.

Docker sigue ausente de PATH y no se instaló. Linux, proxy público Railway,
Host/authority real HTTP/WebSocket y SignalR no se probaron; permanecen pendientes.
