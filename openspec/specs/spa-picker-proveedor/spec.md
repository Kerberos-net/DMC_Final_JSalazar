# spa-picker-proveedor Specification

New capability pulled into BACKLOG #18: the functional proveedor picker for `factura-form`.
`factura-form` already emits a `buscarProveedor` output (added in PR4) but nothing handled
it before item #18. This spec defines the modal picker and its wiring, consuming
`api-catalogos-proveedores`.

## Purpose

Let a validator replace a factura's proveedor by searching the catalog in a modal dialog
and selecting a result, pushing the choice through the existing draft path — never a new
save contract.

## Requirements

### Requirement: `ProveedorService` data-access

The system MUST provide `ProveedorService` as `@Injectable({ providedIn: 'root' })` (ADR
0009), exposing search state through a private signal published via `asReadonly()` and
issuing requests with `firstValueFrom(http.get(...))` against
`GET /api/catalogos/proveedores`. No state-management library may be introduced. Search
input MUST be debounced before a request is issued. The service MUST live in a `catalogos`
data-access slice with a `proveedor.model.ts` type for `{ codigo, nombre, ruc }`.

#### Scenario: Debounced search issues one request

- GIVEN the user types "AC", "ACM", "ACME" within the debounce window
- WHEN the debounce settles
- THEN exactly one `GET /api/catalogos/proveedores?q=ACME` is issued (asserted with `HttpTestingController`) and the readonly signal holds the parsed results

#### Scenario: Pagination request

- GIVEN a result set with `hayMas=true`
- WHEN the user requests more
- THEN the service issues the next `pagina` and appends results to the signal

### Requirement: Modal picker dialog component

The system MUST provide a presentational picker dialog component rendering a debounced
search input, a result list (`nombre`, `codigo`, `ruc`), and a select action per row. It
MUST use the modal radius and elevation tokens from PR1 and MUST NOT introduce any new
design token (`contraste.spec.ts` / palette guard stay unaffected). It MUST support
keyboard navigation of the result list, `Enter` to select the focused row, `Escape` to
close, a focus trap while open, and appropriate `aria` roles/labels on the dialog. On
select it MUST emit the chosen `{ codigo }` (and `ruc` when applicable) and close; it MUST
NOT issue any PATCH itself.

#### Scenario: Select a result

- GIVEN the picker is open with results listed
- WHEN the user activates a row (click or keyboard `Enter`)
- THEN the component emits the selected `codigo` (and `ruc` if present) and closes

#### Scenario: Keyboard and accessibility

- GIVEN the picker is open
- WHEN the user presses arrow keys then `Escape`
- THEN focus moves within the trapped result list and `Escape` closes the dialog without a selection

#### Scenario: No new tokens

- GIVEN the picker styles
- WHEN the palette guard / `contraste.spec.ts` runs
- THEN it is unchanged and green (picker reuses existing modal tokens only)

### Requirement: Opened from `factura-form`, selection flows through `borradorFactura`

The picker MUST open in response to `factura-form`'s existing `buscarProveedor` output,
wired in `detalle-page`. On selection, `detalle-page` MUST push `{ proveedorCodigo }` (and
`rucProveedor` when applicable) into `borradorFactura` via the existing `onCambiosFactura`
path — the same path used by every other editable field. The picker MUST NOT define a new
save contract and MUST NOT PATCH directly; persistence still happens only via the existing
"Guardar avance" flow.

#### Scenario: buscarProveedor opens the picker

- GIVEN the detalle screen is rendered
- WHEN `factura-form` emits `buscarProveedor`
- THEN `detalle-page` opens the picker dialog

#### Scenario: Selection updates the draft, not the server

- GIVEN the picker emits a selected `codigo`
- WHEN `detalle-page` handles it
- THEN `borradorFactura` receives `{ proveedorCodigo }` (and `rucProveedor` if applicable) through `onCambiosFactura`, no PATCH is sent, and the value persists only on "Guardar avance"

### Requirement: Test coverage

`npx ng test --no-watch` MUST cover: `ProveedorService` debounce/single-request,
pagination, and response parsing with `HttpTestingController`; picker dialog DOM (input,
result list rendering), keyboard navigation, `Escape` close, focus trap, and select emit;
`detalle-page` wiring of `buscarProveedor` → open and selection → `borradorFactura` via
`onCambiosFactura`; and that `factura-form` still emits `buscarProveedor` unchanged.

#### Scenario: Suite runs green

- GIVEN the picker specs
- WHEN `npx ng test --no-watch` runs
- THEN every listed case passes and no new token assertion is added

## Out of Scope

- No direct PATCH from the picker; no new SPA save contract.
- No changes to the existing `borradorFactura` / `onCambiosFactura` shape beyond the `proveedorCodigo` / `rucProveedor` keys it already carries.
