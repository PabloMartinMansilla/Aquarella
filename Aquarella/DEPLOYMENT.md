# Aquarella en Railway

## Estado y límites

**Actualización de proxy (2026-10-10):** la respuesta de Railway aportada por el
propietario descarta una allowlist estable y autenticación criptográfica del edge.
El propietario autorizó el perímetro de un entorno dedicado. El modo explícito
está implementado y desactivado por defecto; ver la cuarta revisión al final.
Antes del despliegue falta verificar Host HTTP/WebSocket real y Docker/Linux.
No usar `100.64.0.0/10` ni afirmar que los headers autentican criptográficamente al edge.

Despliegue MVP de una instancia, Docker + .NET 10 + SQLite. Development conserva
`https://localhost:7188`, SQLite local, User Secrets y su acceso de prueba.
La base local nunca se incluye en la imagen ni se copia a Production.
Un volumen nuevo requiere inicialización explícita del esquema antes del arranque.
Production no crea un usuario inicial ni un acceso alternativo.
La primera beta es privada: `Accounts:Registration:Enabled` es `false` por defecto
en Production y `true` en Development. La página y el servicio rechazan registros
cuando está cerrado, incluso si después se configura correo. Login y Logout
siguen exigiendo cuentas verificadas y contraseñas hasheadas. Sin correo no hay
recuperación ni reenvío reales. No habilitar el bypass para resolverlo.
El dominio gratuito `*.up.railway.app` alcanza para esta beta; no activar SMTP en
Hobby ni Resend todavía. Una base nueva no contiene usuarios: la preparación de
cuentas privadas verificadas requiere una operación separada y autorizada.

No crear recursos pagos ni hacer commit/push sin aprobación del propietario.

## Configuración del servicio

El repositorio utiliza `Dockerfile` y `.dockerignore` en su raíz. En Railway,
seleccionar esa raíz (no `Aquarella/Aquarella`) y el builder Dockerfile.
Agregar un único volumen montado en `/data`, una sola réplica, health check `/health`.
Mantener el servicio activo: Blazor Server necesita conexiones WebSocket duraderas.
Railway proporciona el certificado HTTPS; el contenedor escucha HTTP internamente.
La aplicación utiliza `PORT` inyectado por Railway, sin un puerto público fijo.

Variables públicas necesarias (reemplazar el dominio por el realmente generado):

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

No activar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: puede eludir la lista de confianza.
El hostname `healthcheck.railway.app` es necesario para los sondeos internos de
Railway; no es el dominio público de Aquarella.
No configurar un certificado de localhost ni incluir User Secrets en el contenedor.
Las variables de proveedor de correo/API se agregan posteriormente como secretos.
Dejar `PORT` con el valor provisto por Railway; el contenedor se enlaza a
`0.0.0.0:$PORT`. No copiar puertos de launchSettings. Mantener una réplica,
desactivar Serverless/suspensión y no exponer un TCP proxy del puerto HTTP interno.
Production toma una reserva exclusiva sobre la base: una segunda instancia con
el mismo volumen se detiene sin escribir ni migrar.

## Confianza del proxy: requisito antes del despliegue

Hay dos modos separados: proxies con IP/red conocida (modo habitual) y entorno
dedicado de Railway (opt-in autorizado). El segundo procesa solamente
`X-Forwarded-Proto` y `X-Real-IP` estrictamente validados, confiando en el perímetro
del entorno. No acepta Host/For reenviados. Los enlaces absolutos salen del origen
HTTPS explícito configurado. No mezclar modos ni vaciar las listas del framework.

Railway confirmó al propietario que NO proporciona IP/CIDR estable y que
`100.64.0.0/10` no es un contrato utilizable. Por eso ya no se solicita una IP
como paso de despliegue. `ReverseProxy:KnownProxies` y `KnownNetworks` siguen
existiendo para proxies realmente controlados (por ejemplo, el laboratorio local),
pero NO hay valores válidos acreditados para Railway. No poner una IP observada,
un rango privado supuesto, `0.0.0.0/0` o `::/0` para eludir el rechazo.
En el modo habitual, sin una lista configurada la aplicación falla al iniciar;
un proxy no reconocido no permite tráfico de aplicación por HTTP. El modo Railway
requiere configuración y metadatos explícitos y rechaza solicitudes inválidas.
GET `/health` admite sondeos HTTP internos de lectura, sin autenticación ni datos de usuario.

## Persistencia y migraciones

- `/data/aquarella.db`: datos de Production, independientes de Development.
- `/data/data-protection-keys`: claves necesarias para cookies y tokens; persisten
  entre reinicios. Mantener el nombre de aplicación `Aquarella` estable.
- El directorio de claves tiene permisos exclusivos del usuario en Linux.
  La persistencia no agrega cifrado XML de las claves: tratar el volumen y sus
  backups como secretos, limitar acceso y confirmar cifrado en reposo del hosting.
- El arranque verifica en modo solo lectura integridad, relaciones, historial EF
  y esquema físico. Exige una base existente y compatible, sin migraciones pendientes.
  No migra ni crea SQLite automáticamente, tampoco en Development. Una ruta incorrecta,
  un esquema desconocido o un bloqueo EF pendiente detienen el arranque.
- Antes de actualizar, detener todas las instancias y otros escritores. Utilizar
  `tools/DatabaseMaintenance` con rutas absolutas explícitas, `--confirm-migrate`
  y `--confirm-stopped`. La herramienta verifica el esquema, crea un respaldo con
  la API de SQLite (incluye WAL), comprueba su integridad y datos, aplica las
  migraciones versionadas y registra el resultado. `--create` se reserva para una
  inicialización nueva expresamente autorizada; no usarlo para solucionar una ruta errónea.
- El Dockerfile publica también la herramienta de la misma versión en
  `/maintenance/DatabaseMaintenance.dll`. Es un comando alternativo explícito,
  sin iniciar Blazor ni el worker. El CMD normal solo inicia `/app/Aquarella.dll`.
  Ningún build ni arranque normal ejecuta migraciones.
- Restaurar primero a un archivo nuevo privado y validar. Nunca sobrescribir una
  base activa ni borrar `.db`, WAL o SHM para resolver un error. Guardar también
  el key ring por un procedimiento privado separado; el backup SQLite no lo incluye.
- El bloqueo de mantenimiento coordina instancias de esta versión; no impide
  escrituras de herramientas SQLite externas ni versiones anteriores. Confirmar
  su detención sigue siendo obligatorio. Ver `AUDITORIA-MIGRACION-CORREO.md`.
- Un volumen único no ofrece alta disponibilidad. No activar múltiples réplicas;
  revisar PostgreSQL, key ring compartido y SignalR antes de escalar horizontalmente.
- El runtime del Dockerfile utiliza el usuario predeterminado de la imagen para
  poder escribir en el volumen montado. Revisar propietario/permisos al endurecer
  el contenedor para ejecución con un usuario sin privilegios.

## Prueba local Docker, sin tocar la base real

Desde la raíz del repositorio, con Docker Desktop iniciado en contenedores Linux:

Estos comandos solo afectan un volumen Docker NUEVO de prueba, nunca la base local.
Inicializarlo explícitamente con la herramienta incluida en la imagen:

```powershell
docker build -t aquarella:production-test .
docker volume create aquarella-production-test
docker run --rm --mount source=aquarella-production-test,target=/data aquarella:production-test dotnet /maintenance/DatabaseMaintenance.dll migrate --database /data/aquarella.db --backup-dir /data/migration-backups --confirm-migrate --confirm-stopped --create
docker run -d --name aquarella-production-test -p 127.0.0.1:18080:8080 --mount source=aquarella-production-test,target=/data -e PORT=8080 -e ASPNETCORE_ENVIRONMENT=Production -e DOTNET_ENVIRONMENT=Production -e ProductionStorage__Root=/data -e 'ConnectionStrings__Aquarella=Data Source=/data/aquarella.db' -e DataProtection__KeysPath=/data/data-protection-keys -e Accounts__PublicOrigin=https://localhost -e AllowedHosts=localhost -e ReverseProxy__KnownProxies__0=127.0.0.1 aquarella:production-test
```

Esto permite probar `/health` por HTTP, pero no autoriza inventar headers HTTPS
desde una conexión arbitraria. Para probar Login/cookies en Docker, usar un proxy
HTTPS local, configurar su dirección real dentro de la red Docker en KnownProxies
y su origen HTTPS local en PublicOrigin. El certificado local es solo de ese proxy
de prueba; nunca forma parte de la imagen pública.
No utilizar el volumen ni la base local real en estas pruebas.
Reiniciar el contenedor de prueba conservando su volumen y comprobar registros,
migraciones, claves y cookies. No eliminar el volumen para solucionar fallos.

Existe también una verificación aislada del proceso .NET:

```powershell
dotnet run --project Aquarella/tools/ProductionHostingVerification -- RUTA-ABSOLUTA-AL-PUBLISH
```

Esta prueba utiliza puertos libres y un directorio temporal nuevo, simula el
proxy autorizado y comprueba persistencia tras reiniciar solamente su proceso.
No reemplaza la prueba real de la imagen Linux ni la verificación pública.

## Orden del despliegue autorizado

1. Compilar y completar la prueba Docker local.
2. Revisar los cambios pendientes y obtener aprobación antes de commit/push.
3. El propietario inicia sesión en Railway y autoriza GitHub/repositorio.
4. Elegir prueba gratuita si está disponible; detenerse ante cualquier cobro.
5. Crear servicio, volumen y dominio; configurar las variables anteriores y proxy.
6. Con el servicio detenido, preparar la herramienta de mantenimiento, confirmar
   la ruta del volumen, respaldar y validar una base existente; inicializar solamente
   si se ha autorizado un volumen nuevo. Aplicar la migración explícita, revisar
   su resultado e integridad y recién entonces iniciar el servicio.
7. Desplegar y probar `/health`, `/login`, rutas protegidas, rechazo del bypass,
   ausencia de tokens y comportamiento móvil. No afirmar éxito sin esas pruebas.
8. Utilizar exclusivamente cuentas privadas previamente verificadas por el flujo
   existente. No marcar EmailVerified a mano ni cambiar el entorno público a
   Development. Reiniciar/redeployar y verificar persistencia real antes de invitar.

Referencias:
- https://docs.railway.com/networking/public-networking/specs-and-limits
- https://docs.railway.com/volumes
- https://docs.railway.com/deployments/healthchecks
- https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0

## Archivos de esta preparación

Creación:
- `/Dockerfile`, `/.dockerignore`, `/.gitignore`.
- `Aquarella/DEPLOYMENT.md`.
- `Aquarella/Aquarella/Services/ProductionHosting.cs`.
- `Aquarella/tools/ProductionHostingVerification/Program.cs` y su `.csproj`.

Modificación:
- `Aquarella/.gitignore`.
- `Aquarella/Aquarella/Aquarella.csproj`: exclusiones de publicación.
- `Aquarella/Aquarella/Program.cs`: hosting, proxy, correo por entorno, health y errores.
- `Aquarella/Aquarella/Services/Accounts/AccountEmail.cs`: disponibilidad de correo.
- `Aquarella/Aquarella/Services/Accounts/AccountService.cs`: no generar tokens sin proveedor.
- `Aquarella/Aquarella/Pages/Access.cshtml.cs` y `Access.cshtml`: ausencia de correo clara.
- `Aquarella/tools/DatabaseVerification/Program.cs`: adaptar el proveedor simulado a la interfaz.

No se modificaron en esta preparación launchSettings, diseño, módulos, base local
ni migraciones. Los cambios anteriores pendientes del repositorio se conservaron.

## Actualización: beta privada sin correo en Production (2026-10-10)

No se publicó el servicio ni se ejecutaron comandos contra Railway. SMTP de
Development mantiene su selección y STARTTLS; no se efectuaron envíos reales.
El registro se puede reabrir posteriormente con `Accounts__Registration__Enabled=true`,
pero sin un proveedor disponible el servidor sigue rechazando la creación en Production.

### Mantenimiento con volumen montado

Railway NO monta el volumen durante build ni pre-deploy. No configurar migraciones
en Pre-deploy Command. Para una operación autorizada usar un lanzamiento de
mantenimiento del mismo servicio/volumen con la aplicación detenida, healthcheck
desactivado y restart policy `Never` durante la operación. Confirmar que el
despliegue anterior está detenido antes de usar `--confirm-stopped`. No compartir
el volumen con otra instancia simultánea.

Comandos alternativos disponibles en la imagen (no son comandos del arranque normal):

```text
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
dotnet /maintenance/DatabaseMaintenance.dll migrate --database /data/aquarella.db --backup-dir /data/migration-backups --confirm-migrate --confirm-stopped --create
dotnet /maintenance/DatabaseMaintenance.dll migrate --database /data/aquarella.db --backup-dir /data/migration-backups --confirm-migrate --confirm-stopped
dotnet /maintenance/DatabaseMaintenance.dll restore --database /data/migration-backups/RESPALDO-VERIFICADO.db --destination /data/restoration-test.db
```

El segundo comando SOLO inicializa un volumen NUEVO cuya creación se autorizó.
Para actualizar una base existente usar el tercero, sin `--create`. La herramienta
crea y valida un backup consistente mediante SQLite (incluido WAL), integrity_check,
foreign_key_check y comparación de datos, y conserva un recibo privado. Restaurar
de prueba siempre a un archivo NUEVO. No sobrescribir ni borrar la base activa.
Si falla, detenerse; no iniciar con esquema pendiente/desconocido. Después restaurar
el CMD normal `dotnet /app/Aquarella.dll`, healthcheck `/health`, una réplica y
restart `On Failure`. Este procedimiento nunca se ejecutó contra la base local.

Respaldar el key ring por separado y conservar copias privadas fuera del volumen.
El backup SQLite no contiene las claves ni protege de eliminar el volumen completo.
No versionar bases, recibos, logs, copias de claves ni User Secrets.

### Cuentas iniciales

El procedimiento disponible conserva la verificación real: una base NUEVA de beta,
registro local mediante la pantalla existente, verificación Gmail y respaldo/restauración
controlados. No requiere otro sistema de contraseñas ni permitir altas públicas.
Se agregó `inspect-private-beta`, una comprobación de SOLO LECTURA que muestra
conteos, nunca correos, nombres, hashes, contraseñas ni tokens. Devuelve exit code 2
si no hay cuentas verificadas, hay cuentas pendientes/incompletas, sesiones activas,
tokens no utilizados/no vencidos o correos pendientes. No corrige ni borra nada.
La base debe estar detenida al efectuar el control y el respaldo: un resultado
Ready no garantiza que otro escritor no la cambie posteriormente.

Antes de ejecutar, autorizar expresamente las cuentas reales y el envío Gmail.
No se creó ninguna cuenta real ni se envió correo durante esta preparación.
Decisión confirmada por el propietario (2026-10-10): verificar mediante Gmail en
una base separada de Development. La elección del procedimiento no autoriza aún
crear cuentas reales o enviar correos. No editar EmailVerified mediante SQL.

#### Preparar una base de beta independiente en esta PC

Con Aquarella de beta detenida, desde la raíz del repositorio, en PowerShell:

```powershell
$betaDirectory = Join-Path ([IO.Path]::GetTempPath()) ('aquarella-beta-preparation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $betaDirectory | Out-Null
$betaDatabase = Join-Path $betaDirectory 'beta.db'
dotnet run --project Aquarella/tools/DatabaseMaintenance -- migrate --database $betaDatabase --backup-dir (Join-Path $betaDirectory 'migration-backups') --confirm-migrate --confirm-stopped --create
```

Es una ruta nueva; NO sustituirla por `App_Data/aquarella.db`. Detenerse si el
comando falla. La contraseña SMTP permanece en los User Secrets del proyecto;
no listarlos, copiarlos, introducirlos en argumentos ni exportarlos a la imagen.
Esta ejecución posterior, expresamente autorizada, utiliza ese servicio actual.
Para iniciar únicamente la instancia local de preparación (puerto libre de prueba):

```powershell
$previousBetaAspnetEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousBetaDotnetEnvironment = $env:DOTNET_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    dotnet run --project Aquarella/Aquarella --no-launch-profile -- --urls https://localhost:17188 --ConnectionStrings:Aquarella "Data Source=$betaDatabase" --DataProtection:KeysPath (Join-Path $betaDirectory 'local-keys') --Accounts:PublicOrigin https://localhost:17188 --Accounts:Registration:Enabled true --Email:Smtp:Enabled true --DevelopmentAccess:Username ('disabled-' + [guid]::NewGuid().ToString('N'))
} finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousBetaAspnetEnvironment
    $env:DOTNET_ENVIRONMENT = $previousBetaDotnetEnvironment
}
```

El puerto debe estar libre y el certificado local de desarrollo válido; no cambiar
launchSettings ni exponer esta instancia en Internet. Cada titular autorizado
introduce su propia contraseña en el formulario (12–256 caracteres), recibe el
correo Gmail y confirma su enlace desde esta misma PC/instancia local. Para la
primera cuenta del propietario, hacerlo personalmente. Un enlace localhost no
funciona desde el teléfono remoto del invitado: no presentar este procedimiento
como onboarding remoto. No reenviar enlaces privados al informe ni a logs.
Confirmar la recepción real, no usar el texto informativo Development como prueba
de transporte. Gmail debe quedar configurado y disponible para esta ejecución.

Cerrar sesión en TODOS los navegadores/sesiones utilizados y detener esta instancia
con Ctrl+C. No basta con cerrar una pestaña. Luego:

```powershell
dotnet run --project Aquarella/tools/DatabaseMaintenance -- inspect-private-beta --database $betaDatabase
# Continuar SOLO con exit code 0 y Ready=true; si falla, investigar sin borrar filas.
dotnet run --project Aquarella/tools/DatabaseMaintenance -- backup --database $betaDatabase --destination (Join-Path $betaDirectory 'verified-beta-backup.db')
dotnet run --project Aquarella/tools/DatabaseMaintenance -- restore --database (Join-Path $betaDirectory 'verified-beta-backup.db') --destination (Join-Path $betaDirectory 'restoration-test.db')
dotnet run --project Aquarella/tools/DatabaseMaintenance -- inspect-private-beta --database (Join-Path $betaDirectory 'restoration-test.db')
```

Revisar el exit code de CADA comando y detenerse ante cualquier fallo. Si queda una
sesión activa, cerrarla por el flujo normal; si queda un token sin consumir,
completar la verificación o esperar su vencimiento. No relajar la comprobación.
La restauración compara datos y verifica integridad/esquema con el mecanismo SQLite/WAL.

El traslado posterior a Railway requiere autorización y un canal privado autenticado
que no vuelque el archivo en logs. No se agregó ni probó una herramienta de transferencia.
Con aplicación detenida y volumen montado, ubicar el backup privado autorizado en
`/data/import/verified-beta-backup.db` y restaurar a un archivo NUEVO:

```text
dotnet /maintenance/DatabaseMaintenance.dll restore --database /data/import/verified-beta-backup.db --destination /data/beta-initial.db
dotnet /maintenance/DatabaseMaintenance.dll inspect-private-beta --database /data/beta-initial.db
```

Solo entonces configurar `ConnectionStrings__Aquarella=Data Source=/data/beta-initial.db`.
No sobrescribir `/data/aquarella.db` si existe. No trasladar `local-keys`, outbox,
cookies o certificados; Production crea su key ring nuevo dentro del volumen.
Los hashes de contraseña y las cuentas verificadas funcionan sin las claves locales.
Mantener Production, registro cerrado y SMTP deshabilitado. Este procedimiento sirve
para el primer lote; NO importar otra base completa sobre negocios ya activos para
agregar un usuario. Altas posteriores y recuperación remota sin correo requieren
una decisión separada (o el proveedor HTTPS futuro).

### Condiciones pendientes para autorizar el despliegue

- La decisión de perímetro ya fue autorizada y el modo está implementado. Mantener
  el entorno dedicado sin nuevos servicios y comprobar Host original público en
  HTTP/WebSocket: todavía no está verificado en Railway real. Si no coincide,
  la aplicación rechaza; no activar una confianza adicional como workaround.
- Ejecutar Docker/Linux con un volumen de prueba NUEVO y un proxy HTTPS local
  autorizado. Docker no está instalado/disponible en esta PC durante esta tarea.
- Revisar permisos/cifrado y backups del volumen, preparar cuentas verificadas y
  aceptar las limitaciones de recuperación sin correo antes de invitar usuarios.
- En el despliegue posteriormente autorizado comprobar Login/Logout, CSRF, SignalR,
  registro directo bloqueado, cookies seguras y persistencia tras reinicio/redeploy.

Persistir las claves conserva cookies no expiradas; no conserva circuitos Blazor
en memoria tras un reinicio. Railway introduce una breve interrupción al actualizar
servicios con volumen. No prometer continuidad total de una página abierta.
`/health` comprueba lectura del historial de la base validada al iniciar, sin exponer
datos; falla con 503 y nunca crea ni repara archivos. No hace integrity_check completo
en cada sondeo y no reemplaza backups ni monitoreo continuo.

Fuentes operativas:
- [Volúmenes y disponibilidad](https://docs.railway.com/volumes).
- [Comandos alternativos de arranque](https://docs.railway.com/deployments/start-command).
- [HTTPS, headers y WebSockets](https://docs.railway.com/networking/public-networking/specs-and-limits).
- [Healthchecks y actualizaciones con volumen](https://docs.railway.com/deployments/healthchecks).

## Segunda revisión: proxy y validación Linux (2026-10-10)

Evidencia histórica: la consulta de esta sección ya fue respondida en el adjunto
del propietario. La tercera revisión siguiente sustituye los pasos que pedían
una allowlist confirmada; los comandos de laboratorio con proxy propio siguen vigentes.

### Cómo obtener el dato de confianza que falta

Se revisaron especificaciones, Edge Networking y Edge Rules oficiales. No se
encontró una allowlist publicada del peer inmediato que llega a Kestrel, ni un
mecanismo documentado de autenticación del upstream sin IP (mTLS, header firmado
o secreto insertado por el edge). Las reglas documentadas no incluyen inyección
de un secreto de origen. Esto no prueba que Railway no pueda ofrecer otra solución;
requiere su confirmación. No se implementó autodetección o aceptación universal.

La IP pública anycast, un POP, la IP saliente estática, la IP del cliente y
`X-Railway-Edge` no acreditan la identidad del proxy inmediato. Un proxy local
intermedio tampoco autentica por sí solo el salto Railway → contenedor.

En Hobby el canal documentado es Central Station/comunidad; no se promete ticket
privado ni respuesta garantizada. El propietario puede publicar esta consulta,
sin credenciales, tokens, bases ni logs con datos de usuarios:

> Usamos ASP.NET Core 10/Kestrel, una réplica, dominio gratuito Railway y TLS en
> el edge. Forwarded Headers acepta X-Real-IP/X-Forwarded-Proto solo desde proxies
> acreditados. ¿Qué IP/CIDR del peer inmediato de Kestrel se garantiza para nuestra
> región/servicio y cómo cambia con reinicios/redeploys? ¿Ese rango permite distinguir
> al proxy de otros servicios? ¿El edge reemplaza los headers enviados por el cliente
> o los concatena? Si no hay allowlist estable, ¿ofrecen autenticación documentada
> del upstream sin aceptar headers de cualquier origen?

Si solicitan una medición, realizarla solo en una prueba de infraestructura
autorizada, sin base/cuentas reales: registrar exclusivamente RemoteIpAddress
ANTES de UseForwardedHeaders, nunca el header del cliente ni cookies/cuerpo/query.
Comparar varios reinicios, pero no convertir una observación en garantía de rango.
Guardar la confirmación de Railway junto a la configuración y probar rechazo de
headers falsificados antes de abrir la beta. No se hizo esa prueba ni se publicó
la consulta desde esta tarea.

[Especificaciones](https://docs.railway.com/networking/public-networking/specs-and-limits),
[arquitectura edge](https://docs.railway.com/networking/edge-networking),
[reglas disponibles](https://docs.railway.com/networking/edge-rules),
[soporte por plan](https://docs.railway.com/platform/support).
Microsoft advierte que el flag global no restringe la confianza por KnownProxies;
no usarlo para resolver este bloqueo.
[Forwarded Headers en ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

### Pruebas Docker/Linux todavía NO ejecutadas

Docker no está en PATH ni en su ubicación habitual de Program Files. No se instaló.
El Dockerfile/contexto existentes son suficientes para esta etapa: ambos proyectos
net10.0, restore transitivo, publish framework-dependent, rutas absolutas Linux,
runtime ASP.NET sin SDK, mantenimiento incluido, CMD sin migración, exclusiones
de datos/secretos y PORT configurable. No fue necesario modificarlos de nuevo.
El usuario predeterminado root puede escribir el volumen; todavía no se acredita
su permiso real en Railway. No se afirma que una publicación Windows valide la
imagen Linux ni sus librerías nativas de SQLite.

Con Docker Linux disponible, ejecutar los comandos de “Prueba local Docker” arriba,
siempre sobre su volumen NUEVO. Después comprobar:

```sh
curl --fail -H 'Host: localhost' http://127.0.0.1:18080/health
curl -i -H 'Host: localhost' http://127.0.0.1:18080/login
docker exec aquarella-production-test dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
docker exec aquarella-production-test sh -c 'test -w /data && test -w /data/data-protection-keys && stat -c "%a %u:%g" /data /data/data-protection-keys'
docker restart aquarella-production-test
curl --fail -H 'Host: localhost' http://127.0.0.1:18080/health
```

Esperado: health 200, login HTTP directo 400, esquema Current, permisos 700 y datos
conservados después de reiniciar. Reintentar el sondeo solo hasta que el proceso
esté listo. No ejecutar mantenimiento que escriba mientras la app esté activa.
Prueba adicional: otro contenedor de app con el mismo volumen, mismas variables
y sin publicar otro puerto debe fallar por la reserva de base; si no ocurre,
detener ambas instancias y revisar el locking Linux antes de desplegar.

Para ejercitar el proceso .NET en un equipo Linux con SDK 10 y una copia de SOLO
código autorizada, desde su raíz:

```sh
review_dir=$(mktemp -d)
repo_dir="$PWD"
dotnet publish Aquarella/Aquarella/Aquarella.csproj -c Release -o "$review_dir/app" /p:UseAppHost=false
dotnet publish Aquarella/tools/DatabaseMaintenance/DatabaseMaintenance.csproj -c Release -o "$review_dir/maintenance" /p:UseAppHost=false
dotnet build Aquarella/tools/ProductionHostingVerification/ProductionHostingVerification.csproj
dotnet build Aquarella/tools/PrivateBetaVerification/PrivateBetaVerification.csproj
cd "$review_dir"
dotnet "$repo_dir/Aquarella/tools/ProductionHostingVerification/bin/Debug/net10.0/ProductionHostingVerification.dll" "$review_dir/app"
dotnet "$repo_dir/Aquarella/tools/PrivateBetaVerification/bin/Debug/net10.0/PrivateBetaVerification.dll" "$review_dir/maintenance/DatabaseMaintenance.dll"
```

Detenerse si falla cualquiera de los comandos. El verificador de hosting ahora
comprueba permisos 0700 si se ejecuta en Linux. Verifica proxy loopback controlado,
cookies, reinicios y exclusión de instancias con una base ficticia; no sustituye
un navegador detrás de TLS/SignalR en el proxy REAL. La prueba de beta acepta solo
la ruta del ejecutable de mantenimiento, nunca una base o cuenta del operador.

Finalmente probar Login/Logout y circuitos Blazor con un proxy HTTPS local cuyo
origen se conozca realmente. En el despliegue posteriormente autorizado, repetir
contra el edge Railway con el modelo de confianza aprobado, reinicio/redeploy y volumen.
Mantener estos puntos pendientes hasta contar con evidencia de ejecución.

## Tercera revisión: respuesta Railway y propuesta pendiente de aprobación

Evidencia histórica: el propietario aprobó posteriormente la propuesta bajo
condiciones explícitas. La implementación vigente está en la cuarta revisión.

### Base de la decisión

La respuesta de Railway transcrita por el propietario confirma: ausencia de IP/CIDR
estable y de headers firmados/mTLS; reemplazo de los headers por el edge público;
TLS en el edge y transporte WireGuard; bypass del edge al usar la red privada;
posibilidad de falsificar headers desde otros servicios del mismo entorno.
La documentación pública confirma headers, TLS y aislamiento de redes, pero no
se presenta como evidencia independiente de todos los puntos de esa respuesta.
[Headers y TLS](https://docs.railway.com/networking/public-networking/specs-and-limits),
[red privada](https://docs.railway.com/networking/private-networking).

No hay forma, con esos headers solamente, de distinguir criptográficamente una
petición del edge de otra petición interna que copie los mismos valores. WireGuard
cifra el transporte; no demuestra que la petición haya pasado por el edge público.
AllowedHosts limita el host, tampoco autentica al emisor.

ASP.NET Core 10 verifica KnownProxies/KnownIPNetworks. Vaciar ambas listas elimina
la comprobación de IP; ForwardLimit=1 y RequireHeaderSymmetry no la sustituyen.
No se habilitará el flag global ni se usará una IP ficticia. Un sidecar local no
autenticaría por sí mismo el salto inicial.
[Microsoft: orden y confianza](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0),
[implementación .NET 10](https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Middleware/HttpOverrides/src/ForwardedHeadersMiddleware.cs).

### Propuesta concreta, todavía NO implementada

Un modo explícito y reversible, separado del modo actual de proxies conocidos,
solo para Production en un entorno Railway dedicado a Aquarella. Por defecto
continuaría desactivado. Se validarían las variables de entorno de Railway y
la configuración de origen/volumen antes del arranque, pero esas variables NO
se considerarían autenticación del proxy ni prueba de ausencia de otros servicios.

Condiciones operativas obligatorias: un servicio/una instancia, sin servicios
adicionales ni workers no confiables en ese entorno, acceso administrativo
restringido y sin TCP proxy público del puerto HTTP interno. El perímetro aceptado
sería ese entorno privado de Railway, NO una identidad individual del edge.
Estas condiciones requieren control del propietario; Aquarella no puede comprobar
la topología completa usando una variable de configuración.
Técnicamente el modo aceptaría los headers válidos de cualquier peer que alcance
el puerto del contenedor: la restricción de quién puede alcanzarlo la aporta ese
perímetro de Railway, no una autenticación nueva dentro de ASP.NET Core.

Para peticiones de aplicación se exigirían un único X-Forwarded-Proto=https y
un único X-Real-IP válido; rechazar ausentes, múltiples o inválidos. Procesar solo
esquema e IP antes de HSTS/HTTPS/auth/rate limiting. No procesar X-Forwarded-Host,
X-Forwarded-For, X-Forwarded-Prefix ni Forwarded como fuente de confianza. Conservar
AllowedHosts explícito, origen absoluto fijo, cookies Secure/HttpOnly y CSRF.
No normalizar silenciosamente una cadena de proxies múltiple.
GET /health seguiría siendo la excepción HTTP de solo lectura, sin datos de usuarios.

**Riesgo que requiere aceptación:** un servicio malicioso o comprometido dentro
del mismo entorno podría falsificar esquema/IP, eludir el rechazo HTTP y alterar
los presupuestos de rate limiting por IP. Validar formato no evita esa falsificación.
No otorga por sí solo una sesión válida, pero tampoco es un riesgo eliminado por
el registro cerrado. Agregar un servicio exige revisar o abandonar este modo ANTES
de conectarlo; preferir otro entorno para servicios no confiables. Comprometer la
cuenta Railway o el contenedor también compromete este perímetro.

Si no se acepta ese límite, conservar el rechazo y no desplegar con este modelo.
La alternativa es disponer de infraestructura con upstream autenticado o una
restricción de red verificable; no se crea ni contrata infraestructura en esta tarea.

### Variables y validaciones posteriores

Las variables públicas de “Configuración del servicio” siguen siendo correctas,
pero no bastan para arrancar Railway con el código actual. PORT y variables
Railway las aporta la plataforma. No hay todavía una nueva variable de modo
implementada: su nombre y valores se documentarán después de la aprobación.
No configurar KnownNetworks=100.64.0.0/10 ni el flag global.

No falta otra consulta sobre IP/CIDR o mTLS: ya fue respondida. Confirmar cómo llega
el header Host original al contenedor y al handshake WebSocket: actualmente no
usamos X-Forwarded-Host y AllowedHosts necesita el host público original. Puede
confirmarse en una prueba autorizada o con soporte, sin registrar cookies/tokens.
Si no se conserva, revisar una política de host canónico antes de implementar;
no habilitar automáticamente el host reenviado.

Después de aprobar: implementar el modo acotado; verificar headers inválidos y
directos, spoofing por cliente público detrás de un proxy de laboratorio que los
reemplace, cookies, redirecciones, CSRF, registro cerrado, rate limits y health.
Documentar expresamente que un peer interno con headers válidos NO se puede
distinguir del edge en este modelo. Ejecutar todos los verificadores y JavaScript.
Después realizar Docker/Linux con volumen ficticio y TLS/SignalR reales en un
despliegue separado autorizado. No considerar esas pruebas ya realizadas.

## Cuarta revisión: modo Railway autorizado e implementado

### Activación y límites

`ReverseProxy:RailwayDedicatedEnvironmentEnabled` es `false` por defecto si no se
configura. Activar explícitamente en Railway con:

```text
ReverseProxy__RailwayDedicatedEnvironmentEnabled=true
```

No agregar KnownProxies/KnownNetworks a ese modo. Conservamos el modo de proxies
conocidos para un proxy controlado y el laboratorio Docker. Desactivar el nuevo
flag en Railway devuelve al rechazo de arranque sin una allowlist válida; no es
un fallback automático a confianza universal. Development ignora el modo y conserva
su configuración. Staging u otro entorno que intente habilitarlo es rechazado.

La validación previa a almacenamiento/inicio exige Production, PORT válido,
RAILWAY_PROJECT_ID, RAILWAY_ENVIRONMENT_ID, RAILWAY_SERVICE_ID,
RAILWAY_VOLUME_MOUNT_PATH y RAILWAY_PUBLIC_DOMAIN. El dominio debe ser el gratuito
*.up.railway.app, coincidir con Accounts:PublicOrigin HTTPS en puerto estándar,
y AllowedHosts solo puede incluir ese dominio y healthcheck.railway.app.
El almacenamiento debe coincidir con el volumen y preservar las validaciones
existentes de SQLite/Data Protection. No inventar variables de plataforma:
[referencia oficial](https://docs.railway.com/variables/reference).

Si hay RAILWAY_TCP_PROXY_DOMAIN, RAILWAY_TCP_PROXY_PORT o RAILWAY_TCP_APPLICATION_PORT
configurados, el arranque se rechaza. Esto reduce la exposición accidental del
puerto interno. La ausencia de esos metadatos NO demuestra que no haya otros
servicios, peers o reglas de red: el propietario debe conservar el perímetro.
Metadatos de Railway son una comprobación de configuración, NO autenticación del
edge ni prueba criptográfica de que el proceso está alojado allí.

### Requests y cookies

El middleware se ejecuta antes de HTTPS/HSTS/auth/rate limiting. Para aplicación:

- Host original exactamente igual al dominio configurado (case-insensitive),
  sin puerto o con :443. No se reescribe Host.
- Un único X-Forwarded-Proto con valor exacto https.
- Un único X-Real-IP estándar válido, sin lista, puerto, zona IPv6, brackets,
  whitespace ni formas IPv4 abreviadas/hex/enteras. Se rechazan unspecified/multicast.
- IPv4-mapped IPv6 se normaliza a IPv4; IPv6 estándar conserva su representación
  numérica normalizada para que el presupuesto por IP no dependa del formato textual.
- Headers faltantes, duplicados o inválidos y Host inesperado: 400 con texto genérico.

Solo modifica Request.Scheme y Connection.RemoteIpAddress después de validar.
No usa UseForwardedHeaders en este modo ni deshabilita globalmente sus listas.
X-Forwarded-Host, X-Forwarded-For, X-Forwarded-Prefix y Forwarded no se consumen.
AllowedHosts sigue activo. Cookies de autenticación conservan Secure/HttpOnly/
SameSite=Lax/Path=/ y prefijo __Host sin Domain; CSRF y autenticación no cambian.
Registro permanece cerrado con las variables de la tabla; SMTP Development intacto.

GET /health es la única excepción sin headers HTTPS: Host público válido o
healthcheck.railway.app (sin puerto o con el puerto interno real). No habilita
otras rutas con el hostname de health. Consulta SQLite en solo lectura como antes.

### Host real: pendiente que NO se oculta

Se investigaron las especificaciones, variables y healthchecks oficiales. La
documentación dice que X-Forwarded-Host identifica el host original, pero no
garantiza explícitamente la conservación de Request.Host en el upstream HTTP
ni en WebSocket. La implementación NO presume esa garantía: cualquier otro Host
se rechaza y no se recupera desde X-Forwarded-Host.
[Especificaciones](https://docs.railway.com/networking/public-networking/specs-and-limits),
[hostname de healthcheck](https://docs.railway.com/deployments/healthchecks).

Antes del despliegue: confirmar con Railway o mediante una instancia ficticia
autorizada que Host llega como DOMINIO-REAL.up.railway.app (o :443), incluyendo
el handshake WebSocket. Esa instancia no se creó en esta tarea. Si el edge lo
reescribe a un host/puerto interno, detenerse: hará falta revisar una solución
de host canónico o una confianza adicional, no permitir hosts arbitrarios.
Consulta pendiente precisa: «¿Se conserva el Host/:authority público original
en el upstream HTTP y en WebSocket? ¿Con qué puerto llega a Kestrel?».

### Riesgo aceptado y revisión obligatoria

Un peer que alcance el puerto y copie Host/Proto/IP válidos es indistinguible del
edge. La prueba lo demuestra expresamente; no se describe como spoofing bloqueado.
Podría falsificar IP para eludir rate limits y declarar HTTPS. No obtiene por ello
una sesión válida, pero las validaciones de formato no autentican su origen.
El arranque emite un aviso fijo sobre ese límite, sin IDs, credenciales o headers.

Mantener un servicio web/una instancia, ningún servicio adicional privado,
administración restringida y sin TCP público. **Agregar otro servicio al mismo
entorno requiere una nueva revisión de seguridad ANTES de conectarlo.** No usar
este modo para una red compartida con workloads no confiables.

### Pruebas y próximos pasos

ProductionHostingVerification ejecuta ambos modos con publicaciones y bases
temporales distintas. El hijo --railway recorre login, cookies, CSRF, registro,
health, rate limits, reinicios y logout con el dominio ficticio en Host, sin DNS
público. Incluye configuración desactivada/incompleta/mezclada, entorno inválido,
TCP proxy declarado, headers ausentes/duplicados/invalidos, IPv4/IPv6, autoridad
incorrecta, host de health limitado, headers ignorados y normalización por IP.

Un proxy HTTP solo loopback simula el reemplazo de headers del contrato suministrado
por Railway y demuestra que el cliente no conserva sus valores falsificados.
Esto NO verifica el comportamiento de Railway real, TLS del edge ni SignalR.
El laboratorio con proxies conocidos sigue comprobando rechazo por identidad IP.

Docker no está disponible aquí; Docker/Linux y el volumen montado siguen pendientes.
Continuar con los comandos de laboratorio existentes sobre volumen NUEVO. Después
verificar Host real y TLS/SignalR en una infraestructura expresamente autorizada,
preparar las cuentas privadas en la base separada mediante Gmail y revisar cambios.
No desplegar ni enviar correo como parte de esta implementación.
