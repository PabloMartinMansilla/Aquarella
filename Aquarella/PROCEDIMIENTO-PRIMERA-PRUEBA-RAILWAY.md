# Primera prueba técnica de Aquarella en Railway

Preparado el 10 de octubre de 2026. **Procedimiento para revisar y autorizar; no se ejecutó ningún despliegue desde estas tareas.** No reemplaza DEPLOYMENT.md ni el informe de preparación existente. Los resultados del apartado 1 corresponden a la preparación anterior; esta actualización documental no repite esas pruebas.

Actualización: el propietario confirma que YA existen el proyecto **Aqua-Rella** y el servicio **Aquarella-web**, conectado al repositorio GitHub, rama **main**, con Dockerfile seleccionado como método de construcción. Se reutilizan esos recursos; no crear otro proyecto ni otro servicio. Todavía no se realizó el primer despliegue autorizado. El estado efectivo de autodeploy, deployments, volumen y entorno debe comprobarse en Railway: no se inspeccionó ni modificó la cuenta desde esta revisión documental.

## 1. Resultado de la revisión y evidencia

Se revisaron DEPLOYMENT.md, PREPARACION-RAILWAY-BETA-2026-10-10.md, Dockerfile, .dockerignore, Program.cs, ProductionHosting.cs, configuración, DatabaseMaintenance, DatabaseMigrationSafety y el procedimiento de cuentas privadas.

- Rama local: main; cambios anteriores pendientes conservados. Una publicación desde GitHub NO incluiría automáticamente esos cambios sin guardar/subir. La versión de código que se pruebe debe revisarse y aprobarse primero.
- Docker y railway no están disponibles en PATH en esta PC. No se instalaron. Construcción de imagen, librerías nativas SQLite Linux, montaje, permisos Linux, locking Linux, proxy real, TLS y WebSockets: **pendientes**.
- Se ejecutó la herramienta publicada existente en Windows, sobre una carpeta TEMP nueva y ficticia: Missing → inicialización explícita → Current con las cinco migraciones → backup → restauración nueva → Current → segundo mantenimiento AlreadyCurrent. Todo aprobado. No se inició Blazor ni se accedió a la base local.
- inspect-private-beta sobre esa base vacía devolvió Ready=false, exit code 2, cero usuarios/sesiones/tokens/correos: esperado. Una base vacía permite probar infraestructura, pero no login válido ni circuitos autenticados.
- La revisión anterior documentó 17 verificadores .NET, 2 pruebas JavaScript y build 0 errores/0 warnings. Son resultados anteriores; no se repitió esa suite en esta tarea de procedimiento sin cambios de código.

DatabaseMaintenance se publica en /maintenance/DatabaseMaintenance.dll, net10.0 framework-dependent, dentro de la imagen ASP.NET 10. No necesita SDK, dotnet-ef, sqlite3, User Secrets, host Blazor ni servicios de correo. Las rutas y el empaquetado son compatibles con Linux por inspección; ejecutar el comando dentro de la imagen es la prueba que todavía falta.

## 2. Secuencia y controles de autorización

1. Revisar este procedimiento y elegir una copia de código aprobada, sin datos ni secretos.
2. Abrir Aqua-Rella → Aquarella-web y desactivar autodeploy ANTES de cualquier push a main; comprobar que el cambio quedó aplicado y que no hay deployments automáticos pendientes.
3. Revisar el entorno existente, conservar cualquier volumen/dato y preparar /data, una réplica, variables iniciales y arranque temporal de mantenimiento en Aquarella-web.
4. Solo con autorización separada de commit/push, subir el código aprobado a main con autodeploy confirmado desactivado. **Con autorización de despliegue**, iniciar manualmente ese commit en el contenedor temporal del mismo servicio.
5. Conectar por SSH, comprobar montaje y ausencia de Aquarella, inicializar y verificar SQLite.
6. Generar/copiar dominio gratuito real; completar PublicOrigin y AllowedHosts antes de arrancar Aquarella.
7. Cambiar el arranque al CMD normal y activar /health; revisar/aplicar ese despliegue manualmente.
8. Probar infraestructura y Login público. Detenerse ante un rechazo de Host/proxy; no relajar controles.
9. Bajo autorización separada de cuentas/correos, preparar la base NUEVA de beta con Gmail local, verificar y trasladar un respaldo por canal privado. Restaurar a otro archivo nuevo con la aplicación detenida.
10. Probar autenticación, Blazor/WebSockets, módulos con datos ficticios y persistencia tras reinicio/redeploy.

La preparación actual NO autoriza los pasos que publican código, transfieren cuentas o envían correo. Ningún comando de esta guía se ejecutó contra Railway.

## 3. Desde la interfaz de Railway

1. Abrir el proyecto Aqua-Rella y seleccionar Aquarella-web en el entorno donde está conectado a main. Comprobar que no haya otros servicios en ESE entorno ni TCP proxy. Si los hay, detenerse: el modo dedicado no queda acreditado. No agregar plantillas, bases ni Redis.
2. Antes de subir cambios, seguir el apartado de autodeploy siguiente. Revisar Deployments, incluyendo estados queued/building/deploying/waiting. “Sin despliegue autorizado” no demuestra que nunca se haya iniciado uno automáticamente. Si aparece una instancia activa o datos inesperados, detenerse y revisar antes de tocar almacenamiento. El nombre del entorno de Railway no sustituye ASPNETCORE_ENVIRONMENT=Production.
3. Revisar primero si Aquarella-web ya tiene un volumen adjunto. Si existe en /data, conservarlo y su identidad; no recrearlo, vaciarlo ni cambiar su montaje para empezar de cero. Si no existe y no hay datos/instancias que preservar, crear uno desde el canvas/paleta, seleccionar Aquarella-web y establecer Mount Path=/data. Si existe con otro montaje o aparecen datos persistentes/efímeros previos, detenerse para identificar y respaldar antes de cambiar rutas: adjuntar/remontar un volumen puede ocultar archivos anteriores. No montar /app. Confirmar una región y una réplica total; desactivar Serverless/suspensión para la prueba Blazor y el mantenimiento. No se ejecuta ningún cambio desde este documento.
4. Settings → Build: conservar Dockerfile como método seleccionado y verificar raíz de construcción = raíz del repositorio, Dockerfile = Dockerfile. No usar Aquarella/Aquarella como raíz: rompería los COPY actuales. No añadir un Build Command alternativo; el Dockerfile hace restore/publish. Conservar repositorio y rama main salvo la desconexión temporal opcional explicada abajo.
5. Pre-deploy Command vacío. Para el primer contenedor temporal: Start Command indicado abajo, Healthcheck Path vacío y Restart Policy=Never. No hay servidor web en esta etapa; no esperar /health ni interpretar el estado Running como aplicación lista.
6. Cargar las variables de la tabla. Las dos variables que dependen del dominio pueden completarse después del arranque temporal, pero antes del arranque web.
7. Revisar Details de los cambios pendientes. Railway permite aplicarlos sin redeploy con Alt + Deploy; comprobar la opción que muestra la interfaz antes de confirmar. No pulsar un despliegue mientras no esté autorizado. Los cambios de networking se aplican inmediatamente. [Cambios staged](https://docs.railway.com/deployments/staged-changes).
8. Mantener GitHub conectado con autodeploy desactivado si ese estado se puede confirmar. No hace falta desconectarlo como procedimiento normal. Revisar también que ningún workflow/hook/operador invoque despliegues explícitos: Disable no impide acciones manuales/API ni cancela intentos ya iniciados. No confiar en Wait for CI como aprobación humana. [Autodeploy](https://docs.railway.com/deployments/github-autodeploys).
9. Cuando estén aprobados el código, commit/push y el primer despliegue, confirmar el hash esperado en GitHub/main, que no haya commits posteriores y que el Start Command aplicado siga siendo sleep. Desde Aquarella-web usar Ctrl+K → Deploy Latest Commit para construir ese código; comprobar el SHA en el deployment antes de continuar. Si no coincide, detenerse. No usar Redeploy de un intento viejo para traer los cambios nuevos. Revisar logs de build/estado; no usar imágenes con cuentas/bases.
10. Networking: si ya existe dominio gratuito, conservarlo y copiar el hostname real; si no, Generate Domain. Comprobar sufijo .up.railway.app. Si la interfaz exige deployment previo, hacerlo después del contenedor temporal. Su Target Port debe coincidir con PORT del servicio, no con 7188/18188 ni con 443 de Kestrel. No habilitar TCP Proxy.
11. Tras mantenimiento aprobado, Start Command=dotnet /app/Aquarella.dll, Healthcheck Path=/health, timeout inicial predeterminado 300 s, Restart Policy=On Failure con reintentos limitados. Mantener una réplica. Revisar Details y autorizar/aplicar el cambio web.

La fuente del servicio existente se revisa en Settings; no se reemplaza el servicio para modificarla. [Railway Services](https://docs.railway.com/services). El volumen se monta al ejecutar el contenedor, no en build ni pre-deploy. [Volúmenes](https://docs.railway.com/volumes).

### Desactivar autodeploy antes del push: camino recomendado

1. Aqua-Rella → entorno correcto → Aquarella-web → Settings → bloque Source/GitHub. Comprobar repositorio conectado y rama main, sin cambiarla.
2. En el control de despliegues automáticos del repositorio pulsar **Disable**. Si ya muestra **Enable**, el control está deshabilitado: no pulsar Enable. Los rótulos/ubicación pueden variar; identificar el control de autodeploy, no el botón general Deploy.
3. Si aparece como cambio staged, revisar Details y aplicar SOLO ese cambio sin redeploy mediante la opción que Railway documenta como Alt + Deploy; confirmar que la interfaz indica aplicación sin despliegue. El botón aplica un lote: si existen otros cambios pendientes no aprobados y no se puede aislar la desactivación, no aplicarlos ni descartarlos; detenerse para revisar el lote. Si no se puede identificar la opción sin redeploy con certeza, no pulsar Deploy normal y no hacer push. Recargar Settings y comprobar que sigue deshabilitado: un cambio meramente staged no basta.
4. Revisar Deployments. Disable no cancela lo que ya esté queued/building/deploying/waiting. Para un intento automático identificado en curso, el operador puede usar su menú de tres puntos → **Abort deployment**; confirmar que terminó. No usar Remove/rollback ni detener una instancia con datos desconocidos sin revisarla primero. [Acciones de deployment](https://docs.railway.com/deployments/deployment-actions).
5. Solo después de comprobar esos controles y recibir autorización de Git, hacer el futuro commit/push. Revisar nuevamente Deployments: no debe aparecer un build iniciado por ese push. No crear un commit de prueba vacío para comprobarlo ni disparar workflows de despliegue.
6. Mantener autodeploy desactivado durante mantenimiento, prueba web y revisión posterior. La publicación manual requiere su propia autorización y la revisión de la configuración aplicada; subir a main no equivale a autorizar publicar.

### Desconexión temporal: únicamente si no se puede asegurar el control anterior

No es necesaria si Disable quedó aplicado. Si falta el control o su estado no es verificable, no hacer push y usar esta alternativa antes de continuar:

1. En Aquarella-web → Settings → Source, anotar repositorio, rama main y opciones de build/start existentes, sin copiar secretos.
2. Usar la acción **Disconnect** del vínculo al repositorio en ese bloque (puede estar en su menú). No borrar el servicio/proyecto/volumen ni revocar globalmente la integración GitHub de la cuenta. Si esa acción no está disponible o pide una operación distinta/destructiva, detenerse: no improvisar otro botón.
3. Si el cambio queda staged, aplicarlo sin redeploy como en el apartado anterior. Recargar y confirmar que Aquarella-web ya no muestra fuente GitHub conectada. Revisar/cancelar intentos automáticos en curso antes de un push autorizado. Conservación del servicio/volumen no autoriza cambiar sus datos.
4. Para el primer test manual, preferir mantenerlo desconectado y usar la alternativa CLI hacia el MISMO Aquarella-web. No crear Empty Service. Si se decide reconectar, hacerlo solo cuando un primer build ya esté autorizado y la configuración temporal de mantenimiento esté aplicada: Settings → Source → Connect Repo, mismo repositorio y main. La reconexión puede disparar un build; no tratarla como acción de lectura ni prometer que no desplegará. Desactivar autodeploy otra vez y revisar intentos antes de pushes posteriores.

Esta guía describe acciones futuras del operador; no se deshabilitó ni desconectó nada durante la revisión. Si hay un servicio adicional, deployment activo con datos, triggers externos desconocidos o no se puede asegurar la ausencia de despliegue, conservar todo y detenerse antes de subir código/cambiar almacenamiento.

### Fuente de código y CLI: alternativa, no requisito del camino GitHub

El camino recomendado reutiliza GitHub/main con Disable aplicado y deployment manual; no necesita railway up. La CLI seguirá siendo una opción para SSH. Si se desconecta GitHub y se elige carga local, el propietario deberá instalarla/iniciar sesión manualmente. No introducir tokens en informes o comandos compartidos.

Preparar posteriormente una carpeta revisada de SOLO código, con Dockerfile, .dockerignore y estas dos ramas de directorios: Aquarella/Aquarella y Aquarella/tools/DatabaseMaintenance. Excluir bin, obj, App_Data, .env, bases/journals, secretos, certificados, claves, backups y configuración privada. Conservar las rutas COPY. No copiar la base Development. Revisar el listado de archivos antes de cargar.

Desde esa carpeta, únicamente tras aprobar el despliegue:

```powershell
railway login
railway link
# Elegir Aqua-Rella, el entorno existente y Aquarella-web, sin crear recursos.
railway up --service ID_REAL_SERVICIO --environment ID_REAL_ENTORNO
```

Los ID_REAL son marcadores, no valores para copiar literalmente. No usar railway up --new ni --no-gitignore. railway up respeta .gitignore/.railwayignore; .dockerignore protege el contexto de Docker, pero NO debe asumirse que filtra la carga CLI. No cargar el workspace completo sin revisar. [Comando up](https://docs.railway.com/cli/up).

## 4. Variables exactas

### Obligatorias, configuradas manualmente para el arranque web

| Variable | Valor |
| --- | --- |
| ASPNETCORE_ENVIRONMENT | Production |
| DOTNET_ENVIRONMENT | Production |
| ProductionStorage__Root | /data |
| ConnectionStrings__Aquarella | Data Source=/data/aquarella.db |
| DataProtection__KeysPath | /data/data-protection-keys |
| Accounts__PublicOrigin | https://HOSTNAME_REAL_GENERADO_POR_RAILWAY |
| AllowedHosts | HOSTNAME_REAL_GENERADO_POR_RAILWAY;healthcheck.railway.app |
| Accounts__Registration__Enabled | false |
| Email__Smtp__Enabled | false |
| ReverseProxy__RailwayDedicatedEnvironmentEnabled | true |

No incluir comillas como parte del valor en la interfaz. PublicOrigin no lleva ruta, query ni puerto alternativo. AllowedHosts lleva hostnames, no https://. Registro/SMTP se explicitan aunque sus defaults actuales sean cerrados/deshabilitados.

### Proporcionadas por Railway; no fabricar ni sobrescribir

PORT, RAILWAY_PROJECT_ID, RAILWAY_ENVIRONMENT_ID, RAILWAY_SERVICE_ID, RAILWAY_PUBLIC_DOMAIN, RAILWAY_VOLUME_MOUNT_PATH. Las requiere el modo dedicado; el último debe ser /data. Verificar su presencia sin volcar el entorno entero. RAILWAY_VOLUME_NAME y RAILWAY_REPLICA_ID son metadatos auxiliares. [Referencia](https://docs.railway.com/variables/reference), [PORT y health](https://docs.railway.com/deployments/healthchecks).

### Opcionales

- OpenAI__Enabled=false: explicitarlo conserva IA real deshabilitada; no cargar API key.
- RAILWAY_DOCKERFILE_PATH=Dockerfile: innecesaria si está en la raíz; usar solo si la detección no corresponde.
- RAILWAY_HEALTHCHECK_TIMEOUT_SEC=300: innecesaria con el default; aumentar no arregla una base/proxy inválido.
- RAILWAY_DEPLOYMENT_OVERLAP_SECONDS=0: conservar el default, sin concurrencia de instancias con SQLite.
- RAILWAY_DEPLOYMENT_DRAINING_SECONDS=30: opción operativa para permitir apagado ordenado; confirmar en la prueba que ninguna operación larga quede interrumpida. No equivale a persistir circuitos Blazor.
- RAILWAY_RUN_UID: no configurar por ahora. El Dockerfile actual no declara USER. Comprobar id/permisos; no solucionar un error con chmod 777 ni cambiar de usuario a ciegas.

### Secretos

Esta etapa no necesita contraseña SMTP, API key de correo ni OpenAI. No trasladar User Secrets ni local-keys. Las credenciales de Railway/SSH son del operador y se guardan en su cliente, no dentro de la aplicación. Las claves Data Protection se generan dentro del volumen y son sensibles: no leer/imprimir sus archivos.

### No configurar

ASPNETCORE_FORWARDEDHEADERS_ENABLED=true; ReverseProxy__KnownProxies__*; ReverseProxy__KnownNetworks__*; AllowedHosts=*; SMTP habilitado; registro habilitado; variables de bypass Development; ni TCP proxy. No añadir manualmente RAILWAY_TCP_PROXY_DOMAIN/PORT/APPLICATION_PORT: su presencia no vacía bloquea el modo dedicado.

### Datos por confirmar durante la prueba

Dominio real, PORT y Target Port coincidentes, montaje efectivo, usuario/permisos reales, una instancia total, Host recibido en HTTP/WebSocket, headers del edge y configuración de backups. No sustituirlos por ejemplos ficticios.

## 5. Inicializar la base dentro del volumen

### Arranque temporal, mismo servicio

En Settings → Start Command, para el deployment de mantenimiento autorizado:

```sh
/bin/sh -c "umask 077; exec sleep infinity"
```

Healthcheck vacío, Restart Never, una réplica, volumen /data. Este proceso mantiene el contenedor accesible sin iniciar Aquarella ni migrar. El override se ejecuta en la imagen; no requiere modificar Dockerfile ni Program.cs. La imagen oficial estándar usa una base Linux completa, pero disponibilidad efectiva de sh/sleep sigue pendiente de ejecución. Si el comando falla, detenerse; no instalar herramientas automáticamente. [Start Command](https://docs.railway.com/deployments/start-command), [base oficial .NET](https://github.com/dotnet/dotnet-docker/blob/main/src/runtime-deps/10.0/noble/amd64/Dockerfile).

Tras autorizar y arrancar ese contenedor, desde la terminal del operador:

```powershell
railway ssh --project ID_REAL_PROYECTO --environment ID_REAL_ENTORNO --service ID_REAL_SERVICIO
```

Requiere cliente SSH y clave pública registrada por el propietario. No leer claves privadas ni aceptar una huella SSH inesperada sin verificar. No usar --session: puede instalar tmux automáticamente. railway run no es un sustituto: ejecutaría localmente, fuera del volumen remoto. [SSH](https://docs.railway.com/cli/ssh).

Dentro del shell Linux, comprobar primero:

```sh
set -eu
umask 077
id
test "$RAILWAY_VOLUME_MOUNT_PATH" = /data
test -d /data
test -w /data
test -f /app/Aquarella.dll
test -f /maintenance/DatabaseMaintenance.dll
dotnet --list-runtimes
dotnet /maintenance/DatabaseMaintenance.dll --help
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
```

Confirmar en Railway que solo está activo el deployment de sleep, que la instancia web anterior terminó y que nadie más escribe. --confirm-stopped es una declaración del operador, no un botón que detenga procesos. El lease también debe funcionar en Linux.

**Continuar con la inicialización nueva únicamente si inspect informa State=Missing.** Inspect puede devolver exit 0 con Missing/Pending: el exit code no basta. Tampoco asumir que el volumen está vacío porque todavía no hubo despliegue autorizado. Revisar metadatos de archivos/DB/journals existentes sin leer claves o datos personales; si la conexión actual apunta a otra base, detenerse antes de crear otra y aparentar una pérdida de datos.

Si la base identificada ya existe: Current se reutiliza sin --create; Pending reconocido requiere primero app/escritores detenidos, backup consistente a destino NUEVO y restauración aislada verificada, y luego migrate sin --create bajo autorización. Empty, Locked, Incompatible, varias bases candidatas o archivos inesperados requieren investigación; no inicializar encima, borrar journals ni mover/renombrar la base a ciegas. Los comandos de backup/restore de abajo sirven también para verificar el respaldo de una base existente, usando nombres nuevos; para Pending la restauración debe conservar ese mismo estado compatible, no exigirse Current antes de migrar.

Ejecutar entonces en ese mismo shell:

```sh
dotnet /maintenance/DatabaseMaintenance.dll migrate --database /data/aquarella.db --backup-dir /data/migration-backups --confirm-migrate --confirm-stopped --create
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
```

Esperado: Status=Completed; luego State=Current, Pending=[], cinco migraciones aplicadas:

1. 20261004053300_InitialBusinessData
2. 20261004211838_RealAccounts
3. 20261005000444_UserAiSettings
4. 20261005180000_StockIntakeReceipts
5. 20261008090000_AccountEmailDelivery

Para una base originalmente ausente, BackupPath=null es correcto: no existían datos que respaldar. Se conserva un recibo de mantenimiento. Crear luego el primer backup verificable:

```sh
dotnet /maintenance/DatabaseMaintenance.dll backup --database /data/aquarella.db --destination /data/technical-backups/initial-schema.db
dotnet /maintenance/DatabaseMaintenance.dll restore --database /data/technical-backups/initial-schema.db --destination /data/verification/initial-restore.db
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/verification/initial-restore.db
```

Continuar solo si ambos comandos terminan bien y la copia es Current. Backup/restore usan SQLite BackupDatabase con snapshot, incluyen páginas comprometidas en WAL y verifican integrity_check, schema y datos. No copiar aquarella.db con cp mientras pudiera existir WAL activo. No se necesita ejecutar PRAGMA mediante sqlite3.

Los destinos deben ser nuevos. Si se repite la prueba, elegir otros nombres revisados; no borrar una copia existente para hacer pasar el comando. Las copias dentro del mismo volumen son evidencia de restauración, NO recuperación ante pérdida del volumen. Preparar backup privado fuera de ese volumen antes de incorporar usuarios.

### Errores o interrupción

- Cualquier exit no cero: detener la secuencia, conservar base, journals, backups y recibos.
- Timeout interno de mantenimiento: 2 minutos; no lanzar en paralelo ni agregar reintentos automáticos.
- Si se corta SSH, comprobar que el proceso anterior terminó antes de reintentar. Volver a inspect y revisar el recibo privado del intento; no borrar locks ni __EFMigrationsLock.
- Current: no repetir creación; el mantenimiento sin --create devuelve AlreadyCurrent.
- Pending reconocido: revisar historial/recibo y ausencia de escritores; una continuación autorizada usa migrate sin --create, con backup previo verificado por la herramienta.
- Empty de un archivo reservado por ESTA inicialización: investigar antes de autorizar otro --create.
- Incompatible o Locked: detenerse. No reparar ni restaurar sobre el original. Una recuperación debe restaurar un respaldo verificado a un archivo NUEVO y revisarse antes de cambiar la conexión.

No hay promesa de rollback total frente a una caída entre migraciones. Los controles detectan el estado resultante; nunca se elimina/recrea silenciosamente la base. El arranque web solo acepta esquema Current y no crea/migra archivos.

## 6. Primera validación Docker/Linux

No ejecutada aquí: Docker ausente. Si posteriormente hay Docker con contenedores Linux, desde la raíz del repositorio aprobado, en PowerShell, ejecutar cada comando y comprobar $LASTEXITCODE antes del siguiente:

```powershell
$testSuffix = [guid]::NewGuid().ToString('N')
$testVolume = 'aquarella-test-' + $testSuffix
$testContainer = 'aquarella-test-' + $testSuffix
docker build --tag aquarella:railway-check --file Dockerfile .
docker image inspect aquarella:railway-check --format '{{.Os}}/{{.Architecture}}'
docker volume create $testVolume
docker run --rm --mount "type=volume,source=$testVolume,target=/data" aquarella:railway-check dotnet /maintenance/DatabaseMaintenance.dll migrate --database /data/aquarella.db --backup-dir /data/migration-backups --confirm-migrate --confirm-stopped --create
docker run --rm --mount "type=volume,source=$testVolume,target=/data" aquarella:railway-check dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
docker run -d --name $testContainer -p 127.0.0.1:18080:8080 --mount "type=volume,source=$testVolume,target=/data" -e PORT=8080 -e ASPNETCORE_ENVIRONMENT=Production -e DOTNET_ENVIRONMENT=Production -e ProductionStorage__Root=/data -e 'ConnectionStrings__Aquarella=Data Source=/data/aquarella.db' -e DataProtection__KeysPath=/data/data-protection-keys -e Accounts__PublicOrigin=https://localhost -e AllowedHosts=localhost -e Accounts__Registration__Enabled=false -e Email__Smtp__Enabled=false -e ReverseProxy__KnownProxies__0=127.0.0.1 aquarella:railway-check
docker logs $testContainer
curl.exe --fail --silent --show-error -H 'Host: localhost' http://127.0.0.1:18080/health
curl.exe --silent --output NUL --write-out '%{http_code}' -H 'Host: localhost' http://127.0.0.1:18080/login
docker exec $testContainer sh -c 'test -w /data && test -w /data/data-protection-keys && stat -c "%a %u:%g" /data /data/data-protection-keys'
docker exec $testContainer dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/aquarella.db
docker restart $testContainer
```

Comprobar puerto 18080 libre primero. Esperado: imagen linux, mantenimiento Current, servidor sin excepciones, health 200/OK, login HTTP directo 400, directorios 700. Esperar disponibilidad después del restart y repetir health/inspect. No usar -k, no fabricar headers HTTPS para convertir HTTP en un login inseguro.

Este laboratorio usa el modo existente de proxies conocidos con loopback; NO es prueba del edge Railway ni de un proxy Docker acreditado. Solo cubre imagen, SQLite y lectura health directa. Login válido/cookies/SignalR requieren el HTTPS real de la etapa siguiente. No fabricar RAILWAY_* para llamar a esto una prueba Railway.

Guardar el nombre exacto del volumen nuevo; no usar un bind mount a App_Data. No hacer mantenimiento que escriba con la app activa. Una prueba local adicional de segunda instancia con ESE volumen ficticio debe fallar por el lease; detener/revisar si no ocurre. No crear una segunda instancia en Railway para probarlo.

Si no se dispone de Docker, la misma construcción y comandos /maintenance se prueban dentro del contenedor temporal de Railway, luego /health y restart en ese único servicio. Esa prueba ya es un despliegue: necesita autorización explícita. No considerar Windows equivalente a Linux.

## 7. Proxy HTTPS real y WebSockets

Una vez arrancada Aquarella, usar el dominio REAL; no enviar datos ni credenciales por HTTP. Desde PowerShell del operador:

```powershell
$railwayOrigin = 'https://HOSTNAME_REAL_GENERADO_POR_RAILWAY'
curl.exe --fail --silent --show-error "$railwayOrigin/health"
curl.exe --silent --output NUL --write-out '%{http_code}' "$railwayOrigin/login"
curl.exe --silent --output NUL --write-out '%{http_code} %{redirect_url}' "http://HOSTNAME_REAL_GENERADO_POR_RAILWAY/login"
curl.exe --silent --output NUL --write-out '%{http_code}' -H 'X-Forwarded-Proto: http' -H 'X-Real-IP: invalid' "$railwayOrigin/login"
```

Esperado: health 200/OK, login 200, HTTP GET redirigido a HTTPS y login 200 aun con headers de cliente inválidos si el edge los reemplaza según el contrato comunicado. No ejecutar estas peticiones antes de la autorización/publicación. No usar -i/-v ni guardar headers completos: pueden imprimir Set-Cookie. El último caso es una única petición inocua, no prueba agresiva.

En navegador: certificado válido, Login HTTPS, sin bypass Development ni registro público; rutas protegidas redirigen al Login. Después del lote privado autorizado: entrar, Dashboard → Stock/Precios/Agenda, cerrar sesión y confirmar protección. Revisar redirecciones sin saltos a localhost, http o hosts ajenos.

DevTools → Application/Cookies: comprobar SOLO propiedades de __Host-Aquarella.Auth: Secure, HttpOnly, SameSite=Lax, Path=/, sin Domain explícito. No copiar el valor, screenshots con valores ni exportar HAR. Para sobrevivir a cerrar/reabrir navegador, usar la opción existente de recordar sesión si se desea; no confundir expiración normal con pérdida de claves.

DevTools → Network/WS: tras login válido, comprobar wss://HOST_REAL/_blazor, handshake 101, conexión activa, clicks interactivos y navegación. No exportar URL completa/query de conexión, mensajes/frames ni headers Cookie. Reiniciar posteriormente puede romper el circuito en memoria: recargar y comprobar reconexión/interactividad sin duplicaciones. Railway documenta soporte WebSocket, pero esto no prueba que Aquarella lo haya utilizado. [Headers/TLS/WebSockets](https://docs.railway.com/networking/public-networking/specs-and-limits).

### Lo que se puede confirmar sin cambiar código

Con el middleware actual, GET /login 200 y handshake aceptado implican que Host fue el dominio público permitido (sin puerto o :443), Proto tuvo un único valor https y Real-IP pasó la validación sintáctica. Es evidencia indirecta de esa combinación, NO captura del valor exacto ni prueba de la identidad del emisor.

El Host visible en DevTools es el enviado por el navegador, NO necesariamente el entregado a Kestrel. La documentación garantiza X-Forwarded-Host original; no se encontró contrato explícito de raw Host HTTP/WebSocket. /health 200 solo prueba su excepción: NO confirma el proxy de Login/SignalR.

### Cambio pequeño que requiere autorización adicional

Para registrar exactamente el Host recibido y verificar explícitamente las otras cabeceras desde el servidor falta instrumentación. No se implementó ni se creó endpoint.

Propuesta para otra pasada: una traza temporal en el middleware existente, opt-in/default off y limitada a Production Railway, solo GET /login y handshake /_blazor. Capturar antes de modificar Scheme/IP, sin aumentar niveles de logging globales. Emitir exclusivamente Host público canónico reconocido, presencia/cantidad/validez de X-Forwarded-Proto y X-Real-IP, y coincidencia contra la IP esperada del operador sin emitir esa IP. Valores no reconocidos se resumen como inválidos, sin imprimir texto arbitrario. Nunca cookies, tokens, queries, cuerpos, IDs de usuario/conexión ni IP completa.

El diagnóstico debe estar limitado a la sesión de prueba del operador y retirarse/desactivarse al terminar; la forma concreta de restringirlo necesita revisión antes de codificarlo. No agregar rutas públicas. Solicitar autorización antes de implementar. Mientras no exista esa traza o una confirmación suficiente de Railway, mantener pendiente la medición exacta solicitada.

Si /health pasa pero Login/WS devuelve 400, investigar el Host real y contrato del edge; no activar X-Forwarded-Host ni aceptar hosts internos/todos los proxies para destrabarlo. Cualquier servicio adicional en el entorno invalida el perímetro aprobado y requiere revisión. Los metadatos Railway no autentican al proxy.

## 8. Cuentas privadas: orden y procedimiento existente

Primero la prueba vacía permite inicializar/validar infraestructura. Las cuentas verificadas son necesarias ANTES de acreditar Login válido, WebSockets autenticados y persistencia de datos de negocio. Pueden prepararse localmente de forma independiente, pero no se trasladan a una app activa.

Con autorización adicional de correo/cuentas, seguir DEPLOYMENT.md, apartado “Preparar una base de beta independiente en esta PC”: directorio NUEVO de beta, migración explícita, instancia Development loopback con conexión explícita a esa base y keys locales aparte, registro normal y verificación Gmail. No usar la base Development actual ni editar EmailVerified por SQL.

Cada titular autorizado introduce su contraseña. El enlace localhost debe abrirse en la misma PC/instancia; no funciona como onboarding remoto desde el teléfono del invitado. No crear credenciales predeterminadas ni bypass. Cerrar TODAS las sesiones por el logout normal, terminar la instancia y exigir inspect-private-beta Ready=true, exit 0; backup y restauración aislada verificados. No vaciar tablas para hacer pasar el control.

Después de la prueba de infraestructura Railway, volver el mismo servicio al Start Command de sleep, health vacío y esperar que Aquarella termine. En shell:

```sh
umask 077
mkdir -p /data/import
chmod 700 /data/import
test ! -e /data/import/verified-beta-backup.db
test ! -e /data/beta-initial.db
```

Transferir únicamente el respaldo NUEVO de beta autorizado, por SSH/SFTP privado. Desde la PC, con la clave del propietario registrada y una ruta de respaldo revisada:

```powershell
scp RUTA_PRIVADA_RESPALDO_BETA_VERIFICADO SERVICE_INSTANCE_ID_REAL@ssh.railway.com:/data/import/verified-beta-backup.db
```

Usar Copy Service Instance ID del deployment temporal vigente, NO Service ID; el identificador puede cambiar al redeploy. No subir la base Development actual, local-keys, User Secrets ni archivos de correo. Confirmar destino ausente antes de scp porque scp puede sobrescribir. La transferencia aún no se probó. [SSH/SFTP](https://docs.railway.com/cli/ssh).

En el shell remoto:

```sh
set -eu
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/import/verified-beta-backup.db
dotnet /maintenance/DatabaseMaintenance.dll inspect-private-beta --database /data/import/verified-beta-backup.db
dotnet /maintenance/DatabaseMaintenance.dll restore --database /data/import/verified-beta-backup.db --destination /data/beta-initial.db
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/beta-initial.db
dotnet /maintenance/DatabaseMaintenance.dll inspect-private-beta --database /data/beta-initial.db
```

Exigir Current y Ready=true. Luego cambiar SOLO ConnectionStrings__Aquarella=Data Source=/data/beta-initial.db, restaurar arranque web/health y desplegar manualmente la misma versión aprobada. No sobrescribir aquarella.db. No importar sesiones ni claves locales: el preflight exige ausencia de sesiones/tokens utilizables/correos pendientes; Production mantiene su propio key ring /data.

Esto prepara el lote inicial. No sirve para incorporar lotes nuevos reemplazando la base en uso: eso perdería datos ya generados en Railway. Ese flujo futuro necesita una solución revisada. Sin correo Production, recuperación de contraseña/altas remotas no pueden prometerse; limitar la beta a quienes acepten esa condición.

## 9. Persistencia y criterio de aprobación

Con una cuenta privada autorizada y SOLO datos ficticios, guardar por UI: nombre de negocio de prueba, producto “Prueba Railway” cantidad 7, costo 2000,50 y una nota Agenda. Anotar valores esperados sin credenciales/datos personales. Esperar confirmación de guardado.

Verificar por SSH únicamente metadatos/permisos, sin leer XML:

```sh
test -w /data
test -w /data/data-protection-keys
stat -c '%a %u:%g' /data /data/data-protection-keys
ls -1 /data/data-protection-keys
dotnet /maintenance/DatabaseMaintenance.dll inspect --database /data/beta-initial.db
```

Registrar nombres de key files, no contenidos. Su existencia sola no prueba descifrado de cookies. Dejar una sesión no vencida en el navegador, Restart del deployment web desde Railway, esperar health 200 y recargar: misma cuenta autenticada, datos exactos, UI interactiva. Repetir con redeploy manual de la misma versión, manteniendo volumen/rutas. El circuito abierto puede requerir recarga; no prometer continuidad sin interrupción.

Después detener web y volver a mantenimiento si se quiere una verificación íntegra y backup: inspect de la base activa debe ser Current, backup a destino nuevo y restore a otro destino nuevo verificado. La herramienta comprueba integridad/datos sin imprimir filas. Al volver a web conservar exactamente ruta DB y keys. Un backup dentro del mismo volumen no protege contra perder ese volumen; planificar copia privada externa y restauración antes de invitar personas.

No recrear/eliminar/detachar el volumen para probar persistencia. No usar autodeploy ni rollbacks de código a un modelo anterior sin analizar compatibilidad de schema. /health es readiness de lectura, no backup ni monitor continuo; Railway lo usa al activar deployments y los servicios con volumen tienen una interrupción al redeploy. [Healthchecks](https://docs.railway.com/deployments/healthchecks).

## 10. Bloqueos y resultado de esta preparación

- Falta ejecutar Docker/Linux o el contenedor de prueba Railway autorizado; todavía no hay evidencia de permisos/locking nativos/imagen real.
- Falta medir Host real HTTP/WS y headers. La instrumentación mínima propuesta necesita autorización; no se agregó código.
- Falta seleccionar/aprobar la copia exacta de código que se cargará: hay cambios locales pendientes.
- Falta dominio/PORT efectivos y acceso SSH del operador. No se consultaron credenciales ni se inventaron valores.
- Falta autorizar y preparar el primer lote Gmail/base separada; base vacía no acredita Login exitoso ni cookies autenticadas persistentes.
- Falta plan de backup privado fuera del volumen y restauración; falta correo Production para recuperación remota. No son motivos para eliminar controles o abrir registro.
- Riesgo residual aceptado: un peer que alcance el puerto interno y fabrique Host/Proto/IP válidos es indistinguible del edge. Mantener estrictamente un servicio/instancia, sin TCP proxy ni otros peers.

En la preparación original se añadió este documento; en esta actualización solo se corrige este mismo archivo para reutilizar Aqua-Rella/Aquarella-web. Código, bases y configuraciones locales/remotas sin modificaciones. Los metadatos de base y resultados Windows arriba son evidencia de la preparación anterior, no pruebas repetidas aquí. No se consultaron secretos, abrió/copió/migró la base, creó una cuenta, envió correo, operó Railway, hizo commit/push/deploy ni modificó configuración global de Windows.

**Primera acción concreta del propietario: abrir Aqua-Rella → Aquarella-web → Settings → Source/GitHub y desactivar autodeploy con Disable, comprobando que quede aplicado antes de cualquier push a main. No pulsar Deploy normal.**
