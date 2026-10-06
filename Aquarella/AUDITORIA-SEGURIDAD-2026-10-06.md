# Auditoría de seguridad web de Aquarella — 2026-10-06

## Resumen ejecutivo

**No se encontró un impedimento de seguridad crítico o alto en los flujos locales comprobados para una beta cerrada, pequeña y con cuentas verificadas.** No es una aprobación para abrir el registro público ni una validación del despliegue real.

**La beta con usuarios nuevos aún tiene un impedimento operativo conocido:** Production utiliza `UnavailableAccountEmail`. Registro, verificación por envío de correo y recuperación no están conectados a un proveedor. No habilitar el acceso de Development ni copiar la base local para resolverlo. Definir el flujo de incorporación y recuperación antes de invitar usuarios reales.

Además, falta comprobar avisos actualizados de vulnerabilidades de dependencias. Se respetó la prohibición de contactar servicios externos: se inventariaron paquetes/runtime y se buscó caché de avisos, pero no había avisos utilizables. Por tanto, **no se afirma que los paquetes estén libres de CVE**.

| Severidad | Hallazgos | Corregidos | Pendientes |
|---|---:|---:|---:|
| Crítico | 0 | 0 | 0 |
| Alto | 0 | 0 | 0 |
| Medio | 1 | 0 | 1 |
| Bajo | 5 | 4 | 1 |

Los pendientes de nivel medio/bajo son cuotas de uso por cuenta y protección de claves de Data Protection en disco. Los controles actuales de sesión, aislamiento y validación no se reemplazaron.

## Alcance y estado inicial

- Rama `main`, HEAD inicial `93d7d74`, seguimiento `origin/main`. Sin modificaciones iniciales de código.
- Siete capturas PNG sin seguimiento ya existentes; se conservaron sin modificarlas.
- Revisión de la aplicación .NET 10, Razor Pages, Blazor Server, JavaScript propio, configuración, archivos versionados y herramientas de verificación.
- Pruebas con bases SQLite temporales nuevas, cuentas `example.test`, instancias locales propias y puertos libres. No se escribió ni se probó contra la base real.
- No se accedió a GitHub, Railway, OpenAI, WhatsApp, correo externo, NuGet remoto ni equipos de la red. El dominio ficticio de las pruebas HTTP solo se usó como header Host; el transporte fue loopback.
- Se iniciaron y detuvieron únicamente procesos propios de la auditoría. Las instancias del usuario en 7188/18188 no se cerraron.
- Las auditorías anteriores se conservaron. Sus verificadores se ejecutaron como regresión; no se reimplementaron sus soluciones.

## Superficie de ataque real

| Superficie | Entrada / control relevante |
|---|---|
| Razor Pages de cuentas | `/login`, `/crear-cuenta`, `/verificar-correo`, `/verificar-email`, `/recuperar-contrasena`, `/nueva-contrasena`; formularios POST y enlaces de verificación/reset |
| Cuenta autenticada | `/mi-cuenta`, POST de logout y solicitud de cambio de contraseña; autorización y antiforgery |
| Blazor Server | Dashboard, Perfil, Stock, Precios, Agenda, Métricas, Avisos, preferencias IA, configuración de página, Conversaciones y chat existente; eventos de circuito, no una API REST de negocios |
| SignalR | Negociación `/_blazor/negotiate`; autenticación comprobada: anónimo obtiene 401 |
| Stores/persistencia | `BusinessData`, `AccountSession`, `AccountService`, stores de perfil/agenda/avisos/IA; identidad del servidor, queries por propietario |
| Archivos | Logo mediante `InputFile`; carga de factura en RAM. OCR/extracción no disponibles |
| Archivos públicos | Assets locales en `wwwroot`; App_Data/configuración/claves no publicados como assets |
| Producción | Proxy explícitamente confiable, Host permitido, HTTPS, volumen privado, Data Protection y `/health` público sin datos |
| Integraciones preparadas | Cliente Responses API deshabilitado por defecto. Ningún webhook WhatsApp ni endpoint OCR conectado |

No existen roles administrables por formulario, OAuth, JWT públicos, API de productos por URL ni recuperación por WhatsApp. No se inventaron esas superficies.

## Hallazgos críticos

Ninguno encontrado. No se detectaron contraseñas en texto plano, secretos activos versionados, ejecución de contenido ingresado ni acceso A/B no autorizado en los casos probados.

## Hallazgos altos

Ninguno confirmado. La comprobación de vulnerabilidades conocidas de dependencias permanece incompleta; no se transformó esa incertidumbre en un hallazgo alto ficticio.

## Hallazgos medios

### M-01 — Ausencia de cuotas de almacenamiento/operaciones por cuenta

**Estado: pendiente de decisión.** Un usuario con cuenta válida puede crear repetidamente productos, notas y comprobantes propios. Existen límites por dato/lote/archivo y ownership, pero no hay cuota acumulada por negocio ni throttling de escrituras de módulos/circuitos. El limitador de Razor Pages no limita eventos Blazor. Algunas lecturas y cálculos recorren todas las filas del negocio.

**Impacto:** crecimiento de disco, memoria y trabajo en el proceso/SQLite compartidos; puede perjudicar a otros usuarios sin consultar sus datos. No se realizó una prueba de saturación ni se afirma haber tumbado el servidor.

**Evidencia:** `BusinessData.SaveProductAsync`, `SaveNoteAsync`, `ApplyStockIntakeAsync`, lecturas completas de productos y `Program.cs` con rate limiting solo de cuentas. Los verificadores prueban escrituras legítimas y sus validaciones, no una política de cuotas inexistente.

**Recomendación:** antes de una beta abierta, definir límites de productos/notas, almacenamiento y frecuencia por usuario/negocio; límites temporales que permitan reintentar, métricas de consumo y alertas. Para una beta cerrada, limitar y supervisar el grupo de participantes y capacidad. No imponer límites comerciales arbitrarios mediante esta auditoría ni construir infraestructura distribuida.

## Hallazgos bajos

### B-01 — Presupuesto de login separable por barra final

**Corregido.** La partición usaba el `Request.Path` literal. `/login` y `/login/` ejecutan el mismo flujo pero tenían ventanas distintas de 12 POST/minuto/IP.

**Reproducción acotada:** doce intentos alternados entre ambas rutas y un intento adicional. Antes: intento 13 respondió 200. Después: 429 + `Retry-After`. Fueron cuentas ficticias y solamente trece intentos, sin fuerza bruta agresiva.

**Cambio:** normalizar la clave del limitador con `TrimEnd('/').ToLowerInvariant()`. Se mantienen 12 intentos, ventana de un minuto, cola cero y sin bloqueo permanente. No se cambiaron rutas ni la comparación de contraseñas.

No se afirma un bypass ilimitado: la variante comprobada duplicaba el presupuesto. La protección sigue siendo local al proceso y por IP/flujo; botnets e IP rotativas quedan fuera de esta defensa MVP.

### B-02 — Headers defensivos incompletos/no uniformes

**Corregido.** No existía CSP con `frame-ancestors` ni `nosniff`. Antiforgery puede emitir protección de frames en páginas de formularios, pero no había una política explícita uniforme para la aplicación.

**Cambio:** middleware temprano con callback `OnStarting` fija `Content-Security-Policy: frame-ancestors 'none'`, `X-Frame-Options: DENY` y `X-Content-Type-Options: nosniff`. Prueba en respuestas 200, antiforgery 400, limitador 429 y excepción controlada 500.

La CSP es deliberadamente solo de embedding; **no es una política de bloqueo de scripts/XSS**. No impide scripts inline/import maps necesarios ni añade permisos CORS. La navegación y Blazor se comprobaron en navegador local.

### B-03 — POST de login vacío provoca 500

**Corregido.** El binding de campos vacíos puede producir `null`. Se borraba ModelState para validar por flujo y luego `ValidEmail` llamaba `Trim()` o login accedía a `Password.Length` sin comprobación. Un POST válido con campos vacíos disparaba NullReferenceException. No otorgaba acceso ni exponía el stack en Production, pero podía provocar errores/logs innecesarios.

**Antes:** correo/contraseña vacíos → 500; correo válido/contraseña vacía → 500.

**Después:** ambos casos → 200 con errores normales de validación y sin cookie de autenticación.

**Cambio:** `ValidEmail`/`ValidPassword` rechazan null/empty y el login usa `string.IsNullOrEmpty`. No se cambió hashing, identidad, sesión ni reglas de longitud. Prueba HTTP y comprobación directa de validadores.

### B-04 — Exclusiones de Git incompletas para logs/SQLite alternativo

**Corregido preventivamente.** Ya estaban ignorados App_Data, `.env`, bases `.db`, journals, claves, builds y archivos de usuario. No estaban cubiertos `*.log` ni extensiones `.sqlite`/`.sqlite3` y sus journals.

Se agregaron esos patrones a `Aquarella/.gitignore` y se comprobó `git check-ignore`. **No había un log, base o secreto sensible versionado detectado**, no se borró ningún archivo ni se reescribió historial. Esta mejora no equivale a ignorar cualquier formato posible de backup: los backups deben quedar en directorios privados fuera del código/App_Data ignorado.

### B-05 — Claves de Data Protection persistidas sin cifrado explícito en disco

**Pendiente de decisión.** Production configura `PersistKeysToFileSystem` pero no un protector de claves en reposo. El proceso de prueba emite el warning de `XmlKeyManager[35]`. Las cookies/tokens sí están protegidos criptográficamente; el warning se refiere a los archivos de claves, no a cookies sin cifrar.

Ya existe separación del volumen y permisos 0700 del directorio de claves en Unix. Una lectura no autorizada del volumen/backup podría exponer material criptográfico. Requiere decidir permisos y política de backups y, si corresponde, un certificado/protector compatible con el hosting. No se añadió DPAPI solo para Windows porque cambiaría el funcionamiento Linux y la gestión de claves. No se publicaron ni mostraron claves.

## Informativos / límites de preparación

1. **Correo Production no conectado:** bloqueo operativo para nuevas cuentas y recuperación. Conservar fail-closed. Decidir proveedor y privacidad antes de implementarlo.
2. **Enumeración de registro:** `RegisterAsync` devuelve «Este correo ya pertenece a una cuenta». Se reproduce con el outbox Development. En Production actual el flujo está deshabilitado antes de esa respuesta. Antes de activar correo/registro público, decidir respuesta genérica y experiencia de recuperación; no hay un login que revele cuál credencial falló.
3. **Avisos de dependencias no actualizados:** faltan datos externos permitidos para cerrar esa comprobación. No hubo actualizaciones de paquetes.
4. **CSP de scripts:** recursos actuales locales, import map y un script inline de `pageshow`, además de módulos JS/Blazor. Una CSP más completa requiere inventario/nonce/hash y prueba específica; no imponerla a ciegas.
5. **Logs futuros de proveedores:** hoy cuentas/clave OpenAI no se registran en logs propios. El catch del importador puede registrar una excepción completa; un futuro OCR podría incluir texto sensible en su excepción. Redactar errores del adaptador antes de conectarlo.
6. **Privacidad del navegador compartido:** el caché actual de tema persiste solo tres colores; el objeto de Perfil en memoria es del usuario autenticado y necesario para la UI. Las copias legacy de localStorage se conservan deliberadamente para recuperación. Definir su limpieza/exportación privada tras migrar; no borrarlas automáticamente aquí.
7. **Contenedor/deployment no ejecutados:** se leyó Dockerfile/.dockerignore sin modificarlos. Dockerfile no fija explícitamente `USER`; verificar el UID efectivo de la imagen y usar usuario sin privilegios al cerrar la preparación del contenedor. No se descargó ni probó la imagen Linux.

## Autenticación y hashing

- Mecanismo: `Microsoft.AspNetCore.Identity.PasswordHasher<User>`, Identity V3, PBKDF2/HMAC-SHA512.
- Payload real comprobado: 100.000 iteraciones, salt aleatorio de 16 bytes, subkey de 32 bytes; la misma contraseña genera hashes distintos. Se derivó el subkey independientemente con PBKDF2 para comprobar almacenamiento, no texto plano.
- La verificación usa el mecanismo del framework; no una comparación propia de contraseñas/hash por `==`. Se conserva su comparación criptográfica y posibilidad de rehash.
- Contraseñas de 12–256 caracteres al registrar/resetear, comparación sensible a mayúsculas. Se probaron diferencias de contraseña y password reset.
- Formulario password oculto, `value=""`; hashes/contraseñas no aparecen en cuenta/HTML de respuesta autenticada ni cookies/logs capturados de la prueba.
- Login fallido de cuenta conocida/desconocida presenta el mismo mensaje genérico; se utiliza un hash dummy para la cuenta inexistente. No se realizó un estudio estadístico de timing. No hay MFA ni detección de contraseñas filtradas; no se agregaron sistemas nuevos.
- Registro valida en servidor y evita duplicados con índice/validación. Usuarios no verificados no se autentican.
- Reset/verificación utilizan Data Protection con propósito y expiración, registros con propietario y consumo único; reenviar invalida enlaces anteriores y reset revoca sesiones. Regresiones reproducidas por DatabaseVerification y AccountHttpVerification.

## Sesiones y cookies

- Cada login crea un nuevo GUID aleatorio de sesión en servidor; no acepta un ID de sesión elegido por formulario. Nuevos logins producen cookies distintas.
- Cookie `__Host-Aquarella.Auth`: HttpOnly, Secure Always, SameSite Lax, Path `/`, sin Domain; ticket protegido con Data Protection.
- Expiración absoluta de sesión: 8 horas o 14 días al recordar; sin sliding expiration. Cookie sin recordar no tiene persistencia de navegador.
- Validación exige usuario autenticado + UserId + SessionId coincidentes + sesión vigente en DB. El borrado del usuario elimina relaciones/sesiones según configuración existente.
- Logout elimina la sesión correspondiente a la pareja usuario/sesión y borra cookie. Copiar una cookie y reutilizarla después de logout fue rechazado. Cookie alterada y sesión expirada también rechazadas.
- Blazor revalida cada diez segundos y operaciones sensibles vuelven a comprobar sesión en servidor. No confiar en UI que todavía se vea en un circuito viejo. Regresión de sesiones revocadas y pareja falsificada pasó.
- Development mantiene HTTPS local/certificado existente. No se redujo Secure para permitir login HTTP.

## Autorización / IDOR / overposting

- Razor Page de cuenta autorizada; componentes con atributo Authorize y endpoints Razor Components con RequireAuthorization.
- Negociación anónima de Blazor: 401. Rutas principales anónimas, sesión expirada y cookie post-logout redirigen/rechazan de forma segura.
- Se probaron las rutas de Dashboard, Stock, Perfil, Precios, Agenda, Métricas, Avisos, Configuración IA/Página, Cuenta, Conversaciones y Chat; autenticadas respondieron correctamente.
- BusinessId proviene del usuario validado en servidor. Consultas/updates/deletes de productos y notas filtran por recurso + negocio; preferencias IA/tutorial por usuario.
- Regresiones A/B: IDs de producto/nota ajenos, colisiones de creación, ID de sesión de otro usuario, propietario de Perfil, importación legacy y deletes. Todas pasaron.
- Campos añadidos al POST de login (`UserId`, `EmailVerified`, `Role`) no alteraron identidad. PageModel solo vincula campos previstos; DTO de Perfil no contiene ownership/roles y los parches de Precios mantienen campos permitidos. No hay binding directo de entidades User/Business desde HTTP.

## CSRF

- Razor Pages usa antiforgery del framework en formularios POST; token/cookie validados server-side. Sin token o token falsificado → 400. Logout sin token no revoca sesión.
- Logout del header Blazor contiene AntiforgeryToken y envía POST a la página de cuenta; no hay logout destructivo por GET.
- Operaciones interactivas de módulos viajan por el circuito SignalR autorizado, no por endpoints REST públicos. Cookies Lax, autenticación del hub, integridad de descriptores de componentes del framework y validación de sesión/datos en cada store son parte de la protección. `UseAntiforgery` no se presenta como token universal de cada evento SignalR.
- No hay CORS habilitado, origins públicos adicionales ni webhooks de escritura. No se implementaron tokens manuales paralelos.
- La política de same-site debe revisarse si se hospedan otros servicios no confiables bajo el mismo dominio registrable. La auditoría no simula un navegador comprometido ni robado.

## XSS / HTML injection / JavaScript

- Búsqueda en código propio: no se detectaron usos de Html.Raw/MarkupString, innerHTML/outerHTML/insertAdjacentHTML, eval/new Function para renderizar datos de usuario.
- Razor/Blazor renderizan nombres, productos, notas, descripciones, mensajes y preferencias como texto/valores de campos; no como HTML confiable.
- Payloads de prueba: etiqueta img con onerror que solo intenta poner un marcador DOM, contenido script con ese mismo marcador y un link ficticio. Se persistieron exclusivamente en DB temporal.
- Cuenta Razor: HTML codificado y sin password/hash. Navegador real: nombre/header, Perfil/descripcion, Stock, Precios y título/contenido de Agenda mostraron el texto literal. Marcador DOM inexistente, cero nodos onerror y cero scripts inyectados. La descripción no creó links engañosos.
- Chat simulado: envío de payload mostró el texto literal y respuesta simulada; marcador inexistente. OpenAI se mantuvo explícitamente deshabilitado y se quitaron API keys del entorno de esos procesos.
- Colores pasan regex HEX; JS calcula estilos a partir de HEX válidos. Rutas de módulos provienen del catálogo fijo. URLs de Perfil no se convierten actualmente en enlaces HTML públicos; revisar esquemas permitidos antes de hacerlo.
- Se conservaron capturas de evidencia de Agenda y Chat fuera del repositorio, en el directorio de visualizaciones de la tarea.

## SQL injection y validación server-side

- Persistencia de producción utiliza LINQ/EF Core y operaciones ExecuteUpdate/Delete parametrizadas; no se detectó SQL raw construido con input de usuario.
- La consulta de versión SQLite del nuevo verificador es SQL constante sobre la DB temporal; herramientas anteriores también tienen SQL de fixtures. No son endpoints de producción.
- Correo con aspecto de SQL injection no pudo autenticar. No se atacó la DB real.
- `BusinessData` valida nombres/cantidades, rangos de precios, hora/notas, lotes y operación de carga; `BusinessProfileStore.IsValid` valida Perfil; preferencias IA validan rangos/longitud. No se depende únicamente de maxlength/InputNumber del navegador.
- Verificadores anteriores probaron datos inválidos con llamadas directas a stores, conservación/rollback y usuarios anónimos/revocados. Se mantuvieron las reglas y todos pasaron.
- Campos opcionales de contacto sin límite explícito de longitud y falta de cuota acumulada deben formar parte de M-01; no se eligieron límites comerciales nuevos aquí.

## Uploads / archivos / path traversal

- Facturas: stream limitado a 10 MB, archivo no vacío, timeout de 20 s, cancelación y disposición. Detección por contenido de PNG/JPEG/WebP/PDF, no solo nombre/MIME.
- HTML renombrado como imagen y contenido vacío rechazados. Un prefijo PDF truncado pasa el detector de firma: **no se confunde la firma con validación completa de un PDF**. Actualmente el extractor retorna indisponibilidad y no ejecuta/parses documentos.
- El nombre recibido se utiliza como texto UI; no compone un destino de escritura. No existe almacenamiento de uploads con nombre de usuario, descompresión ZIP ni ejecución de comandos de esos archivos. No se detectó un sumidero explotable de path traversal.
- Logo: límite de entrada 2 MB, conversión a PNG y tamaño 400×400 en navegador, límite de stream del servidor; persistencia restringida a data URL PNG y longitud máxima. No se permite SVG/HTML. El servidor no implementa una validación binaria profunda del PNG; no afirmar que confía en un decodificador seguro de OCR inexistente.
- App_Data, outbox, base y configuraciones no responden como archivos estáticos en las solicitudes comprobadas. El volumen/claves de producción queda separado del publish.
- Antes de aceptar/interpretar binarios con parsers del servidor se requieren los controles de IA/OCR de abajo. No se descargaron payloads ni ejecutables.

## Secretos y Git

- Búsqueda en archivos versionados por nombres sensibles y patrones de claves/tokens/privadas: cero candidatos. Búsqueda razonable de esos patrones en cambios históricos: cero commits candidatos. No se afirma una inspección byte a byte de todo binario/historial remoto.
- Configuración versionada contiene SQLite local, usuario Development, modelos deshabilitados y UserSecretsId; no claves ni contraseñas activas. Contraseñas en verificadores son fixtures desechables, no cuentas reales.
- Credencial de OpenAI viene de configuración/User Secrets/entorno; no se devuelve al cliente. No se leyó ni mostró un secreto completo.
- Exclusiones actuales y agregadas comprobadas con git check-ignore. No se hizo stage, commit, push, fetch/pull, merge, rebase ni cambio de rama. No se eliminó evidencia.

## Logging y errores

- Logs propios de cuentas registran flujo/tipo de excepción, no campos/password/cookies. IA no registra key, body ni error remoto.
- Logs de los procesos ficticios no contenían contraseña fixture, hash ni cookie emitida. Archivos de logs permanecen privados/temporales, nunca publicados.
- Production: excepción controlada por hash ficticio inválido devolvió 500 genérico, sin stack trace, SQL ni paths. La causa permanece diagnosticable en servidor. Development conserva debugging.
- **Eventos esperados de la prueba:** warning de claves sin cifrado en disco (B-05) y un error de excepción deliberadamente provocado para comprobar respuesta Production. No confundirlos con un recorrido limpio de UI ni afirmar cero warnings runtime.
- Navegador: recorridos de módulos y XSS controlado sin warnings/errores de consola observados. No se activó logging de datos sensibles de EF ni logging de bodies.
- Al configurar proxy/proveedor, evitar registrar tokens reset/verify de query strings, Authorization/Cookie, documentos, prompts y datos personales. El proxy externo no fue auditado.

## Dependencias

Inventario offline mediante `dotnet list Aquarella/Aquarella.csproj package --include-transitive --no-restore`:

- Directos: EF Core Design/SQLite 10.0.12; assets ASP.NET Core 10.0.12.
- Runtime efectivo: .NET/ASP.NET Core 10.0.12.
- Transitivos relevantes: Microsoft.Data.Sqlite/EF 10.0.12, SQLitePCLRaw 2.1.12, Newtonsoft.Json 13.0.4, Roslyn 5.0.0 y dependencias de herramientas de diseño. Versiones completas disponibles por el comando anterior.
- SQLite nativo observado en la conexión .NET temporal: **3.53.3**. No se dedujo su versión desde Python ni desde el número del wrapper.
- Sin caché local de advisories utilizable. `--vulnerable` con fuentes remotas no se ejecutó, respetando el alcance sin contacto externo. No se usó un feed vacío para afirmar «sin vulnerabilidades».
- Antes de beta, con autorización para consultar solo metadata oficial, ejecutar `dotnet list ... package --vulnerable --include-transitive --no-restore` y comprobar advisories del runtime/imagen. Clasificar paquete, CVE, severidad, uso real y actualización compatible. No se actualizaron versiones automáticamente.

## Configuración Production / Development-only / headers / cache

- Production falla ante origen no HTTPS/no válido, AllowedHosts wildcard, volumen/DB/claves no apropiados o proxy no confiable/configuración global permisiva de forwarded headers.
- Reenvío de IP/proto se acepta solo de proxy conocido, un salto; prueba de proxy confiable/no confiable pasó. Configuración real del proveedor deberá verificarse antes de despliegue.
- HTTP público de la aplicación rechazado en Production; health HTTP permite diagnóstico sin datos. HSTS Production comprobado con Host ficticio local: localhost tiene exclusión estándar de HSTS, no se alteró eso.
- Secure/HttpOnly/SameSite de cookies y TempData existentes se conservaron. Referrer-Policy no-referrer y no-store existentes se conservaron. El programa ya marcaba respuestas con no-cache/no-store antes de esta tarea; no se amplió la política de caché por este audit.
- CSP frame-ancestors none/X-Frame-Options DENY impiden embedding. Nosniff evita interpretación MIME oportunista. No hay CORS y no se añadió.
- Development-only: botón ausente en Production, handler con token CSRF válido retorna 404 y no genera auth cookie; no se crean usuarios demo automáticamente. Regresión Production y Development pasada.
- No se utiliza un returnUrl arbitrario del cliente: intento con destino externo terminó en `/`, sin visitar el destino.
- Publish excluye App_Data, outbox, datos/claves/.env y configuración Development. No se publicaron logs, datos, credenciales ni base real.
- Se dejó pausado Docker/Railway. TLS público, UID/permisos reales, proxy y headers en infraestructura externa no se comprobaron.

## Concurrencia y abuso

Las soluciones anteriores de Precios/Perfil se mantuvieron. Regresiones comprobaron escrituras cercanas/concurrentes, valores finales, fallos/reintento y ownership. La confirmación de cargas Stock tiene comprobante persistente e idempotencia por negocio/operación; replay no suma de nuevo. Logout/reset tienen revocación/consumo único.

Un usuario autorizado puede repetir ajustes legítimos de su stock; no se trata cada evento intencional como un replay prohibido. El abuso por volumen corresponde a M-01. No se añadieron guardas arbitrarias que oculten problemas de persistencia.

## Requisitos antes de habilitar IA/OCR

1. Autorización/ownership para cada documento y recurso; no aceptar BusinessId suministrado como autoridad.
2. Límite de bytes, píxeles/páginas, tiempo, concurrencia y cuota por negocio. Validar estructura real de formatos; tratar archivos truncados, polyglots, bombas de descompresión y PDFs con contenido activo. No ejecutar JavaScript, acciones, adjuntos ni binarios extraídos de documentos.
3. Parser actualizado y aislamiento de procesamiento; revisión/malware scan acorde al almacenamiento/procesamiento elegido. Firma/MIME/extensión son filtros, no garantías.
4. Nombres/rutas generados por servidor, almacenamiento privado fuera de wwwroot, permisos mínimos, limpieza/TTL, sin confiar en nombres recibidos. Descarga autorizada con headers/MIME seguros si se implementa.
5. Documento/prompts/respuesta IA como contenido no confiable. Las instrucciones del documento nunca conceden permisos, acceso a herramientas o autoridad para modificar Stock. Validar respuesta con esquema, unidades, rangos y confirmación humana; conservar transacciones/idempotencia existentes.
6. API key solo en servidor/secretos, scopes mínimos, sin logs/cliente/repositorio. Configuración deshabilitada por defecto, timeout/cancelación y errores redactados.
7. Presupuesto/rate limits/tokens y concurrencia por usuario/negocio/global; rechazo controlado y alertas de gasto.
8. Definir consentimiento, datos que salen del servidor, retención, proveedor/región, eliminación y privacidad de facturas/datos personales. No asumir que `store=false` resuelve todas las obligaciones de privacidad.

No se conectó ni llamó IA/OCR durante la auditoría.

## Requisitos antes de conectar WhatsApp

- Verificar challenge del webhook y firma del cuerpo crudo conforme al proveedor; comparar firma de manera segura y rechazar antes de procesar.
- Secretos en servidor, rotación, HTTPS, scopes mínimos y ownership del número/cuenta de negocio verificado; no asociar un negocio por IDs sin comprobar.
- Idempotencia por identificador de evento/mensaje, protección frente a replay y eventos fuera de orden; persistencia/consumo transaccional.
- Rate limits/límites de body/adjuntos, colas acotadas y timeouts; validación de origen/formato. No confiar en texto/URLs/adjuntos recibidos.
- Autorización de acciones y envío, consentimiento/privacidad de contactos, retención y borrado; logs redactados sin tokens ni conversaciones completas.
- Separar el canal de entrada de permisos/herramientas de IA; no permitir que un mensaje conceda autonomía.

No existe ni se conectó un webhook WhatsApp en esta tarea.

## Correcciones realizadas

1. Partición canónica del rate limiter de formularios de cuenta.
2. Headers de embedding/MIME uniformes, incluidos rechazos/errores.
3. Validación de credenciales null/empty sin excepciones.
4. Exclusiones adicionales de Git para logs y formatos SQLite.
5. Nuevo verificador reproducible de seguridad, sin paquetes nuevos.

No se modificaron diseño, módulos, esquema/migraciones, autenticación/sesiones como sistema, IA, Docker ni Railway.

## Correcciones NO realizadas y motivo

- Cuotas/throttling de módulos: requieren política funcional y capacidad objetivo, no límites inventados.
- Cifrado de claves en reposo: requiere estrategia compatible con Linux/hosting/recuperación de claves.
- Correo/registro público: requiere proveedor e implementación autorizada, fuera de esta auditoría local.
- Mensaje de registro duplicado: decidir UX antes de activar el flujo público; hoy solo disponible en Development.
- CSP completa, MFA, sesiones distribuidas, scanner de archivos, WAF/offline: no hay una vulnerabilidad demostrada que justifique un cambio estructural ni infraestructura nueva en esta tarea.
- Updates/advisories externos/deployment: prohibición de contacto externo y alcance sin despliegue.

## Pruebas ejecutadas

| Verificador | Resultado |
|---|---|
| SecurityVerification (nuevo) | PASS: hashing/salts; rutas/hub; validación vacía; CSRF; sesión/cookies/logout/replay/expiración; SQL-looking login; overposting; redirect; XSS Razor; archivos privados; headers 200/400/429/500; Production; logs |
| DatabaseVerification | PASS: perfil/reintento, cuentas/hash/verify/reset/expiry/single-use, sesiones, migraciones, negocio |
| ReliabilityVerification | PASS: concurrencia Precios, formato es-AR, datos malformados, navegación/desconexión |
| PersistenceVerification | PASS: ownership Perfil, pareja sesión, IDs A/B falsificados, concurrencia Perfil, integridad/rollback, lectura nueva/backup/reinicio |
| StockIntakeVerification | PASS: validación, autorización, IDs ajenos, idempotencia persistente/concurrente, rollback y extractor indisponible |
| AiSettingsVerification | PASS: 20 comprobaciones |
| NoticesVerification | PASS: 17 comprobaciones |
| MetricsVerification | PASS: 20 comprobaciones |
| MetricsPresentationVerification | PASS: 64 comprobaciones |
| ProductionHostingVerification | PASS: proxy/Host/HTTPS, cookies, sesión, CSRF, acceso Development rechazado, correo indisponible y reinicio |
| AccountHttpVerification | PASS: registro, validación, duplicados, verificación/reset/reenvío, login/case, cookies/logout/CSRF/rate limit; servidor/outbox/DB temporales |
| DevelopmentAccessVerification | PASS: cookie real, rutas, logout/reentrada, sin duplicar/modificar datos; DB temporal |
| JavaScript `legacy-recovery.test.mjs` | PASS |
| JavaScript `metrics-motion.test.mjs` | PASS |
| Navegador local de fixture | PASS: Dashboard, Perfil, Stock, Precios, Agenda/popover, Métricas, Avisos, IA, Conversaciones y chat; payloads como texto, sin ejecución; consola sin warn/error observados |

Regresiones se repitieron después de las correcciones de código. Todas usaron fixtures; no se modificaron tests para ocultar un fallo.

### Ejecutar el nuevo verificador

Desde `Aquarella`, con paquetes ya disponibles localmente:

```powershell
dotnet publish Aquarella/Aquarella.csproj --no-restore --artifacts-path "$env:TEMP\aquarella-security-build" -c Release -o "$env:TEMP\aquarella-security-publish"
dotnet run --project tools/SecurityVerification --no-restore --artifacts-path "$env:TEMP\aquarella-security-build" -- "$env:TEMP\aquarella-security-publish"
```

En una carpeta de artifacts nueva, primero restaurar el verificador usando exclusivamente la caché local (`--source "$env:USERPROFILE\.nuget\packages" -p:NuGetAudit=false`), o restaurar con fuentes externas solamente tras autorización. Deshabilitar NuGetAudit en ese comando offline **no significa que se hayan revisado CVE**; es la restricción de no acceder a un feed.

El verificador exige un publish sin App_Data, genera su DB/cuentas/claves y proceso nuevos en TEMP, y lo detiene en finally. `--probe` permite observar el comportamiento del publish previo sin exigir los headers/umbrales corregidos. No pasar un publish con datos reales. Logs no se imprimen completos.

Para repetir XSS en navegador: usar una copia de la fixture generada, habilitar el usuario `security-fixture` solo en Development y abrir sus módulos/nota del 6 de octubre de 2026. Verificar texto literal, inexistencia del marcador `data-audit-xss`, nodos onerror y scripts del payload. No utilizar un usuario o base reales.

## Resultado build

`dotnet build Aquarella.slnx --no-restore --artifacts-path "$env:TEMP\aquarella-data-audit-build"`: **0 errores, 0 warnings**.

Publish Release y compilación del nuevo verificador correctos. Artifacts externos se utilizaron porque hay instancias del usuario ejecutando archivos Debug; no se detuvieron para compilar. No se añadieron dependencias ni se consultaron feeds remotos.

## Archivos modificados / nuevos

Modificados:

- `Aquarella/.gitignore` (ruta desde raíz Git).
- `Aquarella/Aquarella/Program.cs`.
- `Aquarella/Aquarella/Services/Accounts/AccountService.cs`.
- `Aquarella/Aquarella/Pages/Access.cshtml.cs`.

Nuevos:

- `Aquarella/tools/SecurityVerification/SecurityVerification.csproj`.
- `Aquarella/tools/SecurityVerification/Program.cs`.
- `Aquarella/AUDITORIA-SEGURIDAD-2026-10-06.md`.

## Estado Git

Rama `main`, sin cambio de HEAD `93d7d74`. Cuatro archivos existentes modificados y tres nuevos de esta tarea, además de las siete capturas PNG previas sin seguimiento. No eliminaciones, stage, commit ni push. `git diff --check` sin errores; avisos de normalización LF/CRLF de Git no son warnings de compilación. Se conservaron informes, verificadores y soluciones anteriores.

## Checklist manual antes de beta

- [ ] Decidir incorporación de cuentas/verificación/recuperación en Production; no habilitar bypass ni utilizar fixtures reales.
- [ ] Consultar advisories actuales de paquetes, runtime e imagen con autorización específica para metadata externa; resolver vulnerabilidades aplicables.
- [ ] Definir/controlar cuotas y volumen de uso (M-01), participantes/capacidad de la beta.
- [ ] Verificar TLS, AllowedHosts y confianza real del proxy; no aceptar una red arbitraria ni confiar en IP suministrada por el cliente.
- [ ] Verificar acceso/permisos/backup de DB y claves; decidir protección en reposo (B-05) y UID efectivo del contenedor.
- [ ] Con cuentas ficticias de staging, comprobar ruta directa sin sesión, logout en otra pestaña y acciones desde circuito viejo, expiry y restablecimiento que revoca sesiones.
- [ ] Comprobar reconexión SignalR real y comportamiento del navegador al volver con historial/BFCache tras logout; las pruebas automáticas no simulan toda desconexión de red.
- [ ] Confirmar en navegador los headers y el rechazo de iframe desde otro origen local controlado; el test automatizado verifica headers, no un ataque de iframe de un sitio público.
- [ ] Confirmar ausencia de errores técnicos/tokens en logs del proxy/proveedor y cachés externos; actualmente solo se auditó el proceso local.
- [ ] Aplicar los requisitos de archivos/IA/OCR/WhatsApp antes de activar esas capacidades.

