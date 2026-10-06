# Auditoría técnica de Aquarella

Fecha: 5 de octubre de 2026. Alcance: revisión técnica general, compilación, verificadores existentes y ejecución local aislada. Sin commit ni push.

## 1. Estado inicial

- Rama `main`, sincronizada con `origin/main`, commit `0f34f2d`.
- Ningún cambio inicial en archivos versionados. Siete capturas PNG locales sin seguimiento, conservadas sin modificaciones.
- Solución `Aquarella.slnx`: un proyecto web `Aquarella/Aquarella.csproj`, Blazor Interactive Server y Razor Pages sobre `net10.0`.
- Nueve proyectos de verificación ejecutables en `tools/`, fuera de la solución, y una prueba JavaScript de motion. No hay proyectos xUnit/NUnit/MSTest registrados.
- SDK instalado: .NET `10.0.401`; runtime ASP.NET Core `10.0.12`. Nullable habilitado. Sin `global.json`.
- EF Core Design y SQLite solicitados/resueltos en `10.0.12`; dependencias transitivas revisadas mediante `dotnet list package --include-transitive --no-restore`. Sin cambios de paquetes ni de referencias.
- Perfiles locales HTTP `5097` y HTTPS `7188`; certificado de desarrollo válido hasta febrero de 2027. La navegación HTTPS se comprobó sin desactivar validación de certificados.
- Configuración de desarrollo: SQLite en `App_Data`, propietario local `pablo`. OpenAI deshabilitado por defecto; las claves provienen de configuración segura/entorno. Producción usa la configuración de almacenamiento/proxy ya existente.

## 2. Build inicial

`dotnet build` finalizó correctamente: **0 errores y 0 warnings**. El primer intento en el entorno restringido no pudo leer la configuración NuGet del usuario; el mismo comando con los permisos necesarios pasó. Fue una restricción del entorno, no un error del código. No se cambió NuGet.Config.

## 3. Tests iniciales

Todos los verificadores existentes pasaron:

| Verificador | Resultado / cobertura |
| --- | --- |
| DatabaseVerification | PASS: cuentas, importación, persistencia, aislamiento, cantidades, precios y notas |
| AiSettingsVerification | 20 comprobaciones correctas |
| NoticesVerification | 17 comprobaciones correctas |
| MetricsVerification | 20 comprobaciones correctas |
| MetricsPresentationVerification | 64 comprobaciones correctas: formatos, valores exactos, accesibilidad, negativos y límites |
| StockIntakeVerification | PASS: cargas, preservación de precios, rollback, idempotencia, concurrencia y validación de documentos |
| ProductionHostingVerification | PASS: arranque HTTP local con proxy simulado, login, CSRF, cookies, aislamiento y reinicio |
| AccountHttpVerification | PASS: registro, email local, errores de login, recuperación, logout, rutas y rate limiting |
| DevelopmentAccessVerification | PASS: entrada de desarrollo sin duplicar usuarios/negocios ni alterar sus datos |
| metrics-motion.test.mjs | PASS: valores finales, timing, reduced motion, cancelación, intro y desmontaje |

`dotnet test Aquarella.slnx --no-restore` terminó sin errores, pero no sustituye estos verificadores: la solución no contiene un proyecto de tests convencional.

Las pruebas utilizaron SQLite y cuentas ficticias en ubicaciones temporales. Los verificadores HTTP de desarrollo, que tienen rutas y puertos fijos, se ejecutaron desde un directorio temporal con la estructura que esperan; no desde el directorio de datos real. No se modificó la base del negocio. El verificador de base de datos tiene además una comparación opcional de solo lectura con un backup antiguo; no llegó a ejecutarse en este recorrido.

## 4. Errores encontrados

Se reprodujo un fallo en `BusinessProfileStore.InitializeAsync`: después de una primera carga fallida, el servicio guardaba esa tarea fallida indefinidamente. Las llamadas posteriores volvían a lanzar la misma excepción sin consultar nuevamente al adaptador de persistencia.

La nueva prueba de regresión falló antes de corregirlo y pasó después. No se encontraron errores de compilación del proyecto.

## 5. Warnings encontrados

- Compilación inicial, final, verificadores y publish: **0 warnings**.
- En los recorridos de navegador no se capturaron warnings ni errores de consola.
- Logs de las copias aisladas de desarrollo: sin excepciones ni warnings registrados.
- Las respuestas 400, 404 y 429 provocadas por los verificadores son resultados esperados de pruebas negativas de CSRF, acceso de desarrollo en producción y rate limiting.
- No se realizó una auditoría de vulnerabilidades de paquetes ni de deployment público; no se afirma que esas áreas estén certificadas.

## 6. Bugs potenciales y riesgos a revisar

Estos puntos provienen de inspección del código; salvo el reintento de Perfil, no se reprodujeron como fallos en esta auditoría:

1. **Guardado concurrente de Precios.** `Precios.razor` serializa el guardado con un semáforo, pero encola referencias a objetos mutables. Un cambio posterior de un campo puede modificar el objeto antes de que una operación pendiente termine. Conviene probar escritura rápida, errores de guardado y edición del mismo producto desde dos pestañas antes de definir snapshots/versionado.
2. **Productos eliminados durante una edición.** `BusinessData.SaveProductAsync` y `SavePriceAsync` usan `SingleAsync`. Una eliminación concurrente produce una excepción tratada como fallo de almacenamiento, sin distinguir producto eliminado. No se agregaron nuevas reglas de conflicto.
3. **Perfil persistido inválido.** La carga actual descarta silenciosamente un perfil que no cumple validación y mantiene la identidad predeterminada. Revisar cómo informar corrupción y evitar que una edición posterior reemplace información que debería recuperarse. No se alteró esta política ni se inspeccionaron datos privados.
4. **Importación legacy con datos malformados.** Se validan los valores, pero colecciones o propiedades no anulables que lleguen explícitamente como `null` desde JSON pueden provocar excepciones antes de terminar la validación. Corresponde definir una política de recuperación en la futura auditoría de datos.
5. **Recargas duplicadas de Agenda.** Guardar/eliminar publica `NotesChanged`, que dispara una recarga, y `AgendaStore.SaveAsync/DeleteAsync` vuelven a llamar a `InitializeAsync`. El semáforo evita solapamiento, pero puede haber dos lecturas por acción. Es un problema de eficiencia; quitar una de las lecturas requiere verificar la garantía de actualización global.
6. **Ciclo de vida al navegar.** Hay callbacks de eventos que descartan la tarea de `InvokeAsync`, y referencias JS cuya liberación queda después de una llamada que puede fallar. Revisar navegación durante guardados, desconexión y reconexión con pruebas específicas antes de modificar la coordinación.
7. **Recursos de herramientas.** `AccountHttpVerification.Link` construye documentos JSON sin liberación explícita; los semáforos de Agenda y Precios tampoco se liberan explícitamente. No apareció un fallo en el recorrido; conviene revisar su ciclo de vida sin disponer recursos que todavía estén en uso.

## 7. Corrección automática

Se permitió reintentar la inicialización de Perfil cuando la tarea anterior terminó fallida o cancelada. Se conserva la tarea compartida cuando está en curso o finalizó correctamente. No se modifican los valores válidos, el formulario, el esquema, el adaptador de almacenamiento ni la autenticación.

Se agregaron tres comprobaciones de regresión: conservar la identidad tras un fallo, recuperar el perfil en el siguiente intento y no repetir lecturas después de una inicialización exitosa.

## 8. TODO / FIXME / HACK relevantes

No se encontraron marcadores literales `TODO`, `FIXME` ni `HACK` en código fuente de la app y herramientas, excluyendo `bin/obj`. Sí permanecen pendientes explícitos:

- Conversaciones muestra `Próximamente`; el chat de prueba tiene su ruta separada.
- Lectura automática/OCR de facturas: adaptador `UnavailableInvoiceExtractor`, todavía no conectado.
- Envío real de correo: `UnavailableAccountEmail` fuera de desarrollo; en desarrollo se utiliza un outbox local privado.
- Configuración de IA: memoria, historial, web y acciones siguen como futuras capacidades.
- Enlaces LinkedIn/GitHub/Portfolio del creador: URLs todavía sin completar.
- Comentario de HSTS en Program.cs: revisar en una futura tarea de hosting, no cambiar en esta auditoría.

## 9. Datos mock / hardcodeados

- Stock contiene un ejemplo manual explícitamente identificado como simulado: Coca Cola 2,25 L y Sprite 2,25 L. Solo se guarda si se confirma expresamente. Se conservó.
- Chat devuelve una respuesta simulada si OpenAI no está habilitado/configurado. No se hicieron llamadas de IA.
- Defaults de identidad y colores, catálogo de módulos y créditos son constantes intencionales de la interfaz, no datos falsos de negocio.
- El nombre `pablo` sigue en configuración/acceso de desarrollo y compatibilidad legacy; no se cambió autenticación.
- Cuentas, emails `example.test`, contraseñas de prueba y productos ficticios de `tools/` pertenecen a fixtures aislados.
- Puertos y rutas fijos en dos verificadores HTTP dificultan ejecutarlos en cualquier entorno. En esta revisión se respetaron mediante copias temporales; no se modificaron las herramientas por ese motivo.

## 10. Problemas importantes no tocados

No se implementaron soluciones de concurrencia, recuperación de datos corruptos, autenticación, permisos, correo real, OCR, IA ni despliegue. No se cambiaron migraciones, esquema de base de datos, Docker, Railway ni WhatsApp. Tampoco diseño, animaciones, colores, textos o navegación.

La ejecución de ProductionHostingVerification es local/nativa y usa un proxy simulado; no constituye una prueba de Docker/Linux ni de Railway público.

## 11. Recomendaciones por prioridad

1. **Alta:** prueba específica de concurrencia y recuperación de errores de Precios; comprobar edición en dos pestañas y eliminación mientras se guarda.
2. **Alta, siguiente auditoría de datos:** definir manejo explícito de perfiles/legacy malformados sin reemplazarlos por defaults, y comprobar recuperación y copias de seguridad.
3. **Media:** probar navegación y desconexión durante operaciones asíncronas; luego ajustar limpieza de JS y observación de tareas.
4. **Media:** llevar los verificadores a una ejecución automatizada reproducible: comando único, puertos/rutas parametrizados y resultados que una pipeline pueda recoger. Actualmente `dotnet test` no los descubre.
5. **Baja:** reducir las lecturas duplicadas de Agenda y revisar consultas repetidas de validación de sesión, manteniendo las garantías de seguridad.
6. **Baja:** completar documentación de los adaptadores pendientes y URLs del creador cuando se decida desarrollar esas funciones.

## 12. Archivos modificados

- `Aquarella/Services/BusinessProfileStore.cs`: corrección del reintento tras fallo/cancelación.
- `tools/DatabaseVerification/Program.cs`: regresión reproducible con un adaptador ficticio; las comprobaciones existentes se conservaron.
- `AUDITORIA-TECNICA-2026-10-05.md`: este informe.

## 13. Build final

`dotnet build`: **0 errores, 0 warnings**. También pasó el publish Release de la copia final aislada. `git diff --check` sin problemas de whitespace.

## 14. Tests finales y ejecución

Los seis verificadores de datos/presentación y la prueba JavaScript se repitieron después de la corrección y pasaron. La regresión de Perfil quedó cubierta. Los tres verificadores HTTP existentes también pasaron durante la auditoría.

Sobre la copia final se abrió Login, se entró con la cuenta ficticia de desarrollo y se recorrieron las ocho rutas del dashboard: Stock, Avisos, Conversaciones, Configuración de IA, Métricas, Agenda, Perfil y Precios. Se confirmó además que Precios, Métricas y Avisos terminaran de cargar sus estados vacíos. No se capturaron warnings/errores de consola ni excepciones del servidor en ese recorrido.

Se cerró la pestaña de prueba y únicamente los procesos creados para la auditoría. Los puertos temporales quedaron liberados; la instancia previa del usuario no se detuvo. Artefactos y logs de pruebas permanecen en directorios temporales, fuera de Git.

## 15. Git final

Rama `main` conservada; HEAD `0f34f2d`, sin nuevos commits ni push. Dos archivos versionados modificados y este informe nuevo, sin staging. Las siete capturas PNG iniciales siguen sin seguimiento y sin modificaciones. No se descartó ningún cambio del usuario.
