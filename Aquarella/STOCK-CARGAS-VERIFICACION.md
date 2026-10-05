# Carga manual y de documentos en Stock

## Implementación

1. Antes: el panel admitía nombre y cantidad, guardaba una fila nueva y se cerraba. Los identificadores son GUID; no existe SKU/código de barras. Costo, margen y precio están en Products y se administran desde Precios.
2. Se conservó la página, listado, acción superior y panel. Crear tiene un único botón principal «Agregar producto» y una acción secundaria «Cerrar». Editar mantiene «Guardar cambios» y «Cancelar».
3. Agregar valida los campos y realiza una operación de ingreso de Stock en el servidor. Nombre hasta 120 caracteres; cantidad entera requerida, entre 0 e int.MaxValue. No se agregaron campos obligatorios de costo a la carga manual.
4. Después del éxito el panel permanece abierto; nombre y cantidad quedan vacíos, se renueva EditContext y el foco vuelve a Nombre. Si falla, se conservan los datos. Los inputs exclusivos de Stock actualizan al escribir y conservan la validación de InputText/InputNumber, para que Enter envíe el valor visible.
5. La búsqueda se hace solamente entre productos del negocio autenticado, dentro de la transacción. Se reutiliza el GUID al asociar un producto explícitamente.
6. Normalización determinista: Unicode FormKC, mayúsculas invariantes, espacios compactados, guiones/puntuación trivial como separadores, coma decimal entre dígitos equivalente a punto. Se conservan dígitos, tamaños, unidades, acentos, signos +, slash y precisión decimal.
7. Sin fuzzy matching ni similitud semántica. 1,5 L y 2,25 L siguen separados. Si hay varias coincidencias normalizadas se exige una decisión; no se elige la primera. Nombres cuyo valor normalizado queda vacío no se fusionan automáticamente.
8. Ingreso: cantidad existente + cantidad ingresada, con comprobación del límite entero. El feedback distingue creación de actualización y muestra incremento/cantidad final.
9. Edición explícita usa el flujo existente: reemplaza nombre/cantidad, no suma. Ajustes +/- y eliminación no se cambiaron. La carga manual conserva todos los atributos de precios.
10. «Cargar factura» abre un panel dentro de Stock, sin ruta nueva. Estados de selección, procesamiento, revisión, confirmación y completado; cancelación y errores explícitos. El procesamiento tiene límite de 20 segundos.
11. Admite JPG/JPEG, PNG, WebP y PDF hasta 10 MB, comprobando tamaño y firma del contenido. Incluye selección normal y control nativo con capture=environment para cámara cuando la plataforma lo permita. No admite HEIC/GIF ni realiza conversiones implícitas.
12. Seleccionar/procesar un documento no escribe productos. Se presentan líneas editables y solamente «Confirmar carga» ejecuta la transacción. Costos unitarios opcionales; cantidades y nombres requeridos. Se pueden agregar/quitar líneas (hasta 200).
13. Cada línea muestra NUEVO o EXISTENTE — SUMAR STOCK y la cuenta «actuales + nuevas → final». La propuesta incluye también coincidencias dentro de la misma carga. En el servidor se recalcula con el Stock vigente.
14. El selector permite detección conservadora, asociación explícita a un producto del propio negocio o creación explícita de otro. Una coincidencia dudosa nunca modifica Stock por sí sola.
15. Guardado deshabilita los controles y rechaza reentradas. StockIntakeReceipts conserva una operación única por negocio: el reintento devuelve la confirmación previa y no suma de nuevo. Para un documento se usa SHA-256 de sus bytes; su confirmación sigue reconocible al recargar o reabrirlo. La carga manual renueva su identificador después del éxito. El ejemplo simulado representa una nueva operación cada vez que se inicia explícitamente.
16. Lectura automática real: NO implementada, sin llamadas a IA/OCR. IInvoiceExtractor tiene un adaptador UnavailableInvoiceExtractor que informa esa limitación. Hay transcripción manual del documento seleccionado y un ejemplo claramente rotulado «DATOS SIMULADOS», con aviso de que confirmar sí modifica Stock.
17. Falta conectar un lector real de imágenes/PDF a IInvoiceExtractor: reconocimiento/OCR, interpretación de formatos, tratamiento de documentos ilegibles, mapeo fiable de unidades y costos, y configuración explícita del proveedor si fuese externo. El contrato incluye líneas, subtotal por línea y metadatos opcionales de proveedor/fecha/subtotal/impuestos/descuentos/total. Los totales nunca se infieren como costo unitario. No se añadieron dependencias ni credenciales.
18. Backend: ApplyStockIntakeAsync, planificador compartido y tabla aditiva StockIntakeReceipts con índice único BusinessId/OperationKey. Productos y comprobante se guardan en una única transacción. No se alteraron ni borraron tablas/columnas existentes. El costo sólo cambia si se completó: conserva margen/precio manual; si el precio era automático se recalcula siguiendo ProductPrice. Se publican las notificaciones existentes. No se almacenan documentos originales.

## Archivos de esta tarea

19. Archivos modificados/agregados:

- Aquarella/Components/Pages/Stock.razor y Stock.razor.css.
- Aquarella/Components/StockInvoiceImport.razor y StockInvoiceImport.razor.css (nuevos).
- Aquarella/Components/StockTextInput.cs y StockNumberInput.cs (nuevos; inputs al escribir con validación Blazor).
- Aquarella/Models/StockIntake.cs (nuevo; modelos y planificación/normalización).
- Aquarella/Services/InvoiceExtraction.cs (nuevo; contrato, adaptador sin lector y validación de archivos).
- Aquarella/Services/BusinessData.cs y Aquarella/Program.cs.
- Aquarella/Data/Entities.cs y Aquarella/Data/AquarellaDbContext.cs.
- Aquarella/Data/Migrations/20261005180000_StockIntakeReceipts.cs y su Designer.cs (nuevos), más AquarellaDbContextModelSnapshot.cs.
- tools/StockIntakeVerification/Program.cs y StockIntakeVerification.csproj (nuevos).
- tools/AiSettingsVerification/Program.cs: prueba dirigida a su migración específica, sin asumir que es siempre la última.
- tools/ProductionHostingVerification/Program.cs: comprobación de todas las migraciones actuales, en lugar del recuento antiguo fijo.
- STOCK-CARGAS-VERIFICACION.md: este informe.

No se modificaron los archivos de intro, dashboard, Perfil, Agenda ni otras funcionalidades durante esta tarea. Se conservaron sus cambios pendientes anteriores. Sin commit ni push; Docker/Railway permanecieron sin ejecutar.

## Verificación

20. Resultados:

- Build Release .NET 10: 0 errores y 0 advertencias. Publish Release correcto. git diff --check sin errores.
- StockIntakeVerification: migración aditiva/snapshot coherente, nombres normalizados, tamaños distintos, suma 8+6=14, edición absoluta, mezcla nuevos/existentes, duplicados dentro del lote, precios conservados, costos explícitos, precio automático, negativos/vacíos/límites, rechazo atómico de lote inválido, coincidencias ambiguas, asociación/creación explícitas, aislamiento de negocio, sesión anónima/revocada, firmas de los cuatro formatos, ausencia explícita de OCR.
- Confirmaciones en tareas paralelas reales: mismo identificador suma 20 sólo una vez; dos operaciones diferentes de +2 terminan en 24, sin pérdida de actualización. La repetición también se probó con contextos nuevos.
- Verificadores existentes DatabaseVerification, AiSettingsVerification (20 comprobaciones), NoticesVerification (17), MetricsVerification (20) y ProductionHostingVerification: correctos.
- AccountHttpVerification y DevelopmentAccessVerification: correctos mediante copias temporales con destino cambiado a 18188 y archivos/base temporales. Los originales no se redirigieron al servidor real. Incluyeron rutas protegidas, cookies, logout y CSRF. El verificador de publicación también comprobó el rechazo del acceso de desarrollo en Production. No se ejecutó Docker ni Railway.
- Navegador, copia SQLite temporal: Enter; varias cargas sin cerrar; campos realmente vacíos y foco Nombre tras éxito; error conservando valores; enteros inválidos/decimales/vacíos; normalización; 1,5 L separado de 2,25 L; edición 14→10; cancelación de ejemplo sin modificación; corrección de líneas; costo opcional vacío; confirmación mixta; persistencia tras recarga; archivo inválido rechazado; selección de PDF real de prueba y transcripción manual confirmada; error de revisión y desaparición del error al corregir; elección explícita «Crear nuevo».
- Responsive real: escritorio, tablet 768px y móvil 375px; filas en tarjetas, inputs utilizables y sin desbordamiento horizontal.
- Límites verificados honestamente: la cámara física de un teléfono no se probó (se preparó el control nativo); documentos ilegibles/no detectados no pueden probarse con OCR real porque no hay lector conectado. Las confirmaciones duplicadas/concurrentes se verificaron en el servicio y SQLite, no mediante automatización de doble click del navegador. Documentos con bytes distintos (otra foto de la misma factura) no se deduplican semánticamente.
- Sólo se migraron bases temporales de pruebas. El puerto 7188, la instancia de Visual Studio y la base de negocio real no se usaron. La migración aditiva queda lista para el mecanismo de inicio existente.
