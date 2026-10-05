# Aquarella en Railway

## Estado y límites

Despliegue MVP de una instancia, Docker + .NET 10 + SQLite. Development conserva
`https://localhost:7188`, SQLite local, User Secrets y su acceso de prueba.
La base local nunca se incluye en la imagen ni se copia a Production.
Production empieza vacía: no tiene un usuario inicial ni un acceso alternativo.
Mientras no haya un proveedor real de correo, no se pueden crear cuentas públicas
ni recuperar contraseñas. El Login puede publicarse, pero utilizar el dashboard
requiere después una cuenta real verificada. No habilitar el bypass para resolverlo.

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
```

No activar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: puede eludir la lista de confianza.
El hostname `healthcheck.railway.app` es necesario para los sondeos internos de
Railway; no es el dominio público de Aquarella.
No configurar un certificado de localhost ni incluir User Secrets en el contenedor.
Las variables de proveedor de correo/API se agregan posteriormente como secretos.

## Confianza del proxy: requisito antes del despliegue

Railway documenta `X-Forwarded-Proto` y `X-Real-IP`. Procesamos esos headers solo
desde direcciones de proxy permitidas, con un único salto. No aceptamos un host
reenviado: los enlaces absolutos salen del origen HTTPS explícito configurado.

Configurar al menos una dirección o red **verificada** del proxy que efectivamente
se conecta al servicio:

```text
ReverseProxy__KnownProxies__0=DIRECCION-REAL-DEL-PROXY
```

O `ReverseProxy__KnownNetworks__0=CIDR-VERIFICADO`, si Railway confirma un rango
estable. No inventar direcciones, no usar `0.0.0.0/0`, `::/0` ni permitir toda
Internet. Railway no publica esos rangos en la página de especificaciones consultada;
confirmar con su soporte o documentación del servicio antes de publicarlo.
Una dirección observada una vez no garantiza estabilidad después de un redeploy.
Sin una lista configurada la aplicación falla al iniciar; un proxy no reconocido
no permite tráfico de aplicación por HTTP. `/health` admite sondeos HTTP internos.

## Persistencia y migraciones

- `/data/aquarella.db`: datos de Production, independientes de Development.
- `/data/data-protection-keys`: claves necesarias para cookies y tokens; persisten
  entre reinicios. Mantener el nombre de aplicación `Aquarella` estable.
- El directorio de claves tiene permisos exclusivos del usuario en Linux.
  La persistencia no agrega cifrado XML de las claves: tratar el volumen y sus
  backups como secretos, limitar acceso y confirmar cifrado en reposo del hosting.
- Al iniciar se ejecuta `MigrateAsync`: aplica únicamente migraciones pendientes.
  Nunca se usa `EnsureCreated`, borrado o recreación de la base. Un fallo aborta
  el arranque; revisar logs privados, no borrar la base para intentar solucionarlo.
- Hacer un backup consistente de SQLite antes de futuros cambios de esquema.
  No copiar solo un `.db` activo si hay WAL: utilizar backup de SQLite o detener
  la instancia y completar el checkpoint. Probar restauraciones por separado.
- Un volumen único no ofrece alta disponibilidad. No activar múltiples réplicas;
  revisar PostgreSQL, key ring compartido y SignalR antes de escalar horizontalmente.
- El runtime del Dockerfile utiliza el usuario predeterminado de la imagen para
  poder escribir en el volumen montado. Revisar propietario/permisos al endurecer
  el contenedor para ejecución con un usuario sin privilegios.

## Prueba local Docker, sin tocar la base real

Desde la raíz del repositorio, con Docker Desktop iniciado en contenedores Linux:

```powershell
docker build -t aquarella:production-test .
docker volume create aquarella-production-test
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
6. Desplegar y probar `/health`, `/login`, rutas protegidas, rechazo del bypass,
   ausencia de tokens y comportamiento móvil. No afirmar éxito sin esas pruebas.
7. Configurar proveedor real de correo y crear/verificar una cuenta antes de probar
   datos por la interfaz. Reiniciar/redeployar y verificar persistencia real.

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
