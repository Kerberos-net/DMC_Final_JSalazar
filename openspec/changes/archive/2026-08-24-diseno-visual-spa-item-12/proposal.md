# Proposal: Diseño visual para SPA — login y detalle-validación (BACKLOG #12, reabierto)

## Intent
El ítem #12 "Detalle y validación" ya está archivado y funcionalmente completo: la SPA Angular
tiene login y la pantalla detalle-validación con toda su lógica (guardar avance, validar,
conflicto 412, líneas de asiento) conectada al API. Pero el diseño visual nunca se implementó —
`styles.css` y todos los `.css` de componente están vacíos, sin ninguna dependencia de UI
instalada. El resultado es HTML plano sin estilo, inutilizable para el trabajo diario real de
revisión de facturas que `DESIGN_BRIEF.md` describe (lotes de 10-50 facturas/día, necesidad de
distinguir de un vistazo qué requiere atención).

Este cambio traduce `DESIGN_BRIEF.md` a una implementación real para las dos pantallas ya
construidas. Durante `sdd-design` se detectó que dos elementos ya diseñados (panel de historial de
corrección, indicadores de duplicado/afectación) no tienen hoy una superficie de lectura que los
exponga en el detalle de factura — el usuario amplió el alcance para incluir la API mínima (.NET)
que los expone, sin agregar reglas contables nuevas (ver "Investigación" abajo).

## Scope

### In Scope
- Tokens de diseño en `SmartNet/spa/src/styles.css`: paleta semántica (color por estado/nivel de
  alerta), tipografía, espaciado, alineación tabular para montos/fechas — para **tema claro y
  tema oscuro** desde el inicio (decisión del usuario, no diferido).
- Estilos scoped por componente para: `login-page`, `detalle-page`, `factura-form`,
  `asiento-lineas`, `visor-documento`, `conflicto-banner`.
- Paleta de alerta en **dos niveles de énfasis** sobre la misma familia semántica:
  - **Bloqueante** (duplicado, proveedor P00000 sin registrar): color de alerta fuerte, borde y
    fondo sólidos — impide validar hasta resolverse.
  - **Informativo** (campos no extraídos por OCR, afectación no verificada pendiente de
    confirmación): misma familia de color, tratamiento sutil — borde fino o icono, sin fondo
    sólido.
- Distinción visual explícita entre conflicto de edición (412) y error de validación (422):
  colores e iconos **distintos**, no solo copy distinto. 412 usa un color propio neutro/informativo
  (problema de concurrencia, no del dato); 422 usa el color de error de validación existente.
- Historial de corrección: panel expandible/colapsable junto al asiento, **colapsado por
  defecto** — sin protagonismo visual, per brief. Se descarta el tratamiento tipo tooltip.
- Selector de tema (claro/oscuro) accesible desde la SPA — mecanismo mínimo necesario para que el
  tema oscuro sea usable, no solo declarado en tokens.
- Todos los tokens de color (ambos temas) cumplen **WCAG AA** como piso de contraste para texto y
  estados.
- Alineación con budgets de `angular.json` (`anyComponentStyle`: 4kB warning / 8kB error):
  reglas compartidas concentradas en tokens globales de `styles.css`, CSS por componente acotado
  a layout/composición específica.
- **API .NET — historial de corrección**: nuevo método de lectura sobre `fact.AuditoriaCorreccion`
  (hoy solo tiene escritura vía `IUnidadDeTrabajo.RegistrarAuditoriaAsync`) y endpoint que lo
  exponga para una factura/asiento, para poblar el panel `<details>` (campo, valor anterior, valor
  nuevo, cuándo) ya diseñado.
- **API .NET — indicadores en detalle**: extender `FacturaRespuesta` (`FacturaEndpoints.cs`) con
  `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`, `AfectacionMixta` — mismas
  columnas que `fact.Factura` ya persiste y que `GET /api/bandeja` ya lee (`SqlBandejaRepository`),
  simplemente no proyectadas hoy en la respuesta de detalle de factura.
- Campo de confirmación de afectación en `factura-form` si no existe ya, para que el usuario pueda
  confirmar explícitamente la afectación cuando `AfectacionMixta` es `null` (afectación no
  verificada), coherente con la acción de auditoría `CONFIRMACION_AFECTACION` ya existente.

### Out of Scope
- Bandeja/inbox (#13) y cualquier otra pantalla de `DESIGN_BRIEF.md` (registro de compra, panel
  de errores, configuración) — quedan fuera explícitamente, para un ítem futuro si se decide.
- Cualquier cambio a la lógica funcional, endpoints, o estructura de datos de #12 (ya archivado y
  probado E2E).
- Instalación de librerías de UI (Angular Material, Tailwind, u otra) — decisión ya tomada por el
  usuario: CSS propio + tokens, cero dependencias nuevas.
- Iconografía genérica de "IA" (chispas, robots) — per brief, el producto no vende IA.
- Persistencia de la preferencia de tema en backend — si se implementa selector, alcance mínimo es
  cliente (localStorage o `prefers-color-scheme`), sin nueva superficie de API.

## Decisions (resolved during this proposal, not deferred)

1. **Tema oscuro incluido desde el inicio.** El brief lo dejaba condicional ("si el prototipo lo
   permite"); el usuario lo pidió explícitamente en esta ronda. Los tokens de color se definen
   como pares claro/oscuro desde el primer commit, no como capa añadida después.
2. **Alerta en dos niveles de énfasis, misma familia de color.** Evita inventar una paleta nueva
   por indicador (duplicado, P00000, campos faltantes, afectación no verificada comparten familia
   semántica "alerta") mientras preserva la jerarquía real: lo que bloquea `Validar` debe
   distinguirse visualmente de lo que solo informa.
3. **412 y 422 con colores/iconos distintos**, no solo copy. Un conflicto de concurrencia
   ("alguien más lo cambió") y una violación de regla contable ("este dato viola una regla") son
   categorías de problema distintas para el usuario — un color de error único para ambas
   confundiría causa (concurrencia vs. dato) con la misma urgencia visual.
4. **Historial de corrección como panel expandible/colapsable, no tooltip.** Un tooltip no
   aguanta una lista de cambios (campo, valor anterior, valor nuevo, cuándo) de forma legible;
   panel colapsado por defecto cumple "no protagonismo visual" sin sacrificar trazabilidad.
5. **WCAG AA como piso de contraste**, en ambos temas, para todos los tokens de color semántico
   (no solo texto genérico) — dado que es una herramienta de uso diario intensivo, la legibilidad
   de estado no es negociable aunque no haya requisito de accesibilidad formal en el PRD.

## Investigación — ampliación de alcance a API .NET (confirmada por código, no supuesta)

El usuario pidió investigar si el historial de corrección y los indicadores de duplicado/afectación
ya tienen forma de leerse antes de asumir que hace falta lógica nueva. Trazado directo al código:

1. **Historial de corrección (`AuditoriaCorreccion`)**: `EntradaAuditoria` (`SmartNet.Facturacion.Core`)
   modela la fila y `IUnidadDeTrabajo.RegistrarAuditoriaAsync` la escribe — pero **no existe ningún
   método de lectura** en `IUnidadDeTrabajo` ni ningún endpoint que liste entradas de auditoría por
   factura/asiento. Es un gap de **lectura pura**: la tabla y los siete valores de `Accion`
   (`CK_AuditoriaCorreccion_Accion`) ya existen; falta el método de repositorio (SELECT) + el
   endpoint que lo proyecte. **No requiere ninguna regla contable nueva.**
2. **`PosibleDuplicado` / `AfectacionMixta` (afectación no verificada)**: `IndicadoresFactura` se
   calcula una sola vez en `CalculoDeIndicadores.Calcular` (`SmartNet.Inbox.Core`, item #13) y **se
   persiste como columnas directas de `fact.Factura`** (`EsProveedorGenerico`, `PosibleDuplicado`,
   `TieneCamposNoExtraidos`, `FechaEnDomingo`, `AfectacionMixta` — confirmado en
   `SqlBandejaRepository.ListarAsync`, líneas `SELECT ... f.EsProveedorGenerico, f.PosibleDuplicado,
   ...`). `FacturaRespuesta` (`FacturaEndpoints.cs`) ya expone `Afectacion` (texto) pero no estos
   booleanos. Es también un gap de **lectura pura**: las mismas columnas que `GET /api/bandeja` ya
   lee simplemente no están en la proyección de detalle. **No requiere cálculo nuevo, solo ampliar
   la query/DTO existente.**

**Conclusión**: ninguno de los dos puntos requiere lógica de dominio contable nueva — ambos son
proyección/lectura de datos que el sistema ya calcula y ya persiste. No hay decisión contable
pendiente que deba escalarse al usuario en esta fase.

## Capabilities

### New Capabilities
- `spa-design-tokens`: variables CSS globales en `styles.css` — color semántico (2 niveles de
  alerta + estados + 412/422), tipografía, espaciado, tabular-nums, para tema claro y oscuro,
  cumpliendo WCAG AA.
- `spa-theme-toggle`: mecanismo mínimo de selección de tema (claro/oscuro) en la SPA.
- `spa-visual-login`: estilos scoped de `login-page` siguiendo los tokens.
- `spa-visual-detalle-validacion`: estilos scoped de `detalle-page`, `factura-form`,
  `asiento-lineas`, `visor-documento`, `conflicto-banner`, incluyendo el panel de historial de
  corrección colapsable.

- `auditoria-correccion-lectura-api`: nuevo método de lectura en `IUnidadDeTrabajo`/repositorio
  (.NET, `fact.AuditoriaCorreccion`) + endpoint que expone el historial de una factura/asiento —
  read-only, sin nueva regla contable.

### Modified Capabilities
- `factura-respuesta-asiento-respuesta`: ampliar `FacturaRespuesta` con `EsProveedorGenerico`,
  `PosibleDuplicado`, `TieneCamposNoExtraidos`, `AfectacionMixta` — mismas columnas que
  `fact.Factura` ya persiste, proyección adicional no destructiva (additive, no rompe el contrato
  existente).
- `pantalla-detalle-validacion`: `factura-form` incorpora el campo de confirmación de afectación
  (cuando `AfectacionMixta` es `null`) y `asiento-lineas` consume el nuevo endpoint de auditoría
  para poblar el panel de historial ya diseñado — sin cambio a la lógica de guardar avance/validar.

## Approach
CSS propio con variables (`:root { --color-... }`) en `styles.css`, aprovechando que Angular View
Encapsulation ya aísla el CSS por componente — no se requiere metodología BEM/utility adicional
para evitar colisiones. Tema oscuro vía `prefers-color-scheme` + override manual (atributo/clase
en `<html>` controlado por el toggle), redefiniendo el mismo set de variables semánticas por tema
en vez de duplicar reglas de componente. Los dos niveles de alerta (bloqueante/informativo) se
modelan como dos tokens derivados de la misma familia de color (p. ej. `--color-alerta-fuerte` /
`--color-alerta-sutil`), no como colores independientes, para que "un color = un significado" del
brief se mantenga verificable. 412 y 422 obtienen cada uno su propio token semántico
(`--color-conflicto` / `--color-error-validacion`) para que el CSS de `conflicto-banner` y de
mensajes de error de `factura-form`/`asiento-lineas` no compartan clase visual.

Verificación de contraste: los pares de color por token (texto sobre fondo, en ambos temas) se
calculan/validan contra WCAG AA (ratio ≥ 4.5:1 texto normal, ≥ 3:1 texto grande/iconografía de
estado) antes de fijarse como token final.

**Backend (.NET)**: ambas ampliaciones siguen el patrón ya establecido en #12/#11 — métodos de
solo lectura sobre `IUnidadDeTrabajo` (nuevo `CargarAuditoriaAsync` o equivalente, siguiendo el
naming `CargarXAsync` ya usado para documentos/adjuntos), consultas SQL nuevas contra tablas
existentes (ningún cambio de esquema, ninguna migración), y endpoints delegadores finos en
`FacturaEndpoints.cs`/nuevo archivo de auditoría, con el mismo patrón RFC 9457 de
`ProblemasDeNegocio.cs`. `FacturaRespuesta` se amplía de forma aditiva (nuevos campos, ningún
campo existente cambia de forma) para no romper el contrato ya consumido por `factura-form`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/spa/src/styles.css` | Modified | Tokens de diseño globales: color (2 niveles alerta + 412/422 + estados), tipografía, espaciado, tabular-nums, claro/oscuro |
| `SmartNet/spa/src/app/app.css` | Modified | Estilos de shell/layout raíz si aplica (toggle de tema, contenedor global) |
| `SmartNet/spa/src/app/login/feature/login-page/*` | Modified | CSS scoped del login |
| `SmartNet/spa/src/app/detalle/feature/detalle-page/*` | Modified | CSS scoped del layout documento+formulario |
| `SmartNet/spa/src/app/detalle/ui/factura-form/*` | Modified | CSS scoped, resaltado de campos no extraídos, indicadores P00000/duplicado/tipo cambio |
| `SmartNet/spa/src/app/detalle/ui/asiento-lineas/*` | Modified | CSS scoped, alineación tabular, panel historial colapsable |
| `SmartNet/spa/src/app/detalle/ui/visor-documento/*` | Modified | CSS scoped del visor de PDF/imagen |
| `SmartNet/spa/src/app/detalle/ui/conflicto-banner/*` | Modified | CSS scoped, distinción visual 412 vs. 422 |
| `SmartNet/spa/angular.json` | Reviewed | Verificar budgets `anyComponentStyle` tras agregar CSS por componente |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs` | Modified | Nuevo método de lectura read-only para historial de auditoría por factura/asiento |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | Implementación SQL del nuevo método (SELECT sobre `fact.AuditoriaCorreccion` existente) |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlFacturacionStore.cs` (o equivalente de carga de factura) | Modified | Incluir `EsProveedorGenerico`/`PosibleDuplicado`/`TieneCamposNoExtraidos`/`AfectacionMixta` en la carga de `FacturaPersistida`, ya presentes en `fact.Factura` |
| `SmartNet/api/SmartNet.Api/FacturaEndpoints.cs` | Modified | Ampliar `FacturaRespuesta` con los indicadores; posible nuevo endpoint de auditoría |
| `SmartNet/spa/src/app/detalle/ui/factura-form/*` | Modified (funcional) | Campo de confirmación de afectación cuando `AfectacionMixta` es `null` |
| `SmartNet/spa/src/app/detalle/ui/asiento-lineas/*` | Modified (funcional) | Consumo del nuevo endpoint de auditoría para poblar el panel de historial |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| CSS por componente excede el budget de 8kB (`anyComponentStyle` error) | Med | Reglas compartidas centralizadas en tokens de `styles.css`; CSS de componente limitado a layout/composición específica, no a definir color/tipografía desde cero |
| Tokens claro/oscuro no cumplen WCAG AA en la práctica (colores "bonitos" pero de bajo contraste) | Med | Verificación de contraste explícita por par de token antes de fijarlo, no solo elección visual |
| Confusión visual entre 412 y 422 pese a colores distintos, si la ubicación en pantalla coincide | Low | `conflicto-banner` (412) y mensajes inline de campo (422) ya están en ubicaciones distintas por diseño funcional existente; solo se refuerza con color/icono |
| Alcance crece hacia bandeja/inbox u otras pantallas del brief sin pedirlo | Low | Capabilities de esta propuesta están acotadas explícitamente a login + detalle-validación; cualquier extensión requiere una propuesta nueva |
| Sin diseñador/mockups formales: paleta y tipografía exactas se deciden con criterio en sdd-design, no en esta propuesta | Med | `sdd-design` debe fijar la paleta exacta (valores hex/HSL) y justificar contraste WCAG AA antes de `sdd-tasks` |
| Ampliación de `FacturaRespuesta` cambia un contrato ya consumido por `factura-form` | Low | Cambio estrictamente aditivo (campos nuevos, ninguno existente se modifica/renombra); `sdd-spec` debe fijar el contrato exacto antes de tocar el DTO |
| Naming/ubicación exacta del nuevo método de lectura de auditoría no está fijado (`IUnidadDeTrabajo` vs. repositorio nuevo dedicado) | Low | Decisión de diseño para `sdd-design`, siguiendo el patrón `CargarXAsync` ya establecido |

## Rollback Plan
Cambios puramente de CSS/presentación sobre componentes ya funcionales — sin cambios de datos,
API ni contratos. Revertir es un `git revert` de los commits de estilos, sin efecto en la lógica
ya archivada de #12. El toggle de tema (si persiste preferencia en `localStorage`) no requiere
migración: su ausencia simplemente hace caer al tema por defecto (claro, o `prefers-color-scheme`).

## Dependencies
- BACKLOG #12 (archivado) — provee los componentes funcionales que este cambio viste visualmente.
- `DESIGN_BRIEF.md` — fuente de verdad de diseño para las 6 pantallas del producto; este cambio
  ejecuta su guía solo para login y detalle-validación.
- BACKLOG #11 (`AuditoriaCorreccion`, `EntradaAuditoria`) y BACKLOG #13 (`IndicadoresFactura`,
  `CalculoDeIndicadores`) — este cambio solo expone en lectura datos que ambos ítems ya calculan y
  persisten; no depende de trabajo pendiente de #13 (la bandeja ya está construida y funcional).
- Ningún ADR nuevo requerido; no se introduce infraestructura ni lógica de dominio nueva — ambas
  ampliaciones de API son proyección de datos ya persistidos (ver sección "Investigación").

## Success Criteria
- [ ] Login y detalle-validación son legibles y utilizables con estilo aplicado (no HTML plano).
- [ ] Un usuario puede distinguir de un vistazo: alerta bloqueante vs. informativa, conflicto 412
      vs. error de validación 422, sin leer el texto completo.
- [ ] Tema oscuro funcional y seleccionable, con los mismos tokens semánticos que el tema claro
      (ningún color "solo de un tema").
- [ ] Todos los tokens de color pasan verificación de contraste WCAG AA en ambos temas.
- [ ] Historial de corrección visible pero colapsado por defecto, sin competir visualmente con el
      formulario/asiento, poblado con datos reales de `fact.AuditoriaCorreccion` (no mock).
- [ ] Indicadores de duplicado y afectación no verificada visibles en el detalle con los mismos
      valores que ya calcula/persiste el dominio (paridad con `GET /api/bandeja`).
- [ ] Ningún archivo `.css` de componente excede el budget `anyComponentStyle` de `angular.json`.
