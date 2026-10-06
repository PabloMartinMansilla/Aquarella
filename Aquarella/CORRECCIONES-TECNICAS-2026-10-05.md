# Aquarella — cierre de riesgos técnicos, 5 de octubre de 2026

Continuación de `AUDITORIA-TECNICA-2026-10-05.md`. El informe anterior permanece intacto.
Trabajo realizado en `main`, partiendo del commit `0f34f2d`, sin commit, push, cambios de rama ni operaciones de integración de Git.

## Estado inicial conservado

- Corrección pendiente de `BusinessProfileStore.InitializeAsync`: reintentar una carga fallida o cancelada.
- Su prueba pendiente en `tools/DatabaseVerification/Program.cs`, conservada sin cambios adicionales.
- Informe anterior y siete imágenes de previsualización no versionadas, conservados.
- No se modificaron esquema/migraciones, autenticación, paquetes, deployment, Docker/Railway, estilos, layout ni tiempos de motion.

Las soluciones y verificaciones se realizaron en el orden pedido: Precios, datos malformados, navegación/desconexión y verificación conjunta.

## A. Precios

### Problema y causa

El guardado automático enviaba el `ProductPrice` completo y mutable. El semáforo de la vista evitaba escrituras simultáneas dentro de esa vista, pero no evitaba que dos vistas con datos antiguos reemplazaran campos ajenos. Por ejemplo, cambiar costo en una pestaña y porcentaje en otra podía perder el primer cambio. Tampoco distinguía una respuesta antigua de un borrador más reciente.

No existe un botón de submit en esta pantalla: el flujo real utiliza `oninput` y el botón «Usar sugerido». Por ello se verificaron eventos repetidos y ediciones cercanas, en vez de inventar un caso de Enter + submit.

### Solución

- Cada evento captura un `PriceEdit` inmutable: producto, campo y valor.
- `BusinessData.ApplyPriceEditAsync` valida sesión, negocio, campo y rango; actualiza exclusivamente el campo correspondiente mediante un UPDATE atómico y devuelve el registro persistido.
- Una edición idéntica no produce escritura efectiva ni notificación adicional.
- El semáforo mantiene el orden de las ediciones aceptadas por una vista. La revisión por producto impide que una respuesta anterior reemplace el último borrador.
- La vista conserva borradores pendientes durante las recargas y reutiliza las notificaciones existentes para sincronizar cambios de otras vistas del mismo negocio.
- Se utiliza el estado visual existente para indicar guardado/sincronización. Un error restaura el valor persistido; una falla de recarga se muestra como falla de carga y permite reintentar recargando.

`SavePriceAsync(ProductPrice)` sigue disponible para las fixtures que necesitan reemplazar explícitamente todos los campos. La UI de producción usa exclusivamente la nueva operación por campo. Si dos pestañas editan **el mismo campo**, prevalece el último UPDATE efectivo; no se añadió un esquema de versiones ni una migración. Las notificaciones siguen siendo las existentes dentro de una instancia del servidor.

### Pruebas y resultado

`ReliabilityVerification --baseline` reprodujo el fallo anterior: dos vistas antiguas no conservaban ambas ediciones. El verificador normal pasó con:

- llamadas concurrentes sobre campos distintos;
- eventos duplicados sin notificaciones duplicadas;
- cambios cercanos y respuesta anterior demorada;
- handlers reales del componente con un UPDATE demorado;
- error SQL inyectado, restauración visible y guardado posterior exitoso;
- venta manual/sugerida y rechazo de productos ajenos o inexistentes.

También se probaron dos pestañas reales: costo 200 en una y porcentaje 75 en otra. Ambas mostraron costo 200, porcentaje 75, sugerido 350 y ganancia 150; los valores permanecieron después de recargar y reiniciar el servidor temporal.

## B. Datos malformados

### Escenarios y causa

- Un Perfil persistido que no pasaba validación se descartaba durante la lectura y se sustituía visualmente por defaults, ocultando campos válidos recuperables.
- Las colecciones/filas nulas del importador legado podían llegar a recorridos o validadores que desreferenciaban valores nulos.
- El lector legado de identidad trataba JSON roto como ausencia de datos. Los lectores de Stock/Precios asumían filas no nulas.
- Las notas de versiones anteriores podían omitir campos opcionales.

### Recuperación

- Perfil conserva todos los valores originales en memoria, incluidos los que necesitan corrección. Muestra una advertencia mediante el estado existente y registra el hallazgo con `ILogger`. Guardar sigue exigiendo valores válidos y es una acción explícita.
- El avatar utiliza una inicial segura cuando falta el nombre; no se escribe esa inicial ni una identidad inventada en persistencia.
- JSON inválido, colecciones/filas inválidas y valores fuera de rango producen errores controlados antes de importar. No se marca la importación como completada ni se escriben filas parcialmente.
- La copia original del navegador permanece intacta. Tras corregir la fuente, la importación puede reintentarse.
- En notas antiguas solo se normalizan los campos opcionales ausentes: título/contenido vacíos y hora nula. Sigue siendo necesario al menos título o contenido. Los campos adicionales no se eliminan.
- Se reutilizan `PersistenceErrors`, los estados de error existentes y `ILogger`; el log del importador no imprime el contenido privado del dato.
- La configuración de IA conserva su codec y estrategia existentes; sus verificadores cubren defaults, validación y persistencia. No se añadieron llamadas de IA.

El importador rechaza datos críticos no recuperables como una unidad para evitar una importación parcial silenciosa. No se creó un editor de JSON ni una herramienta de reparación de base de datos.

### Pruebas y resultado

`ReliabilityVerification` pasó con Perfil parcialmente válido, Perfil completamente inválido, corrección explícita sin perder teléfono, colecciones/filas nulas, cadenas inválidas de notas, rechazo sin escrituras parciales y reintento desde una fuente corregida.

`legacy-recovery.test.mjs` ejecuta los módulos JS reales y pasó con datos correctos, notas antiguas parcialmente válidas, campos adicionales, JSON roto, raíces/filas inválidas y límites numéricos. Comprueba que las lecturas no sobrescriben el almacenamiento original.

La prueba previa de reintento de Perfil sigue pasando.

## C. Navegación y desconexión

### Riesgos y solución

- Una importación JS podía terminar cuando el componente ya se había destruido: ahora se libera esa referencia sin retenerla ni actualizar la vista abandonada.
- Renders superpuestos podían iniciar más de una importación de Agenda/Métricas: una guarda comparte el proceso de importación.
- Si fallaba `stop` al desconectarse, podía omitirse la liberación del módulo: `BrowserModuleLifetime` intenta ambas acciones por separado.
- Callbacks de notificaciones y operaciones terminadas después de navegar podían actualizar componentes destruidos: se comprueba su ciclo de vida y se observan los errores de los callbacks.
- La actualización visual del Perfil vía JS podía fallar después de que SQLite ya hubiera confirmado el guardado: la desconexión de presentación no convierte esa escritura completada en un falso error de persistencia.
- Agenda puede cargar sus datos aunque el módulo del popover aún no esté disponible; evita quedar bloqueada por una importación interrumpida y permite reintentar su preparación.

### Qué se cancela y qué se completa

Se cancelan lecturas de la vista de Stock/Precios/Métricas, procesamiento de documentos y copia de logo cuando ya no interesan al componente. Se descartan resultados/callbacks tardíos de UI.

Las escrituras ya aceptadas en SQLite **no se cancelan por navegación**. Esto incluye las ediciones de Precios que ya estaban en cola: terminan en orden sin actualizar la vista destruida. No se acepta un evento nuevo desde esa vista después de `Dispose`.

No se implementó offline mode ni un protocolo nuevo de reconexión.

### Pruebas automáticas y límite manual

`ReliabilityVerification` pasó con:

- escritura iniciada y otra aceptada en cola → Dispose → ambas terminan en orden;
- evento nuevo después de Dispose → ignorado;
- lectura cancelada → `OperationCanceledException` controlada;
- desconexión JS después de guardar Perfil → datos confirmados y estado consistente;
- `stop` desconectado → referencia del módulo liberada;
- importación demorada de Agenda, Métricas y tutorial → navegación → referencia liberada;
- renders superpuestos de Agenda/Métricas → una sola importación.

Estas pruebas de JS desconectado **no simulan una reconexión real de SignalR**. Queda para prueba manual cortar/restablecer la red durante una edición, confirmar que no se duplica la operación y verificar recuperación del circuito. Si el circuito expira, debe recargarse y comprobarse el dato persistido. No se afirma cobertura automática de ese escenario.

## D. Verificación conjunta y estado general

### Ejecutado con éxito sobre el estado final

- `DatabaseVerification`, incluida la regresión previa de Perfil.
- `AiSettingsVerification`: 20 comprobaciones.
- `NoticesVerification`: 17 comprobaciones.
- `MetricsVerification`: 20 comprobaciones.
- `MetricsPresentationVerification`: 64 comprobaciones.
- `StockIntakeVerification`.
- `ProductionHostingVerification`: proceso .NET nativo y almacenamiento temporal, sin ejecutar Docker/Railway.
- `AccountHttpVerification`: cookies, rutas, autenticación, CSRF y rate limiting.
- `DevelopmentAccessVerification`: entrada/salida sin duplicar ni alterar datos.
- Nuevo `ReliabilityVerification`.
- JavaScript existente `metrics-motion.test.mjs` y nuevo `legacy-recovery.test.mjs`.
- `dotnet build`: **0 errores, 0 warnings**.
- Publicación Release: exitosa.
- `git diff --check`: sin errores de whitespace.

Los tests SQL/HTTP usan bases aisladas en TEMP, nunca la base local del usuario. La app publicada se recorrió por los ocho módulos del dashboard, Perfil, Stock, Precios, Agenda, Métricas, Avisos, Conversaciones y Configuración de IA. Se comprobó apertura/cierre del popover de Agenda. No aparecieron warnings/errores de consola ni en los logs del servidor de prueba. Las fixtures de datos deliberadamente inválidos sí verifican los errores controlados esperados.

### Archivos del conjunto local

Modificados en esta tarea, rutas relativas a este directorio:

- `Aquarella/Models/ProductPrice.cs`
- `Aquarella/Services/BusinessData.cs`
- `Aquarella/Services/BusinessProfileStore.cs` — además conserva la corrección anterior.
- `Aquarella/Services/DatabaseBusinessProfilePersistence.cs`
- `Aquarella/Services/PersistenceErrors.cs`
- `Aquarella/Services/MetricsService.cs`
- `Aquarella/Services/AgendaStore.cs`
- `Aquarella/Components/Pages/Precios.razor`
- `Aquarella/Components/Pages/Perfil.razor`
- `Aquarella/Components/Pages/ConfiguracionPagina.razor`
- `Aquarella/Components/Pages/Stock.razor`
- `Aquarella/Components/Pages/Agenda.razor`
- `Aquarella/Components/Pages/Metricas.razor`
- `Aquarella/Components/Layout/BusinessHeader.razor`
- `Aquarella/Components/OnboardingTour.razor`
- `Aquarella/Components/StockInvoiceImport.razor`
- `Aquarella/wwwroot/business-identity.js`
- `Aquarella/wwwroot/stock-storage.js`
- `Aquarella/wwwroot/prices-storage.js`
- `Aquarella/wwwroot/agenda-storage.js`

Nuevos:

- `Aquarella/Services/BrowserModuleLifetime.cs`
- `tools/ReliabilityVerification/ReliabilityVerification.csproj`
- `tools/ReliabilityVerification/Program.cs`
- `tools/ReliabilityVerification/legacy-recovery.test.mjs`
- Este informe.

Pendientes anteriores preservados: `tools/DatabaseVerification/Program.cs`, informe de auditoría y siete previews PNG. El conjunto tiene **21 archivos previamente versionados modificados**; no se eliminó ningún archivo del usuario ni se agregó nada al staging.

### Límites y próximos pasos

1. Realizar la prueba manual de caída/reconexión SignalR descrita arriba.
2. En una futura revisión de persistencia, decidir si hace falta control de versiones para conflictos sobre el mismo campo y notificaciones entre varias instancias del servidor. No fue necesario cambiar el esquema para cerrar el riesgo actual de reemplazar campos ajenos.
3. Los datos legados críticos que no se puedan interpretar permanecen conservados y requieren corregir su fuente; no se diseñó una UI de reparación en esta tarea.

La rama y HEAD se mantienen. El working tree queda con los cambios locales para revisión, sin commit ni push. La instancia temporal de verificación se cierra al finalizar; no se detienen otras instancias de Aquarella/dotnet del usuario.
