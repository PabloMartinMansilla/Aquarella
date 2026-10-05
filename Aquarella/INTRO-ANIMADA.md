# Intro de Aquarella

- Implementación frontend compartida entre el documento Blazor y el layout Razor de acceso/cuenta. Un script pequeño monta una capa decorativa fija antes del primer paint; no cambia rutas, cookies, formularios ni estado del servidor.
- Identidad revisada: no hay imágenes/logos de marca en wwwroot. Se reutilizan la A circular existente, el nombre Aquarella y Segoe UI, junto con los colores del tema. Las nueve letras son las piezas; no se agregan assets.
- Duración normal ajustada: aproximadamente 1380 ms. La A entra y gira durante 800 ms; las letras entran desde la izquierda durante 760 ms, escalonadas 22 ms, con las mismas rotaciones deterministas alternadas. Todas terminan alineadas hacia los 1026 ms; a los 1100 ms comienza un crossfade de 280 ms.
- Reutiliza --motion-slow (280 ms), --motion-fast y --ease-soft del sistema de motion. Los tiempos específicos se centralizan en --intro-hold, --intro-fade, --intro-mark-duration y --intro-piece-duration. No se incorporan dependencias ni un motor nuevo de animación: CSS para entrada y Web Animations nativo para salida.
- El contenido real carga debajo. Login (.account-page) o la interfaz completa (.app-shell, incluyendo encabezado, navegación, menú y tarjetas) permanece en opacity 0 durante el armado; al comenzar la salida cambia a opacity 1 mediante un fade de 280 ms, simultáneo y con el mismo easing que la intro. No se agregan delays por objeto: las animaciones propias iniciales continúan debajo de la intro. La intro no espera al load, al circuito Blazor ni a SQLite; la app conserva sus indicadores de carga existentes. Una salida CSS de seguridad a los 1700 ms y limpieza JS acotada evitan una capa congelada.
- Frecuencia: una vez por sesión de pestaña mediante sessionStorage, tanto entrando por Login como con una sesión existente. No se reproduce al navegar, recargar ni al pasar de Login al dashboard. No se reinicia por cerrar/iniciar sesión dentro de la misma pestaña. Si el almacenamiento está bloqueado, se omite.
- Responsive: composición centrada, nombre con clamp y viaje reducido de 55vw a 36vw debajo de 600px. Capa fija con overflow hidden y contain; no modifica las medidas de la página ni el scroll existente.
- Reduced motion: sin desplazamientos ni rotación; fade breve de la composición y salida, aproximadamente 300 ms. Un cambio de preferencia durante la intro inicia su cierre. La capa es aria-hidden y no toma foco ni intercepta controles.

## Archivos de esta tarea

- Aquarella/wwwroot/brand-intro.js (nuevo): frecuencia, composición y limpieza.
- Aquarella/wwwroot/design-system.css: estilos y keyframes de la intro.
- Aquarella/Components/App.razor: carga del script en la aplicación.
- Aquarella/Pages/Shared/_AccountLayout.cshtml: carga del mismo script en Login/cuenta.
- INTRO-ANIMADA.md: este informe.

Se conservaron los cambios pendientes de la pasada de motion anterior. No se hizo commit ni push.

## Verificación

- Build Release .NET 10: 0 errores, 0 advertencias. JavaScript pasó node --check y git diff --check no detectó errores de whitespace.
- Servidor aislado https://localhost:18188 con SQLite de pruebas; sin usar el puerto de Visual Studio ni los datos reales.
- Navegador real: primera entrada de pestaña autenticada, Login inicial, acceso de prueba al dashboard, navegación a Stock, regreso y refresh. Intro presente en primera entrada y ausente en recarga/navegación; se elimina al terminar.
- Viewports de escritorio, 768px y 375px: sin desbordamiento horizontal. Capturados fotogramas de entrada en escritorio y móvil.
- Simulación aislada del script: primera entrada, nueve piezas, supresión de repetición, body tardío, eliminación sin esperar disponibilidad de aplicación, reduced motion, cambio de preferencia, almacenamiento bloqueado y navegador sin WAAPI: todas pasaron.
- Límites: reduced motion y carga lenta se verificaron mediante simulación y revisión CSS, no mediante throttling de red ni cambio de la preferencia del sistema en el navegador. No se cambiaron esas preferencias del usuario.

## Verificación del ajuste de timing y transición

- Build Release: 0 errores y 0 advertencias; node --check correcto.
- Navegador: Login y Dashboard iniciaron con opacity 0. Durante revealing se observaron valores intermedios de opacidad en la interfaz y valores inversos en la intro; al finalizar, opacity 1 y atributo temporal eliminado.
- Refresh y navegación a Stock: sin repetición ni opacidad residual.
- Primera entrada móvil (375px): mismo crossfade de 280 ms, sin desbordamiento horizontal.
- Simulación aislada: crossfade disparado a 1100 ms y fin a 1380 ms, reduced motion a 300 ms, cambio de preferencia durante la intro, body tardío, almacenamiento bloqueado, repetición y ausencia de WAAPI: todas pasaron.
- Este ajuste modifica solamente brand-intro.js, design-system.css y este informe. No se cambió el concepto, stagger, recorridos, giros, identidad ni backend. Sin commit ni push.
