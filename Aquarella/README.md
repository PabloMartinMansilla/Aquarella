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

La portada es el dashboard de módulos. Las seis páginas iniciales son `/stock`,
`/avisos`, `/metricas`, `/conversaciones`, `/configuracion-ia` y `/agenda`.
El buscador compartido encuentra módulos por nombre; no busca datos del negocio todavía.
Las dos tarjetas vacías están reservadas y no tienen acciones.
El chat existente se conserva en `/conversaciones/chat`, accesible desde el menú.
La integración de OpenAI y su configuración de seguridad se mantienen sin cambios.

## Identidad del negocio

BusinessProfileStore centraliza todos los datos y publica cambios al guardar.
IBusinessProfilePersistence separa el almacenamiento de los componentes.
BrowserBusinessProfilePersistence guarda provisionalmente en localStorage,
con la clave aquarella.business-profile.v1, por navegador y origen. No es una cuenta
ni sincroniza dispositivos. Borrar los datos del navegador elimina este perfil.
Un backend puede implementar la misma interfaz y reemplazar su registro en Program.cs.
business-identity.js restaura la paleta al cargar y genera tokens CSS con contraste.
Los colores originales se conservan como primary, secondary y tertiary; para texto
se derivan alternativas legibles cuando una combinación no tiene contraste suficiente.
