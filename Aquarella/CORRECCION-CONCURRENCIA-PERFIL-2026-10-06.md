# Corrección de concurrencia de Perfil — 6 de octubre de 2026

Continúa el escenario reproducido en `AUDITORIA-PERSISTENCIA-2026-10-05.md`. Ese informe se conserva como evidencia histórica. Esta corrección cierra la sobrescritura de campos diferentes de Perfil allí documentada; no modifica los otros hallazgos o el alcance de la auditoría.

## 1. Causa y comportamiento anterior

`BusinessProfileStore.SaveAsync` enviaba una copia completa del formulario. El adaptador la entregaba a `BusinessData.SaveProfileAsync`, que ejecutaba `CurrentValues.SetValues(profile)` sobre el perfil del negocio. Los valores viejos de campos no editados se consideraban parte del guardado y sobrescribían lo persistido por otra pestaña.

Se reemplazó la prueba que documentaba ese problema por una regresión que exige conservar ambos cambios. Antes de corregir código, falló exactamente en `Stale tab: Name + Phone both survive`, pasando por stores, servicios y SQLite reales. No se cambió un test para aceptar el fallo: se cambió su expectativa por la política solicitada y luego se corrigió la implementación.

## 2. Estrategia y estado original

Perfil conserva una copia independiente del modelo cuando termina de cargar. Al guardar entrega **original + editado**. El store toma snapshots, asegura haber cargado su estado original, y el contrato del adaptador exige ambos modelos. El servicio de persistencia también exige el original: ya no hay un guardado de formulario completo sin esa información.

`BusinessProfileChanges`, específico de este modelo, compara sus 19 campos editables de texto. No es un framework genérico ni permite seleccionar IDs/propietarios. Para los colores HEX compara sin distinguir casing; `#ABCDEF` y `#abcdef` no son un cambio de color. `null` y cadena vacía se consideran representaciones equivalentes de un campo vacío. El resto se compara de forma ordinal: no se recortan o reinterpretan nombres, URLs, descripciones o teléfonos.

El servicio deriva el negocio autenticado, abre una transacción SQLite y carga su perfil actual. Sobre esa versión aplica únicamente las diferencias original/editado; valida el resultado y guarda. EF modifica las columnas efectivamente distintas, además de `UpdatedAt`. Lectura, combinación, validación y commit quedan juntos para que dos escrituras simultáneas no realicen la combinación sobre versiones incompatibles.

No hay locks de edición, bloqueos de formulario nuevos, tablas, versiones, migraciones ni interfaces de conflicto. La transacción es una operación breve de persistencia, no un bloqueo mantenido mientras el usuario edita.

## 3. Política aplicada

- **Campos distintos:** se combinan con los valores actuales; ambos sobreviven, aunque el segundo formulario esté desactualizado.
- **Mismo campo:** gana el último guardado válido aplicado por la base. No depende del momento de apertura de la pestaña.
- **Formulario sin diferencias:** conserva lo más reciente y devuelve el perfil actual.
- **Datos inválidos o fallo de escritura:** no se considera exitoso, no se actualiza el baseline del store ni se pierde el perfil persistido. Puede reintentarse.

El adaptador devuelve el perfil resultante y lo utiliza para presentar la identidad. Store y formulario actualizan su copia original después del éxito: una segunda edición no vuelve a enviar como intencional un valor viejo que ya fue combinado. No se agregó sincronización visual instantánea entre pestañas.

## 4. Campos y relaciones

La comparación cubre nombre, logo, descripción, colores primario/secundario/terciario, teléfono, email, sitio web, Instagram, Facebook, TikTok, X, LinkedIn, YouTube, rubro y horario.

No hay una regla actual que obligue a editar dos de esos campos juntos. Todos los cambios solicitados sí se confirman como una unidad validada. Perfil sigue sin editar colores desde su pantalla: sus tres valores se mantienen iguales a su original para no considerarlos cambios. Configuración de página continúa utilizando el mismo store; no se modificó su pantalla ni su lógica.

## 5. Validación e aislamiento

Se mantienen las validaciones del formulario y `BusinessProfileStore.IsValid`. Se valida también el modelo editado en `BusinessData` y el perfil combinado antes de escribir. Los originales malformados pueden servir como baseline para una corrección explícita, pero el resultado persistido debe ser válido. No se reemplazan datos silenciosamente por defaults durante una lectura.

Se mantienen `RequireUserAsync`, validación de sesión, consultas por el negocio derivado del usuario y las correcciones de cuentas de la auditoría. No se reciben BusinessId/UserId en el DTO de Perfil. Un original copiado de B no permite seleccionar ni modificar la fila de B: se probó expresamente y el snapshot de B permaneció idéntico.

## 6. Pruebas específicas

`PersistenceVerification` ahora cubre:

| Escenario | Resultado |
|---|---|
| A Nombre → `Kiosco Central`, B Teléfono → `352222222`, ambos desde `Kiosco Pablo / 351111111` | PASS: nombre nuevo y teléfono nuevo. |
| Orden inverso: A Teléfono, B Nombre | PASS: ambos sobreviven. |
| A Nombre → Central, B Nombre → Norte | PASS: queda Norte. |
| Cambio posterior del mismo campo inválido | PASS: no reemplaza el último nombre válido. |
| `Task.WhenAll` con guardados reales de campos distintos | PASS: ambos sobreviven. |
| Colores, logo, descripción, contacto, redes, rubro y horarios desde modelos desactualizados | PASS: campos independientes conservados; casing HEX no sobrescribe el color reciente. |
| Vaciar explícitamente descripción/quitar logo | PASS. |
| Email inválido | PASS: rechazo, sin perder valores válidos. |
| Submit sin modificaciones desde modelo desactualizado | PASS: conserva lo reciente y devuelve nuevo baseline. |
| Trigger SQL que hace fallar UPDATE de Perfil | PASS: store y DB quedan intactos; reintento correcto. |
| Original tomado de otro negocio | PASS: B intacto. |

También pasaron las pruebas anteriores de ownership, IDs de productos/notas manipulados, cantidades concurrentes, límites numéricos, rollback de carga, recibos malformados, sesiones, cascadas, reinicio por procesos nuevos y backup/restauración.

## 7. Dos pestañas reales

Se publicó una copia Release en TEMP y se inició únicamente una instancia ficticia en `https://localhost:18192`, con una copia coherente del backup de prueba y cuentas A/B. No se usó la DB real ni los servidores abiertos en 7188/18188.

Se abrieron dos pestañas de Perfil con circuitos reales. Ambas cargaron `Kiosco Pablo / 351111111`. A guardó `Kiosco Central`; B conservaba el nombre viejo, cambió solo teléfono y guardó sin refrescar. Después se recargó Perfil y se consultó SQLite en modo de solo lectura: **`Kiosco Central / 352222222`**, mientras B seguía sin modificaciones.

Se comprobó asimismo desde ambas pestañas que un segundo cambio del mismo nombre gana: A guardó Sur y luego B guardó Norte; la lectura de SQLite confirmó Norte con el teléfono conservado. La prueba exacta Central/teléfono se repitió después para guardar evidencia.

La captura de accesibilidad oculta el contenido de campos tipo teléfono, por lo que la comprobación numérica se hizo sobre la DB ficticia, no inferida de esa captura. Consolas de ambas pestañas sin warnings/errores y logs del servidor vacíos a nivel Warning. Se cerraron ambas pestañas y únicamente el PID de prueba; los procesos del usuario no se detuvieron.

Evidencia:

- DB/logs: `%TEMP%\aquarella-profile-tabs-c2ba823e2d924f679ee9f5bde1c46a89`.
- Captura: `C:\Users\pablo\.codex\visualizations\2026\10\03\01a0ff33-ebf7-7311-83ec-63cdf615538d\perfil-concurrencia-verificada.jpg`.

## 8. Regresiones y build

Pasaron **9 verificadores**: PersistenceVerification, DatabaseVerification (incluido reintento de carga de Perfil), ReliabilityVerification (Precios concurrente, parsing es-AR, recuperación y desconexión), AiSettingsVerification, StockIntakeVerification, NoticesVerification, MetricsVerification, MetricsPresentationVerification y ProductionHostingVerification (reinicio real con persistencia/cookies).

Pasaron ambas pruebas JavaScript existentes: `metrics-motion.test.mjs` y `legacy-recovery.test.mjs`. AccountHttpVerification y DevelopmentAccessVerification no se volvieron a ejecutar en esta corrección; habían pasado en la auditoría anterior y no se modificaron ahora.

Publish Release correcto. Build final del proyecto:

```powershell
dotnet build --artifacts-path "$env:TEMP\aquarella-data-audit-build"
```

**0 errores, 0 warnings.** La salida aislada evita interferir con el ejecutable que el usuario tiene abierto desde Visual Studio. No se cambiaron puertos, launchSettings ni configuración. `git diff --check` sin errores; avisos LF/CRLF son de la configuración existente de Git, no warnings del build.

## 9. Archivos de esta corrección y Git

Aplicación:

- `Aquarella/Services/BusinessProfileChanges.cs` — nuevo combinador específico de campos editados.
- `Aquarella/Services/BusinessData.cs` — guardado con original obligatorio, combinación/validación/transacción; conserva las correcciones previas de validación y recibos.
- `Aquarella/Services/BusinessProfileStore.cs` — baseline real y actualización al resultado persistido.
- `Aquarella/Services/IBusinessProfilePersistence.cs` — contrato original/editado y resultado.
- `Aquarella/Services/DatabaseBusinessProfilePersistence.cs` — adapta ese contrato y presenta el resultado combinado.
- `Aquarella/Components/Pages/Perfil.razor` — solo código de carga/guardado; snapshot original y actualización posterior, sin cambios de markup/diseño/textos.

Verificación/documentación:

- `tools/PersistenceVerification/Program.cs` — regresiones nuevas y adaptación de preparación de fixtures al contrato obligatorio.
- `tools/DatabaseVerification/Program.cs` — adaptación de firma del fake de persistencia; el test de reintento se mantiene.
- `CORRECCION-CONCURRENCIA-PERFIL-2026-10-06.md` — este informe.

Rama `main`, HEAD `7ac5d11`. Sin commit, push, staging, pull, merge/rebase ni cambio de rama. Se conservaron todos los cambios previos, reportes y siete PNG. El estado conjunto contiene diez archivos versionados modificados, los archivos nuevos de auditoría/verificación y esta corrección; las DB/logs/salidas de build quedaron exclusivamente en TEMP. No se modificó el código de Precios, autenticación, Stock, Agenda, Métricas, Avisos, IA, tutorial ni esquema en esta tarea.
