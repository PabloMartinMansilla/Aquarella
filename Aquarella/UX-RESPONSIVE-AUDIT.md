# Auditoría responsive de Aquarella

Fecha: 4 de octubre de 2026. Alcance: pulido de presentación y accesibilidad, sin rediseño.

## Pantallas revisadas

Dashboard, Stock, Avisos, Agenda, Precios, Métricas, Perfil, Configuración de página, Configuración de IA, Login y Crear cuenta. También menú lateral, buscador, formularios, navegación, popover de notas y tutorial. Conversaciones quedó fuera del alcance indicado.

Se revisaron anchos de 320, 375, 390, 430, 768 y 1280 px, con altura de 844 px. No apareció scroll horizontal accidental en las rutas comprobadas. La tabla de Precios conserva su desplazamiento horizontal dentro de su propio contenedor.

## Problemas encontrados y correcciones

| Zona | Problema comprobado o identificado en el código | Corrección |
|---|---|---|
| Stock | A 320 px, Guardar producto se partía en dos líneas; botones +/− de unos 40 px de ancho | Guardar ocupa una fila en mobile; controles +/− de 44 px, campo flexible y Confirmar en su propia fila |
| Stock | Acciones de fila sin separación | Separación de 8 px y ajuste de línea cuando sea necesario |
| Precios | Usar sugerido tenía un área de toque pequeña | Altura mínima de 44 px y texto de 13 px en mobile/tablet |
| Precios | No se explicaba cómo recorrer la tabla en celular | Indicación breve de desplazamiento; contenedor accesible con teclado y descripción asociada |
| Agenda | Flechas mensuales de unos 36 px de ancho | Controles de al menos 44 × 44 px; el título usa el espacio disponible |
| Agenda | Confirmaciones de eliminación pequeñas | Mayor área de toque y separación, sin alterar la confirmación |
| Agenda | Cerrar el popover perdía el foco del día seleccionado | Retorno de foco al día, sin desplazar el calendario |
| Menú | Un diálogo modal permitía salir con Tab hacia la página | Foco contenido dentro del menú; fondo inerte mientras está abierto y restaurado al cerrar |
| Menú | Nombres largos podían competir con la X | Texto adaptable, separación y botón de cierre que no se comprime |
| Buscador | Resultados sin límite de altura | Lista con altura máxima y desplazamiento propio |
| Login/cuenta | Links pequeños y padding heredado en checkbox | Links y control de contraseña de 44 px en mobile; checkbox sin padding y etiqueta cómoda para tocar |
| Mi cuenta | Margen de botón más gap duplicaban el espacio | Eliminación del margen adicional en el grupo de acciones |
| Perfil | Selector nativo de archivo pequeño | Botón de archivo de 44 px con los colores y bordes existentes |
| Tutorial | Texto de Perfil seguía indicando que allí se configuraban colores | Texto corregido para reflejar la organización actual |
| Tutorial | Al liberar un circuito desconectado podía emitir una excepción de presentación | Manejo específico de JSDisconnectedException durante la liberación |
| Aviso general de error | Texto en inglés y cierre sin botón accesible | Texto en español, botón con nombre accesible y espacio reservado para cerrarlo |

## Estados y feedback

- Stock vacío ahora ofrece “Agregar el primero”, reutilizando la acción existente; se oculta esa acción mientras el formulario ya está abierto.
- Agenda informa “Cargando notas…” durante la carga inicial.
- Se conservaron estados de carga, error, éxito y deshabilitado existentes en los demás módulos. No se agregaron resultados ficticios.
- Se comprobó el estado “Todo en orden” de Avisos en la cuenta de prueba.
- Se probó guardar Perfil sin modificar sus valores: apareció la confirmación inline y el aviso visual de éxito existentes. Se conservaron sus advertencias opcionales.
- Los estados vacíos de Stock, Precios y Métricas se revisaron también en el código. No se vació el inventario para simularlos.

## Verificación

- Recorrido inicial de las once rutas en los seis anchos; Login autenticado redirige al Dashboard, por lo que también se comprobó Login por separado sin sesión.
- Verificación final de ocho módulos en seis anchos: 48 combinaciones, sin desbordes horizontales ni controles de formulario sin etiqueta accesible.
- Login y Crear cuenta: 12 combinaciones sin desbordes ni inputs sin etiqueta.
- Stock: apertura del formulario y medición de controles en mobile; prueba final también en tablet y desktop.
- Menú: Tab desde Cerrar sesión vuelve al primer control; Escape cierra y devuelve el foco al botón de apertura.
- Agenda: navegación entre meses, apertura de notas existentes, cierre con X y retorno del foco. Popover dentro del viewport en 320, 768 y 1280 px.
- Tutorial: seis pasos completos a 320 px; segundo paso y flujo de salida comprobados también a 768 y 1280 px. Ventanas dentro del viewport.
- Build Release: `dotnet build Aquarella/Aquarella/Aquarella.csproj --no-restore -p:UseAppHost=false -c Release -o <carpeta temporal>` → **0 errores, 0 advertencias**.
- No existe build de frontend separado que corresponda ejecutar.

Las pruebas usaron una base temporal y una instancia en el puerto 18188. Esa instancia fue cerrada al terminar; no se modificó el puerto ni se cerró la instancia de Visual Studio.

## Lo que se mantuvo

Dashboard, distribución general, tarjetas, animaciones, colores configurables y sistema visual existente. Sin cambios en entidades, migraciones, autenticación, permisos, reglas de Stock/Precios/Avisos, cálculos, persistencia, preferencias de IA, APIs o despliegue. Sin nuevas dependencias, commit ni push. Docker/Railway permanecieron sin ejecutar.

## Límites y trabajo posterior

No se detectó un problema que requiera rehacer pantallas o cambiar arquitectura. Queda recomendable una prueba en teléfonos físicos con teclado virtual, zoom y lector de pantalla. Esta revisión no es una auditoría WCAG completa; tampoco se forzaron fallas de base de datos ni se cambiaron las reglas de validación.

## Archivos modificados en esta pasada

Rutas relativas a `Aquarella/Aquarella/`:

- `Components/Pages/Stock.razor` y `Stock.razor.css`
- `Components/Pages/Precios.razor` y `Precios.razor.css`
- `Components/Pages/Agenda.razor` y `Agenda.razor.css`
- `Components/Layout/BusinessHeader.razor.css`
- `Components/Layout/MainLayout.razor` y `MainLayout.razor.css`
- `Components/OnboardingTour.razor`
- `wwwroot/account.css`
- `wwwroot/design-system.css`
- `wwwroot/visual-feedback.js`
- `wwwroot/agenda-popover.js`
- `wwwroot/onboarding.js`

También se agregó este informe, `Aquarella/UX-RESPONSIVE-AUDIT.md`. Los cambios anteriores del proyecto se conservaron.
