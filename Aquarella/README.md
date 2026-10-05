# Aquarella

Prototipo Blazor Web (.NET 10). Los mensajes se mantienen en memoria durante la sesión.

## OpenAI Responses API

La configuración inicial usa respuestas simuladas y no hace llamadas externas.
El servicio solo llama a OpenAI si `OpenAI:Enabled` es `true`, hay una clave y se configuró un modelo.
La clave permanece en el servidor; no se envía al navegador ni se incluye en archivos del proyecto.

Para configurar una clave más adelante, usar la variable de entorno `OpenAI__ApiKey`
o `OPENAI_API_KEY` en el proceso del servidor. `OpenAI:ApiKey` tiene prioridad.
También se admite .NET User Secrets en desarrollo con la clave de configuración `OpenAI:ApiKey`.
No guardar claves en appsettings.json, launchSettings.json ni en el repositorio.

Para activar llamadas reales en el futuro, configurar además `OpenAI__Enabled=true`
y `OpenAI__Model` con un modelo disponible para la cuenta, y reiniciar la aplicación.
Mantener `OpenAI__Enabled=false` para seguir probando sin llamadas aun si existe una clave.

Cada solicitud usa el mensaje actual, sin historial, streaming ni persistencia.
Se envía `store=false` y se leen los bloques `output_text` de `output`.
Los errores de red/API se muestran con un mensaje genérico, sin detalles sensibles.

Compilar desde esta carpeta: `dotnet build Aquarella.slnx`.

Referencia: https://developers.openai.com/api/docs/guides/text

## Navegación

Después del login, la portada es el dashboard de ocho módulos: `/perfil`, `/precios`, `/stock`,
`/avisos`, `/metricas`, `/conversaciones`, `/configuracion-ia` y `/agenda`.
El buscador compartido encuentra módulos por nombre; no busca datos del negocio todavía.
Los módulos pendientes muestran Próximamente.
El chat existente se conserva en `/conversaciones/chat`, accesible desde el menú.
La integración de OpenAI y su configuración de seguridad se mantienen sin cambios.

## Identidad del negocio

BusinessProfileStore centraliza todos los datos y publica cambios al guardar.
IBusinessProfilePersistence separa el almacenamiento de los componentes.
DatabaseBusinessProfilePersistence lee y escribe SQLite mediante BusinessData.
La copia anterior en localStorage se conserva únicamente para importación/recuperación.
business-identity.js restaura la paleta al cargar y genera tokens CSS con contraste.
Los colores originales se conservan como primary, secondary y tertiary; para texto
se derivan alternativas legibles cuando una combinación no tiene contraste suficiente.

## Base de datos local (EF Core 10 / SQLite)

La conexión `ConnectionStrings:Aquarella` usa por defecto `Data Source=App_Data/aquarella.db`,
resuelta respecto al directorio del proyecto web. Se puede sobrescribir con configuración
.NET, user-secrets o `ConnectionStrings__Aquarella`; no incluir credenciales en Git.
`App_Data/`, `*.db`, `*.db-wal`, `*.db-shm` y `*.db-journal` están ignorados.

`AquarellaDbContext` define User (sin contraseña), Business (uno por usuario), Product
(stock y valores de precios) y CalendarEntry (muchas notas por negocio y fecha).
Business reutiliza BusinessProfile como objeto owned con columnas en la misma tabla.
El logo permanece como PNG base64 en SQLite para esta etapa local, con el límite actual.
Dinero se representa con decimal; SQLite almacena esos valores como TEXT exacto.
Precio sugerido y ganancia real se calculan en ProductPrice, no se duplican en tablas.
Los timestamps CreatedAt/UpdatedAt son UTC.

BusinessData resuelve el negocio desde el UserId autenticado y filtra todas las operaciones
por BusinessId. Usa IDbContextFactory: un contexto por operación, nunca un contexto de larga
vida en el circuito. DatabaseChanges sincroniza Stock/Precios y notas entre circuitos del
mismo negocio en este proceso. Cada operación valida la sesión del servidor.

Desde la carpeta Aquarella (la que contiene esta documentación):

```powershell
dotnet tool restore
dotnet ef database update --project Aquarella
dotnet run --project Aquarella
```

Las migraciones InitialBusinessData y RealAccounts se aplican al iniciar la app. La cuenta antigua conserva sus datos, pero ya no acepta pablo / pablo.

En el primer acceso del negocio, BusinessData importa en una transacción los datos del
localStorage del navegador y origen actuales. Conserva IDs y precios. `LegacyImported`
impide repetir la importación o sobrescribir datos de SQLite con copias antiguas. Si los
datos locales son inválidos, no se marca importado ni se descartan: corregirlos antes de
reintentar. Usar el mismo navegador y URL/origen del prototipo para recuperar sus datos.
Los módulos legacy JS se conservan únicamente para leer esa copia durante la importación;
ya no se usan para guardar. El antiguo adaptador BrowserBusinessProfilePersistence se retiró.

Perfil, Stock, Precios, Agenda y el estado del tutorial leen/escriben SQLite. En el navegador quedan la cookie segura de autenticación, la copia legacy intacta y la caché visual de la paleta. La caché no sustituye a SQLite.

No eliminar la base para actualizarla. Las migraciones conservan los datos. Antes de cualquier operación de recreación, hacer una copia consistente con la app detenida. No ejecutar EnsureCreated: las tablas siempre provienen de migraciones.

Verificación reproducible, con SQLite temporal independiente:

```powershell
dotnet run --project tools/DatabaseVerification
```

Prueba migraciones desde cero, importación/idempotencia, perfil/logo, stock, precisión
decimal, precios, notas opcionales, eliminación y aislamiento entre negocios.

Para cambiar proveedor: sustituir UseSqlite por el proveedor elegido y generar un conjunto
de migraciones para ese proveedor; los servicios y DTOs no consultan SQL específico.



## Cuentas reales

URL local: https://localhost:7188. Usar el perfil https: las cookies tienen Secure siempre.
Las pantallas de acceso son Razor Pages con antiforgery; los módulos siguen siendo Blazor
Interactive Server. PasswordHasher<User> de ASP.NET Core Identity almacena hashes salados
PBKDF2 (nunca contraseñas). Requisito: 12–256 caracteres, sin transformar la contraseña.

Cookies HttpOnly/Secure/SameSite=Lax, protegidas con ASP.NET Core Data Protection.
Mantener sesión: cookie persistente y sesión de 14 días. Sin marcar: cookie de sesión del
navegador y límite de 8 horas. No hay renovación automática. Las sesiones se registran
en SQLite; logout revoca la sesión actual y restablecer contraseña revoca todas.
Cada operación de negocio valida la sesión; Blazor también revalida cada 10 segundos.
Las rutas de módulos y los endpoints Blazor exigen autenticación en el servidor.

### Conservar el negocio de pablo

No se borró ni se reasignó el usuario/negocio antiguo. Ya no existe el acceso pablo / pablo.
En Development, si esa cuenta aún no tiene email, el inicio genera un enlace privado,
de un solo uso y válido 24 horas, en App_Data/development-account-link.txt.
Abrir ese archivo y anteponer https://localhost:7188 a la ruta. Desde ese formulario,
elegir nombre, apellido, email y contraseña propios. La cuenta conserva exactamente sus
IDs, Perfil, productos/precios y notas. Verificar el email y luego iniciar sesión.
El enlace se renueva al reiniciar mientras siga sin vincularse. No existe en Production.
Un registro normal crea otro negocio vacío y no importa los datos legacy del navegador.

La migración RealAccounts agrega email/normalización/índice único, nombres, hash,
EmailVerified, HasCompletedOnboarding y tablas de tokens/sesiones, conservando la inicial.
El tutorial se guarda por User.Id en SQLite; repetirlo manualmente sigue disponible.
En la cuenta antigua se importa, si existe, su preferencia legacy completed/skipped una vez.

### Emails y tokens

IAccountEmail permite conectar un proveedor después. Por ahora DevelopmentAccountEmail
escribe cada enlace en App_Data/development-emails/*.json (carpeta privada e ignorada).
Abrir el archivo más reciente correspondiente al email y copiar Link al navegador.
Nunca se envía correo real. En Production no se escriben ni muestran enlaces; hace falta
configurar un proveedor de IAccountEmail y Accounts:PublicOrigin con la URL HTTPS pública antes de habilitar
el envío real. Los mensajes de recuperación y reenvío son genéricos.

Los tokens usan ITimeLimitedDataProtector del framework, con vencimiento de una hora,
propósito y usuario específicos, y registro de uso en SQLite. Reenvío invalida el anterior;
consumo es transaccional y de un solo uso. No se sirven archivos de App_Data por HTTP.
Conservar y proteger el key ring de Data Protection en un despliegue, compartiéndolo entre
instancias si corresponde. La configuración local usa el almacenamiento estándar del SDK.

Rate limiting por IP y pantalla: hasta 12 envíos por minuto, sin bloquear cuentas
permanentemente. HTTP 429 incluye Retry-After. Todas las mutaciones de cuenta usan POST
con antiforgery; las páginas sensibles llevan no-store y no-referrer.

### Pruebas

Desde la raíz del repositorio:

dotnet run --project Aquarella/tools/DatabaseVerification

Prueba migraciones, negocio y aislamiento, registro, hashes, unicidad, contraseña
case-sensitive, tokens inválidos/vencidos/usados/reenvío, revocación, tutorial por usuario
y vinculación legacy. Compara también los datos reales con la copia previa a la migración
si App_Data/aquarella.before-accounts.db está presente.

Para pruebas HTTP usar SOLO una base aislada, nunca aquarella.db:

$env:ConnectionStrings__Aquarella = 'Data Source=App_Data/accounts-verification.db'
dotnet run --project Aquarella/Aquarella --launch-profile https

En otra terminal desde la raíz:
dotnet run --project Aquarella/tools/AccountHttpVerification

Genera cuentas ficticias en la base de pruebas y verifica validaciones, cookies persistentes
y temporales, rutas, logout, recuperación, antiforgery y rate limiting. No ejecutar contra
la base real. Al terminar, detener el servidor y quitar la variable de conexión para volver
a aquarella.db. Los archivos de pruebas/outbox/backup quedan dentro de App_Data ignorado.

## Entrar sin cuenta (solo Development)

El Login muestra una acción secundaria Entrar sin cuenta. Usa un POST antiforgery a
/login?handler=Development, protegido además por el rate limiter existente.
DevelopmentAccess consulta DevelopmentAccess:Username de appsettings.Development.json
(pablo) y requiere un usuario existente con negocio. No crea ni modifica usuarios,
negocios, credenciales, verificación ni el tutorial. Genera la misma cookie real del Login,
con sesión no persistente; logout funciona normalmente. El servidor y el servicio rechazan
este mecanismo fuera de Development aunque se inyecte esa configuración manualmente.
No requiere una migración. Si falta el usuario/negocio, muestra un error sin recrear datos.

Pruebas con una copia aislada de aquarella.db en App_Data/development-access-verification.db:
$env:ConnectionStrings__Aquarella = 'Data Source=App_Data/development-access-verification.db'
dotnet run --project Aquarella/Aquarella --launch-profile https
En otra terminal: dotnet run --project Aquarella/tools/DevelopmentAccessVerification
Para la prueba Production usar esa copia y --no-launch-profile --environment Production
--urls https://localhost:7190; ejecutar el verificador con --production. El puerto 7190
es exclusivo de pruebas: launchSettings conserva sus puertos normales 7188 y 5097.
