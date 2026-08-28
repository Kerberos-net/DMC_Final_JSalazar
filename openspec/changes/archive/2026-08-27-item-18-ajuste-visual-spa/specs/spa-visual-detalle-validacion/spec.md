# Delta for spa-visual-detalle-validacion

Revises archived `2026-08-24-diseno-visual-spa-item-12`. Conforms the detalle-validación layout to the ratified handoff. Functional logic (#12) unchanged except the `factura-form` fields covered in `pantalla-detalle-validacion`.

## ADDED Requirements

### Requirement: Page header with back action, title, estado pill, and top-right actions

The system MUST render a page header row above the document/form split containing: a back affordance ("← Volver"), a title `{tipoComprobante} - {numero} - {proveedor}`, an estado pill, and the "Guardar avance" and "Validar" actions aligned top-right.

#### Scenario: Header composition

- GIVEN a factura detail screen loads
- WHEN the header renders
- THEN it shows the back affordance, the composed title, the estado pill, and both actions positioned top-right

#### Scenario: Estado pill reflects the real estado

- GIVEN a factura with estado "Pendiente"
- WHEN the header pill renders
- THEN it uses the "Pendiente" chip token (accent blue per the ratified exception), driven by the real estado value, not hardcoded

### Requirement: Indicator banners rendered above the split

The system MUST render up to three full-width indicator banners between the page header and the document/form split — never nested inside `factura-form`: (1) duplicado — strong amber, blocking; (2) proveedor P00000 — accent-blue informational, blocking; (3) tipo de cambio faltante — strong red, showing "Se muestra 0.00". Each banner appears only when its underlying `FacturaRespuesta` condition is true.

#### Scenario: Duplicate banner placement and tone

- GIVEN `FacturaRespuesta.PosibleDuplicado` is true
- WHEN the screen renders
- THEN a strong amber banner appears above the split, outside `factura-form`

#### Scenario: P00000 banner is informational in tone but blocking in effect

- GIVEN `FacturaRespuesta.EsProveedorGenerico` is true
- WHEN the screen renders
- THEN an accent-blue informational-styled banner appears above the split, and the "Validar" action is disabled (see `pantalla-detalle-validacion`)

#### Scenario: TC-faltante banner

- GIVEN the factura is foreign-currency and no compra tipo de cambio is available
- WHEN the screen renders
- THEN a strong red banner appears above the split stating the shown value is 0.00

### Requirement: Document/form split ratio is static 42 / 58

The system MUST render the document viewer at a fixed 42% width on the left and the form at flex:1 on the right, aligned to the top, with the viewer NOT sticky.

#### Scenario: Split proportions

- GIVEN the detalle screen renders on a wide viewport
- WHEN the layout is measured
- THEN the viewer occupies ~42% width, the form fills the rest, and the viewer does not stick on scroll

### Requirement: "Fecha de corte contable" stays adjacent to the asiento block

The system MUST keep the "Fecha de corte contable" period control adjacent to the asiento (`asiento-lineas`) block, not in the page header (user decision 5.1).

#### Scenario: Corte-contable control placement

- GIVEN the detalle screen renders
- WHEN the "Fecha de corte contable" input is located
- THEN it sits with the asiento block, not in the header actions area

### Requirement: `asiento-lineas` renders as a tabular Debe/Haber grid with a total row and cuadre pill

The system MUST render `asiento-lineas` as a column-aligned table (Cuenta / Debe / Haber), with Debe and Haber right-aligned using the tabular-nums token, a Total row summing each column, an "+ Agregar línea" accent-text link, and a cuadre pill reflecting the `cuadre` value already computed in `detalle-page.ts`.

#### Scenario: Column alignment and total

- GIVEN an asiento with multiple líneas
- WHEN `asiento-lineas` renders
- THEN Debe/Haber digits align vertically and a Total row shows the column sums

#### Scenario: Cuadre pill state

- GIVEN `detalle-page` computes `cuadre` as balanced
- WHEN the pill renders
- THEN it shows the balanced state; when unbalanced it shows the unbalanced state, both using the pill radius token

## MODIFIED Requirements

### Requirement: Blocking indicators use the strong alert token

The system MUST render blocking conditions with a strong, solid-fill indicator and MUST prevent the "Validar" action's affordance from appearing available while unresolved:

- duplicate invoice → strong amber banner (above the split)
- unregistered provider P00000 → accent-blue informational-styled banner (above the split), still hard-blocking "Validar" per the ratified exception
- tipo de cambio faltante → strong red banner (above the split)

(Previously: duplicate and P00000 both rendered as strong amber indicators inside `factura-form`; no dedicated TC-faltante indicator; banners were not relocated above the split.)

#### Scenario: Duplicate invoice indicator

- GIVEN a factura is flagged as a duplicate
- WHEN the screen renders
- THEN a strong amber banner appears above the split and "Validar" is disabled

#### Scenario: Unregistered provider P00000 indicator

- GIVEN a factura references provider P00000
- WHEN the screen renders
- THEN an accent-blue informational-styled banner appears above the split and "Validar" is disabled

### Requirement: Informational indicators use the subtle alert token

The system MUST render informational conditions with the subtle alert token (thin border or icon, no solid background). OCR fields not extracted MUST be highlighted per-field: `factura-form` MUST apply `.campo--resaltado` to each individual field whose value was not OCR-extracted, driven by real data — not as a single generic sentence.

#### Scenario: OCR field not extracted is highlighted per field

- GIVEN specific factura fields were not extracted by OCR
- WHEN `factura-form` renders
- THEN each such field individually carries `.campo--resaltado`, and fields that were extracted do not

#### Scenario: Unverified affectation

- GIVEN an afectación is pending confirmation
- WHEN `asiento-lineas` renders the line
- THEN it uses the subtle alert token, without a solid background

### Requirement: Component CSS budget compliance

Each of `detalle-page`, `factura-form`, `asiento-lineas`, `visor-documento`, `conflicto-banner`, and `historial-correccion` MUST keep its component stylesheet under Angular's `anyComponentStyle` budget thresholds in `angular.json`. Shared color/typography/radius/elevation rules MUST live in `styles.css` tokens; component CSS is layout/composition only.
(Previously: same rule over a smaller component set and before radius/elevation tokens.)

#### Scenario: Build-time budget check per component

- GIVEN any in-scope component's stylesheet
- WHEN the Angular build runs budget checks
- THEN it does not trigger the `anyComponentStyle` warning or error threshold
