# Correo, verificación y recuperación de cuentas

Fecha: 2026-10-08. Rama: `main`. Estado: implementación local; correo real deshabilitado.

## 1. Resultado y arquitectura conservada

Quedaron preparados registro con cuenta pendiente, confirmación explícita, recuperación temporal y notificación de cambio de contraseña. Se conservan Razor Pages, Blazor Server, EF Core/SQLite, `AccountService`, Identity `PasswordHasher<User>`, cookies y sesiones persistentes existentes. No se incorporaron Identity completo, JWT, otro proveedor de autenticación ni una base nueva.

La inspección encontró infraestructura segura reutilizable: `User.EmailVerified`, `AccountToken`, Data Protection temporal, `AccountLoginSession`, antiforgery y límites de solicitudes. Los problemas concretos eran consumo de verificación mediante GET, ausencia de transporte Production, mensajes que afirmaban envío sin conocer su resultado y falta de notificación/reintento durable.

## 2. Abstracción de correo

`IEmailSender` transporta un `EmailMessage` reutilizable. `AccountEmail` mantiene la interfaz `IAccountEmail` anterior y genera plantillas de texto para `verify`, `reset` y `password-changed`. Ni las plantillas ni los servicios contienen la dirección provisional del remitente: está únicamente en configuración.

El transporte está separado de registro/tokens. La solicitud pública registra un trabajo durable y responde sin esperar SMTP. Esto evita diferencias de tiempo de red entre correos existentes e inexistentes. Registro duplicado tiene el mismo destino que registro nuevo y realiza trabajo de hashing comparable; no se promete tiempo constante entre solicitudes.

## 3. Simulación Development

Development utiliza siempre `DevelopmentEmailSender`, aun si alguien configura opciones SMTP. No contacta servidores de correo. Produce JSON privado en `App_Data/development-emails` con destinatario, propósito, asunto, cuerpo, enlace y fecha. Estos archivos sí contienen enlaces sensibles de prueba: no deben copiarse, publicarse ni adjuntarse a logs.

Se mantienen las exclusiones existentes de Git/publish y no existe endpoint para acceder al outbox. El proveedor simulado rechaza Production. El adaptador `DevelopmentAccountEmail` se conserva para verificadores existentes. Una instancia Development aislada puede configurar un key ring temporal mediante `DataProtection:KeysPath`; no necesita usar las claves reales del equipo.

## 4. SMTP preparado

`SmtpEmailSender` usa `System.Net.Mail` de .NET, sin dependencias nuevas. Soporta **STARTTLS obligatorio**, normalmente puerto 587. `EnableSsl=true`, credenciales explícitas y validación normal de certificados; nunca se habilitan credenciales de Windows por defecto, modo plaintext ni omisión de validación de certificados. Timeout configurable entre 1 y 30 segundos, inicialmente 20.

Limitación deliberada: este adaptador soporta STARTTLS, no TLS implícito/SMTPS 465; se rechaza 465 y cualquier modo diferente de `StartTls`. Microsoft desaconseja `SmtpClient` para desarrollos SMTP generales nuevos; aquí se aisló un adaptador de alcance limitado, sin agregar paquetes, para preparar 587/STARTTLS. Si posteriormente se necesita 465/OAuth u otras capacidades, se puede reemplazar solamente este transporte por uno moderno. Referencias: [SmtpClient](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient?view=net-10.0) y [EnableSsl/STARTTLS](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl?view=net-10.0).

`Available` exige Enabled, host, puerto, seguridad admitida, usuario, contraseña, remitente válido y timeout válido. Production sin configuración no crea cuentas ni emite enlaces desde estos formularios: devuelve un mensaje controlado de indisponibilidad. Los errores SMTP/autenticación/timeout/conexión se convierten en una excepción genérica sin respuesta del servidor ni inner exception sensible.

## 5. Registro y confirmación pendiente

Se validan nombre, apellido, correo y contraseña en servidor; se crea la cuenta con hash y `EmailVerified=false`. Se reserva token + trabajo de verificación en una transacción. La pantalla pendiente dice que la solicitud fue recibida y que el envío se procesará **si corresponde**; no afirma que un correo fue enviado/recibido.

La cuenta duplicada no se anuncia públicamente ni se modifican sus datos. Si la cuenta nueva existe pero el proveedor falla después, continúa pendiente. Puede solicitar otro enlace después del intervalo permitido.

## 6. Confirmación explícita y páginas de enlaces

GET `/verificar-correo?token=...` comprueba validez sin consumir ni verificar. El usuario pulsa **Verificar correo** en un formulario POST con antiforgery. La operación consume el token una sola vez y marca únicamente a su propietario como verificado. Un escáner de correo que carga GET no activa la cuenta.

Se reutilizan las páginas, clases, tipografía, estilos y prevención de doble envío existentes. Enlace inválido/vencido/usado muestra un mensaje genérico. `/nueva-contrasena` también valida en GET sin cambiar la contraseña; el cambio ocurre mediante POST. Los recursos de estas pantallas son locales.

## 7. Recuperación

Para direcciones válidas existentes/inexistentes se muestra el mismo mensaje condicional, sin afirmar entrega. Si corresponde, se encola un enlace para el usuario encontrado en servidor; la solicitud no acepta un UserId del navegador. El proveedor no interviene en la latencia de esa respuesta.

El POST de nueva contraseña valida longitud 12–256 y confirmación exacta. Una contraseña inválida no consume el token. Un token de verificación no sirve para recuperar, ni un token de A puede modificar B.

## 8. Tokens y vencimientos

Se conserva `Aquarella.AccountTokens.v1` con `ITimeLimitedDataProtector`. El token autentica ID de fila, usuario y propósito; SQLite guarda una referencia, expiración y uso, **no el bearer token ni un enlace completo**. No se agregó una criptografía casera ni una copia innecesaria del token.

Valores configurables y validados:

| Configuración | Inicial | Rango aceptado |
|---|---:|---:|
| `Accounts:Mail:VerificationHours` | 24 h | 1–168 h |
| `Accounts:Mail:RecoveryMinutes` | 30 min | 1–120 min |
| `Accounts:Mail:ResendSeconds` | 60 s | 1–3600 s |
| `Accounts:Mail:SendsPerHour` | 5 | 1–10 |
| `Accounts:Mail:NotificationRetryMinutes` | 5 min | 1–60 min |

Cada uso requiere fila vigente, usuario/propósito correspondientes y ausencia de consumo. Consumo, actualización de cuenta y revocación relacionada son transaccionales. Al consumir un enlace se invalidan también los demás del mismo usuario/propósito.

## 9. Reenvío, límites y fallo de entrega

Se conservan 12 POST/minuto/IP/ruta, sin cola de espera, 429 y Retry-After. También hay presupuesto para GET de las dos páginas de validación de tokens. Normalización de ruta mantiene el mismo límite entre mayúsculas o barra final.

Una reserva SQLite serializada limita cada usuario/propósito a un envío por minuto y cinco por hora, compartido entre instancias/stores y tras reiniciar. Los intentos fallidos también cuentan para evitar spam. Un reenvío exitosamente aceptado por el transporte invalida enlaces anteriores; mientras está pendiente, y si falla, el enlace anterior entregado sigue vigente hasta su vencimiento/consumo. El token del envío fallido se revoca y el trabajo queda cancelado, sin reintentar indefinidamente.

## 10. Cambio y revocación de sesiones

Una transacción guarda el nuevo hash, consume los tokens, elimina las sesiones previas y agrega la notificación durable. Si falla la escritura, todo se revierte: contraseña, sesiones y token conservan su estado previo para permitir reintento.

Después del commit se intenta la notificación. Un fallo no revierte la contraseña ni devuelve falsamente un error de cambio. En Production la validación de sesiones exige además usuario verificado con hash; una sesión Development persistida para una cuenta pendiente no habilita acceso Production. El bypass sigue siendo únicamente Development.

## 11. Cola y reintento operativo

`AccountEmailWorker` procesa hasta 20 trabajos por pasada, cada cinco segundos, en serie. Una reclamación condicional persistente de dos minutos impide entregas concurrentes del mismo trabajo. Al detener la instancia no se toman nuevos trabajos; el envío actual tiene timeout acotado. Navegar no cancela un cambio de contraseña ya persistido.

Las notificaciones de contraseña tienen máximo cinco intentos, con intervalo inicial de cinco minutos, sobreviven reinicios y no contienen contraseñas ni enlaces. `Mi cuenta` permite reintentar notificaciones propias mediante POST antiforgery; el ID del propietario deriva de la sesión, no del formulario. El reintento respeta intervalo, lease y máximo. Cuando se agota, queda evidencia pendiente y se requiere intervención del responsable; no se reinicia el contador automáticamente.

SMTP no ofrece garantía de exactly-once: si acepta el mensaje y el proceso cae antes de marcarlo enviado, un reintento puede repetir la misma notificación. La reclamación evita simultaneidad ordinaria; este caso de entrega incierta se documenta y los reintentos siguen acotados. No se garantiza recepción final en bandeja: `SentAt` significa aceptación por el transporte.

## 12. URLs, secretos y logs

Production exige `Accounts:PublicOrigin` HTTPS válido, sin credenciales, ruta adicional, query ni fragmento y sin loopback/localhost. Se usa el origen configurado del servidor, no Host del usuario. Development permite fallback exclusivamente para Host `localhost` HTTPS. No se agregaron redirecciones externas.

`Referrer-Policy: no-referrer` también está en errores/rechazos; las páginas de tokens llevan `no-store`, incluso 429. No hay analytics ni recursos externos en el layout de cuentas. Los logs propios incluyen propósito/ID interno/tipo de excepción, nunca contraseñas, tokens, cuerpos, destinatarios o enlaces. No se habilitó sensitive data logging de EF.

Los enlaces siguen llegando al navegador y al correo: el usuario debe tratarlos como secretos. Antes de activar un proxy público hay que comprobar que sus access logs no registren query strings con tokens; esta tarea no cambió ni desplegó infraestructura.

## 13. Persistencia y migración preparada

Nueva migración aditiva `20261008090000_AccountEmailDelivery`:

- `AccountTokens.CreatedAt` para límites de envío persistentes, con valor mínimo para filas anteriores; se mantiene su expiración/uso original.
- Índice compuesto usuario/propósito/fecha.
- `AccountEmailNotifications` para propósito, referencia de token opcional, origen público validado y estado de cola/lease/intentos/entrega/cancelación. Sin contraseñas ni token bearer.

Se probó tanto una base nueva como una actualización desde las cuatro migraciones anteriores con cuenta, negocio y token ficticios; se conservaron los datos y el snapshot coincide con el modelo. No se ejecutó `database update` contra la base real, ni se inició el proyecto con su configuración real.

**Antes de ejecutar esta versión sobre datos reales:** revisar y respaldar la DB. El inicio existente llama `MigrateAsync`; por lo tanto ejecutar la nueva versión sobre la base real aplicará esta migración automáticamente. La autorización de esta tarea cubrió preparar y probar en TEMP, no ejecutar esa aplicación real. No se cambió ese mecanismo de inicio ni se aplicó un downgrade.

## 14. Pruebas de correo

`AccountMailVerification`: 83 comprobaciones del verificador principal y un proceso hijo con claves/SQLite temporales. Cubre registro inválido/válido/pendiente, acceso Production denegado para sesión de pendiente, hashing, verificación explícita, lectura repetida, propósito/propietario, reenvío, expiración, recuperación, contraseña inválida/single-use, revocación, aviso sin secretos, rollback real con interceptor, fallos de entrega, conservación del enlace previo, límites concurrentes/horarios, reintentos concurrentes y máximo, reinicio, logs redactados y origen seguro.

SMTP se probó únicamente con servicios falsos de **loopback**: ausencia de STARTTLS (no se transmite AUTH), timeout y conexión rechazada. El simulador genera contenido privado y rechaza Production. No se probó autenticación real contra Gmail ni entrega externa; errores del proveedor se ejercitan mediante emisor ficticio y controles del adaptador.

## 15. Regresiones y recorrido local

| Verificador | Resultado |
|---|---|
| AccountMailVerification | PASS |
| AccountHttpVerification | PASS: HTTPS temporal, registro duplicado genérico, GET sin consumo, POST antiforgery, recuperación, contraseña/notificación, cookies y límites |
| DevelopmentAccessVerification | PASS: sesión, rutas, logout/reingreso y datos invariantes en fixture |
| DatabaseVerification | PASS: Perfil/reintento, cuentas, tokens, importación, Stock/Precios/Agenda/aislamiento |
| PersistenceVerification | PASS: aislamiento A/B, concurrencia Perfil, sesiones, rollback, integridad, backup y reinicios |
| ReliabilityVerification | PASS: concurrencia Precios, es-AR, malformados y navegación/desconexión |
| StockIntakeVerification | PASS: validación, atomicidad, idempotencia y aislamiento |
| ProductVisualVerification | PASS: 90 assets, clasificación y miniaturas compartidas |
| AiSettingsVerification | PASS |
| NoticesVerification | PASS |
| MetricsVerification | PASS |
| MetricsPresentationVerification | PASS |
| ProductionHostingVerification | PASS: HTTPS/proxy, cookies, bypass rechazado, correo no configurado, persistencia/reinicio |
| SecurityVerification | PASS: headers, rutas, CSRF, XSS, sesiones, overposting y logs |

`legacy-recovery.test.mjs` y `metrics-motion.test.mjs`: PASS. No se actualizaron paquetes.

Se inició una copia local aislada con SQLite, key ring y correos ficticios en TEMP, utilizando HTTPS y el certificado local existente, que se comprobó válido sin modificarlo. Se corrigió una dependencia previa de inicio: al usar DB externa con usuario legacy, faltaba crear `App_Data` antes de escribir el enlace privado. El recorrido posterior pasó. Solo se finalizaron procesos creados por estos verificadores.

El key ring temporal genera el aviso habitual de claves sin cifrado en reposo; no es un warning del compilador ni se suprimió esa evidencia. Es la limitación de Data Protection ya documentada en la auditoría anterior, no una conexión SMTP ni una credencial en logs. Desconexión real SignalR, visual responsive y entrega real quedan para comprobación manual; no se sustituyeron por pruebas ficticias.

## 16. Compilación y comandos reproducibles

`dotnet build` de la solución y de todos los verificadores, con salida temporal: **0 errores y 0 warnings**. Publish local Release también correcto. Se usaron paquetes del caché local con NuGetAudit deshabilitado para estos builds; no equivale a una nueva auditoría de vulnerabilidades de dependencias.

Desde la carpeta del repositorio `Aquarella`, PowerShell:

```powershell
dotnet build Aquarella.slnx --artifacts-path "$env:TEMP\aquarella-mail-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet build tools/AccountMailVerification --artifacts-path "$env:TEMP\aquarella-mail-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet "$env:TEMP\aquarella-mail-build\bin\AccountMailVerification\debug\AccountMailVerification.dll"
# Compilar también DevelopmentAccessVerification antes del recorrido HTTP:
dotnet build tools/DevelopmentAccessVerification --artifacts-path "$env:TEMP\aquarella-mail-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet build tools/AccountHttpVerification --artifacts-path "$env:TEMP\aquarella-mail-build" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet publish Aquarella/Aquarella.csproj -c Release --artifacts-path "$env:TEMP\aquarella-mail-build" -o "$env:TEMP\aquarella-mail-publish-review" --source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false
dotnet "$env:TEMP\aquarella-mail-build\bin\AccountHttpVerification\debug\AccountHttpVerification.dll" "$env:TEMP\aquarella-mail-publish-review"
```

El runner HTTP exige un publish sin App_Data, crea sus propios fixtures TEMP y se niega a utilizar los datos del proyecto. El intervalo de un segundo utilizado por su prueba de reenvío solo se configura en esa instancia. Los defaults del producto siguen en 60 segundos.

## 17. Configuración pendiente de Gmail

Todavía no se inició sesión en Google, no se generó contraseña de aplicación, no se pidieron credenciales y no se abrió ninguna conexión con Gmail. El remitente provisional se puede cambiar sin modificar código.

Variables de servidor a configurar posteriormente, fuera de Git:

| Variable | Valor/uso |
|---|---|
| `Email__Smtp__Enabled` | `true` únicamente al aprobar envíos reales |
| `Email__Smtp__Host` | SMTP del proveedor; para Gmail normalmente `smtp.gmail.com` |
| `Email__Smtp__Port` | `587` |
| `Email__Smtp__Security` | `StartTls` |
| `Email__Smtp__Username` | cuenta SMTP autorizada |
| `Email__Smtp__Password` | contraseña de aplicación, almacenada como secreto |
| `Email__Smtp__FromAddress` | remitente autorizado; inicialmente el provisional |
| `Email__Smtp__FromName` | `Aquarella` o nombre elegido |
| `Email__Smtp__TimeoutSeconds` | `20` inicialmente |
| `Accounts__PublicOrigin` | origen HTTPS público válido y coherente con AllowedHosts |

No pegar secretos en appsettings, comandos visibles compartidos, capturas, logs, archivos de prueba ni este informe. El sistema se entrega con `Enabled=false`, host/usuario/contraseña vacíos.

## 18. Comprobación manual antes de Production

1. Revisar diff y migración; trabajar primero con una copia temporal y backup. No iniciar esta versión contra la DB real hasta autorizar la migración.
2. En Development aislado, registrar una cuenta ficticia; intentar login antes de verificar: rechazado.
3. Leer JSON privado del outbox, abrir enlace: todavía no verifica; pulsar Verificar correo: habilita. Reusar: rechazado.
4. Reenviar tras intervalo; comprobar que el enlace previo se invalida al completar la entrega simulada. Probar correo inexistente y verificado: misma respuesta genérica.
5. Solicitar recuperación; rechazar contraseña corta, guardar válida, comprobar login viejo rechazado y sesiones anteriores revocadas.
6. Verificar mensaje `password-changed` sin contraseña; provocar fallo con emisor de prueba, reiniciar fixture y comprobar reintento acotado.
7. Revisar teclado, lector de pantalla, mobile y doble click en las pantallas existentes, sin rediseñarlas.
8. Después de aprobar SMTP: configurar secretos, dominio/origen y validar TLS/certificados/credenciales/aceptación y entrega real a una cuenta controlada. Comprobar carpeta spam, cuotas y política del proveedor. Esta prueba real no fue ejecutada.
9. Revisar permisos y backup del volumen/key ring, acceso a logs del proxy sin query sensible, monitoreo de fallos y procedimiento ante cinco intentos agotados.

No se habilitaron registro público, Railway, Docker ni deployment. La implementación está preparada y probada con simulación; Production requiere las decisiones/configuraciones anteriores.

## 19. Archivos modificados y dependencias

Aplicación: `Data/Entities.cs`, `Data/AquarellaDbContext.cs`, snapshot y dos archivos de la nueva migración; `Services/Accounts/AccountEmail.cs`, `AccountService.cs`, nuevos `AccountMailOptions.cs` y `AccountEmailWorker.cs`; `Pages/Access.cshtml[.cs]`, `Pages/Account.cshtml[.cs]`; `Program.cs` y `appsettings.json`.

Verificación: nuevos `tools/AccountMailVerification/AccountMailVerification.csproj` y `Program.cs`; `tools/AccountHttpVerification/AccountHttpVerification.csproj`, `Program.cs` y nuevo `Isolated.cs`; `tools/DevelopmentAccessVerification/Program.cs`; contador de migraciones en `tools/PersistenceVerification/Program.cs`; SMTP explícitamente deshabilitado en los runners `ProductionHostingVerification` y `SecurityVerification`. Este informe es nuevo. No se modificaron módulos ni estilos.

Dependencias nuevas: **ninguna**. Los cambios en csproj corresponden a referencias locales de verificadores, no paquetes del producto.

## 20. Git y límites de esta entrega

Rama `main`, HEAD inicial `6c78d2c`. Cambios locales pendientes para revisión; sin staging, commit, push, pull, merge, rebase, reset ni cambio de rama. Se preservaron las siete imágenes de preview que ya estaban sin versionar y los informes anteriores. No se agregaron secretos, SQLite, logs ni correos privados al índice.

No se modificó la DB real, puertos de la aplicación del usuario, launchSettings, certificados, Docker/Railway ni servidores de terceros. Las bases, claves, logs y correos de las pruebas están únicamente en carpetas temporales de los verificadores.
