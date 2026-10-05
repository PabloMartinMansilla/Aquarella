# Motion design de Aquarella

Fecha: 5 de octubre de 2026.

## Criterio y stack

Se aplicaron principios de respuesta inmediata, continuidad, jerarquía y movimiento breve. Las referencias Linear, Vercel, Notion, Stripe y Apple se tomaron como orientación conceptual, sin copiar sus interfaces.

Aquarella usa Blazor Interactive Server en .NET 10, CSS aislado y una capa visual compartida. Se reutilizaron `design-system.css`, `visual-feedback.js`, el sistema de scroll y los módulos de Agenda/tutorial. No se instaló ninguna dependencia.

## Duraciones y easing

- Fast: **140 ms**, estados de controles, números y salidas breves.
- Normal: **200 ms**, entrada de contenido, dropdowns, acordeones y cambios de calendario.
- Slow: **280 ms**, desplazamiento del drawer y cierre nativo del diálogo de reconexión.
- Entrada/continuidad: `cubic-bezier(.22, 1, .36, 1)`.
- Cambio de estado: `cubic-bezier(.2, 0, .2, 1)`.

Los valores están centralizados en variables CSS y las animaciones nativas de JavaScript leen esas variables. No se introdujeron esperas artificiales en navegación ni guardado. Se conservó el sistema individual de scroll, incluidos sus umbrales de entrada/salida y tiempos previamente ajustados.

## Cambios por área

| Área | Refinamiento |
|---|---|
| Navegación | Fade de contenido de 200 ms, sin mover el contenedor de los paneles posicionados respecto al viewport |
| Sidebar | Drawer de 280 ms, backdrop de 200 ms y transición breve del estado activo; se mantienen el foco, Escape y la navegación |
| Botones | Se reutilizan hover/press existentes, con Fast de 140 ms; `aria-busy` permite mostrar el indicador de procesamiento también en controles como Actualizar |
| Cards | Se elimina la segunda animación de entrada y su stagger del Dashboard para no competir con scroll-reveal; se conserva el hover discreto en desktop |
| Modales/paneles | Salidas de 140 ms en Agenda/tutorial mediante copias temporales inertes, sin retrasar la acción real; reconexión sustituye la entrada de 1,5 s con demora por una entrada breve |
| Dropdowns | Buscador con entrada breve y salida mediante el mismo mecanismo visual; los selectores nativos conservan su comportamiento del navegador |
| Accordions | CSS nativo en Configuración de IA y detalles de Avisos: apertura/cierre de 200 ms y opacidad de 140 ms. Navegadores sin soporte mantienen la interacción instantánea |
| Formularios | Se reutilizan foco, errores con texto y estados deshabilitados; Login/Crear cuenta añaden `aria-busy` al procesamiento existente. Sin shake ni cambios en validaciones |
| Guardado/toasts | Se conserva el sistema existente de mensajes inline y toast; la entrada/salida usa los tokens compartidos y sigue sin bloquear interacción |
| Tablas | Hover discreto de filas de Precios en desktop; números actualizados reciben un fade breve, sin desplazarse ni contar desde cero |
| Stock | Formulario de edición con entrada/salida breve; cantidad actual con feedback al cambiar; un producto agregado puede entrar discretamente, sin stagger ni animar cargas masivas |
| Avisos | Entrada de grupos, salida breve de avisos resueltos y continuidad por transform para los grupos que cambian de posición. Sin modificar reglas ni colores semánticos |
| Agenda | Selección de día con cambio de borde/fondo breve; cambio de mes de 200 ms; cierre de popover sin bloquear el calendario y manteniendo el retorno de foco |
| Precios | Feedback breve en valores calculados, conservando edición, cálculo y guardado automático |
| Métricas | Entrada conjunta de KPIs y feedback de números actualizados; no se agregaron contadores, gráficos ni filtros |
| Tutorial | Transición breve entre pasos y salida compartida; se eliminó la interpolación continua de top/left/width/height del spotlight |
| Login/Crear cuenta | Entrada del formulario de 200 ms y estado de procesamiento con indicador; se mantienen autenticación, mensajes y diseño |
| Decoración | Fondo estático y créditos estáticos; se quitaron los loops decorativos |

## Performance y accesibilidad

La mayoría del movimiento usa opacity/transform. La interpolación de altura se limita a los acordeones nativos, donde permite seguir la apertura/cierre del contenido. No se añadieron listeners permanentes: se amplió el observador visual existente. Los listeners de Agenda/tutorial se siguen liberando al cerrar.

Las copias de salida duran 140 ms, no interceptan interacción, son inertes y están ocultas para accesibilidad. Sus IDs, nombres, autofocus y anuncios live se eliminan; desaparecen al finalizar o cancelar la animación. Solo se usan para superficies pequeñas/grupos, no para duplicar tablas completas.

`prefers-reduced-motion` conserva las reglas globales que desactivan animaciones/transiciones CSS. JavaScript evita iniciar animaciones o copias y cancela las activas si cambia la preferencia. Los estados, textos y posiciones finales siguen siendo visibles.

## Qué se decidió no agregar

Sin bounce/springs, zoom de páginas, parallax, contadores desde cero, stagger de productos, skeletons que parpadeen, animaciones de gráficos inexistentes ni loaders artificiales. No se reemplazan los controles nativos ni se retiene una fila eliminada como dato activo solo para animarla.

No se modificaron layout, colores globales, navegación, base de datos, migraciones, entidades, endpoints, autenticación, cálculos, reglas o persistencia. Sin commit, push ni ejecución de Docker/Railway.

## Verificación

- Recorrido de nueve rutas en 375, 768 y 1280 px: **27 combinaciones sin desbordes horizontales**.
- Navegación y formularios, tabla de Precios, acordeones abiertos/cerrados repetidamente y menú con Escape.
- Agenda: cinco aperturas/cierres seguidos, cambio de mes y dos ciclos adicionales en una conexión nueva; sin copias restantes, con retorno del foco al día.
- Stock/Avisos: cambio temporal de cantidad 7 → 0 → 7 en una base de prueba; aviso aparece y se resuelve automáticamente.
- Precios: cambio temporal de costo 100 → 101 → 100; cálculo y confirmación de guardado correctos.
- Perfil: guardado de los mismos valores, con confirmación inline y toast.
- Tutorial: seis pasos completos en celular, dentro del viewport.
- Sin errores de scripts en el recorrido estable. El reinicio deliberado del servidor de prueba produjo los reintentos de conexión esperables; una conexión nueva se verificó sin errores.
- Sintaxis de los cuatro scripts modificados verificada con Node.
- Prueba aislada con preferencia de movimiento reducido simulada: no inicia animaciones ni copias y cancela las tres animaciones activas; verifica 140/200/280 ms. Se revisaron además las reglas CSS de reduced motion. No se cambió la preferencia del sistema operativo del usuario.
- `dotnet build` Release: **0 errores, 0 advertencias**. No hay un build frontend separado.

Las pruebas se realizaron en una instancia temporal en 18188 con SQLite de prueba; los valores utilizados se restauraron. La instancia se cerró al terminar. No se ejecutó un benchmark de FPS ni una prueba en teléfonos físicos; la revisión confirma interacción funcional, geometría y limpieza de efectos en el navegador disponible.

## Archivos modificados

Rutas relativas a `Aquarella/Aquarella/`:

- `wwwroot/design-system.css`
- `wwwroot/visual-feedback.js`
- `wwwroot/agenda-popover.js`
- `wwwroot/onboarding.js`
- `wwwroot/onboarding.css`
- `wwwroot/account.js`
- `Components/CreatorCredits.razor`
- `Components/CreatorCredits.razor.css`
- `Components/Layout/ReconnectModal.razor.css`

También se agregó este informe: `Aquarella/MOTION-DESIGN-AUDIT.md`.
