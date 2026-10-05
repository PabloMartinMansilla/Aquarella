# Métricas: rediseño visual y verificación

Fecha: 5 de octubre de 2026. Alcance: presentación de /metricas. Sin commit ni push.

## Auditoría y orden de implementación

Se revisaron la página, sus estilos, MetricProductList, BusinessMetrics, MetricsService, MetricsCalculator, avisos y los sistemas visual-feedback, brand-intro y design-system. La pantalla tenía cinco KPIs de inventario, un porcentaje ponderado separado, dos rankings textuales y un listado de avisos. No tenía gráficos, librería de charts ni filtros. El snapshot disponible no contiene categorías ni series temporales.

La implementación siguió este orden: auditoría; jerarquía; layout y gráficos estáticos; detalles e interacciones; build estático; motion; coordinación; pruebas responsive; reduced motion; comparación con datos originales; recorrido visual; build final.

Los cálculos y fuentes permanecen en MetricsCalculator y MetricsService, sin cambios. No se cambiaron entidades, migraciones, persistencia, autenticación ni otros módulos en esta tarea. Los cambios pendientes de tareas anteriores se conservaron.

## Informe solicitado

1. **Textos reducidos.** Se quitaron los párrafos que repetían cantidades, fórmula, alcance e historial. La fórmula y condiciones de los importes se consultan en las ayudas. Permanecen las aclaraciones indispensables: valores potenciales, importes parciales, porcentajes sobre costo y posibilidad de avisos superpuestos. Costo/venta/ganancia de cada producto aparecen en el detalle. Con hasta cinco productos calculables se muestra una sola lista, evitando duplicar los rankings.

2. **KPIs conservados.** Inventario a costo, valor potencial de venta, ganancia potencial ponderada sobre costo, unidades disponibles, productos y productos sin stock. El porcentaje existente pasó a la zona superior. No se agregó un “margen sobre ventas”: el cálculo existente es ganancia sobre costo.

3–5. **Gráficos, elección y datos.**

| Visualización | Por qué | Datos del snapshot |
| --- | --- | --- |
| Barras de ganancia por producto | Comparar porcentajes, mostrando pérdidas a la izquierda y ganancias a la derecha | HighestProfit / LowestProfit, ProfitPercent; Cost, SalePrice y Profit en el detalle |
| Donut de stock | Dos estados mutuamente excluyentes | ProductsInStock y OutOfStock respecto de ProductCount |
| Progreso de cobertura | Proporción acotada entre cero y el total | ProductsWithCost y ProductsWithSalePrice respecto de ProductsInStock |
| Barras de avisos | Comparar cantidades por causa sin sugerir que sean categorías exclusivas | Notices: Count, Kind y Severity |

Las barras de productos comparten una escala simétrica calculada del máximo porcentaje absoluto de ambos rankings. La escala se indica; no se limita una ganancia a 100%, ni se ocultan valores negativos. Un porcentaje pequeño también usa la escala real. Las barras de avisos se normalizan contra la mayor cantidad, no contra un supuesto total de productos únicos. El donut distingue con stock/sin stock; no inventa un umbral de stock bajo.

6–7. **Animaciones y tiempos.** Tarjetas: opacidad .65 → 1 y translateY(6px) → 0 durante 280ms, stagger de 18ms acotado a 126ms. KPIs: count-up de 600ms, inicio a 100ms. Barras: scaleX durante 560ms, inicio a 150ms y stagger de 12ms acotado a 96ms. Donut: stroke-dasharray durante 600ms, inicio a 150ms. Todas se superponen y terminan aproximadamente a los 806ms. Se reutilizan --motion-normal, --motion-slow y --ease-soft del diseño actual. No hay loops ni animaciones por hover, foco o scroll.

8. **Exactitud final.** El servidor genera el texto definitivo con decimal y cultura es-AR (moneda y porcentajes N2, cantidades N0). JS anima únicamente un span decorativo aria-hidden; la etiqueta accesible ya contiene el resultado real. Al terminar o cancelar restaura el texto exacto de .NET. Los valores superiores a Number.MAX_SAFE_INTEGER se muestran directamente. Las medidas finales de barras y arcos están en HTML/SVG y la animación se retira; nunca se guarda un valor animado ni se altera el cálculo. Sin JS, todos los resultados ya están visibles.

9. **Tooltips/interacciones.** Ayuda contextual en el icono i y detalles de productos por hover desktop o clic. En mobile, toque para abrir/cerrar detalles en el flujo de la tarjeta. Los summaries funcionan con teclado y están asociados a sus explicaciones mediante aria-describedby. La leyenda del donut despliega el porcentaje exacto. Los avisos despliegan severidad y cantidad. Se preservan Actualizar, Volver y los enlaces a Stock, Precios y Avisos. No se agregaron filtros porque no existían.

10. **Responsive.** KPIs en tres columnas desktop, dos tablet y una mobile. Gráfico principal y donut comparten fila en desktop; tablet los reorganiza, mobile apila también los rankings. Detalles táctiles permanecen dentro del ancho disponible. Se verificó ausencia de scroll horizontal en 375×844 y 768×1024, además del viewport desktop. Los nombres largos e importes extremos pueden ocupar más líneas, sin truncar el dato.

11. **Reduced motion.** matchMedia evita count-up, desplazamientos y dibujo; muestra directamente los mismos valores y gráficos. Si la preferencia cambia durante una animación, se cancela y se restablece el resultado final. Esta rama se verificó con pruebas automatizadas de matchMedia; no se modificó la preferencia del sistema operativo. El sistema global existente de reduced motion sigue intacto.

12–13. **Tecnología/dependencias.** Componentes Razor, CSS y SVG nativos; Web Animations API y requestAnimationFrame para la presentación. No había una librería de gráficos para reutilizar. No se instalaron paquetes ni dependencias nuevas.

14. **Visualizaciones omitidas.** Sin líneas históricas, tendencias, ventas realizadas, categorías ni distribución por “stock bajo”, porque el snapshot no proporciona esos datos. Sin gauge 0–100 para ganancia: puede ser negativa o superar 100%. Sin donut de avisos: un producto puede tener más de un problema. No se dibuja un gráfico de ganancias si faltan costos. Un negocio sin productos muestra un estado vacío y acceso a Stock.

15. **Archivos de esta tarea.**

- Aquarella/Components/Pages/Metricas.razor: jerarquía, gráficos, detalles y ciclo de presentación.
- Aquarella/Components/Pages/Metricas.razor.css: grillas, donut, cobertura y responsive.
- Aquarella/Components/MetricProductList.razor: barras con signo y detalles exactos.
- Aquarella/Components/MetricProductList.razor.css: barras, tooltips y foco.
- Aquarella/Components/MetricKpi.razor (nuevo): KPI con resultado exacto y ayuda.
- Aquarella/Components/MetricKpi.razor.css (nuevo): estilo del KPI y tooltip.
- Aquarella/wwwroot/metrics-motion.js (nuevo): entrada finita, count-up, cancelación y coordinación con la intro.
- tools/MetricsPresentationVerification/MetricsPresentationVerification.csproj (nuevo).
- tools/MetricsPresentationVerification/Program.cs (nuevo): verificación de render y fixtures temporales.
- tools/MetricsPresentationVerification/metrics-motion.test.mjs (nuevo): pruebas de timing/cancelación/reduced motion.
- METRICAS-REDISENO-VERIFICACION.md (este informe).

16. **Build.** Ejecutado al finalizar:

    dotnet build Aquarella/Aquarella/Aquarella.csproj --no-restore -p:UseAppHost=false -c Release -o "$env:TEMP/aquarella-metrics-final"

Resultado: **0 errores, 0 advertencias**. También se publicó una copia temporal para las pruebas de navegador. Release y salida temporal permiten no interferir con el ejecutable abierto en Visual Studio.

## Pruebas realizadas

- MetricsVerification: **20 comprobaciones correctas** de cálculos, datos persistidos, cobertura, límites, aislamiento de usuarios, actualizaciones y errores.
- MetricsPresentationVerification: **64 comprobaciones correctas** de formato exacto, etiquetas accesibles, signo y escala de barras. Casos: vacío, faltan precios, un producto, porcentaje 0,01%, sin unidades, cuarenta productos y valores extremos.
- metrics-motion.test.mjs: fin antes de 850ms; finales exactos; números fuera del rango seguro; reduced motion inicial y durante animación; cancelación; coordinación con la intro; reemplazo de datos; desmontaje.
- Navegador: entrada desde dashboard y entrada directa; Actualizar; valores finales idénticos a data-final; barras sin transform residual; tooltips por hover y clic; detalles por toque y teclado; leyenda; enlaces a Stock, Precios, Avisos y dashboard.
- Se verificaron en navegador los casos vacío, precios faltantes, un producto, stock agotado, porcentaje pequeño, cuarenta productos e importes extremos. La última entrada en una pestaña nueva no produjo warnings ni errores en consola.
- Se corrigió una excepción de interop que apareció al apagar el servidor temporal con un circuito desconectado. El cierre final no volvió a producir ese error.
- Datos de prueba exclusivamente en una SQLite dedicada dentro de TEMP. El verificador rechaza rutas que no sean archivos temporales con el prefijo aquarella-metrics-fixture-. No se utilizó la base de datos real ni el puerto 7188; no se modificó launchSettings. El servidor temporal de 18188 quedó detenido.

Comparación verificada del caso de 40 productos: costo $262.240,00; venta potencial $459.075,00; ganancia 75,06%; unidades 720; productos 40; sin stock 7; donut 33/7 (82,5%/17,5%); cobertura 31/33 en ambas barras; avisos 5 por venta bajo costo y 7 sin stock.

Con el caso extremo, el navegador conservó exactamente $2.147.483.649.126.008.811,54 y $2.651.214.355.144.992.867,64; los porcentajes negativos y superiores a 100% conservaron su valor. Ante un valor desproporcionadamente alto, las otras barras quedan pequeñas según la escala común; los números y detalles siguen disponibles.

Capturas de verificación con fixtures temporales:

- [Desktop](C:/Users/pablo/.codex/visualizations/2026/10/03/01a0ff33-ebf7-7311-83ec-63cdf615538d/aquarella-metricas-desktop.png)
- [Tablet](C:/Users/pablo/.codex/visualizations/2026/10/03/01a0ff33-ebf7-7311-83ec-63cdf615538d/aquarella-metricas-tablet.png)
- [Mobile](C:/Users/pablo/.codex/visualizations/2026/10/03/01a0ff33-ebf7-7311-83ec-63cdf615538d/aquarella-metricas-mobile.png)
