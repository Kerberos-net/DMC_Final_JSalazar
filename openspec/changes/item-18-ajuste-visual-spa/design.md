# Design: item-18-ajuste-visual-spa (BACKLOG #18 — Ajuste visual del diseño SPA)

## Technical Approach

Proposal Approach 2. Four layers, in dependency order: (1) a **two-tier token layer** in
`styles.css` — a private color ramp holding every literal, plus semantic aliases that name *roles*;
(2) a **machine-checked palette guard** that parses `styles.css` so the WCAG spec can actually go
RED; (3) **template restructure** of the two in-scope screens, moving indicator banners up to the
container; (4) a **small additive .NET delta** making `TipoComprobante`/`Numero` PATCH-editable.
Component CSS stays layout-only and outside `@layer` (existing architecture, unchanged).

## Architecture Decisions

### D1 — Ratified accent reuse via a private ramp + role aliases

| Option | Tradeoff | Decision |
|---|---|---|
| One `--accento` used in 3 places | Cheapest; role intent invisible, a reviewer "fixes" it | ✗ |
| Three unrelated hex literals | Greppable but drifts | ✗ |
| Private ramp + 3 semantic aliases | +8 lines; intent named, one place to change | ✓ |

```css
/* ramp — the ONLY place a blue literal exists (ratified exception, decision 1) */
--azul-600: #0071e3; --azul-700: #0a63c9; --azul-400: #409cff;
/* roles — deliberately the same hue in three roles; do NOT collapse without re-ratification */
--accento: var(--azul-600);            /* primary fill  */
--accento-texto: var(--azul-700);      /* links / accent text */
--estado-pendiente-ink: var(--azul-700);
--info-generico-ink: var(--azul-700);  /* P00000 banner */
```
`rg -- '--azul-600'` enumerates every role; the exception is documented at the ramp, not scattered.

### D2 — The WCAG guard must read `styles.css`, or Strict TDD is theatre

`contraste.spec.ts` today asserts hex *literals*, so a token change can never make it RED. Add pure
`src/app/shared/paleta.ts`: `leerTokens(css): Map<string,string>` + `componer(rgba, fondoHex): hex`
(alpha compositing for the new translucent hairlines). New `paleta.spec.ts` does the `node:fs` read
of `src/styles.css` and feeds parsed values into `contraste()`. Parser pure, I/O in the spec —
mirrors ADR 0019 on the SPA side. RED sequence: assert `--accento` exists → fails (token absent) →
add token → GREEN.

**Rejected**: a `PALETA` TS constant as source of truth (CSS would still hardcode; drift persists).

### D3 — AA corrections to the handoff palette (evidence, not preference)

Computed with the project's own `contraste()`. Two handoff values fail AA **as text**; both are
fixed by *role-splitting*, never by changing the visual:

| Pair | Ratio | Verdict |
|---|---|---|
| `#ffffff` on `#0071e3` (button fill) | 4.75 | ✓ |
| `#0a63c9` on `#f5f5f7` / `#ffffff` | 5.14 / 5.59 | ✓ |
| `#0a84ff` on `#1c1c1e` | 4.66 | ✓ |
| **`#0a84ff` on `#2c2c2e`** (raised surface, dark) | **3.82** | ✗ → use `--azul-400 #409cff` (4.92 / 6.01) |
| **`#0071e3` on pendiente tint `#e6f1fc`** | **4.15** | ✗ → chip ink = `--azul-700` (4.89) |
| `#86868b` on `#f5f5f7` (`--texto-terciario`) | 3.33 | ✗ as text → non-text/decorative role only |

Ratified decision 5 said dark `--accento-texto` = `#0a84ff`. It fails on the second dark surface.
`#409cff` is a handoff-native value (dark "pendiente"), so no color is invented. **Flagged for
confirmation, not silently changed.**

### D4 — Indicator banners move to a new presentational component

`factura-form` loses `esBloqueante`/`esInformativa`; new `detalle/ui/indicadores-factura/` renders
the three full-width banners above the split. Container stays thin (existing container/presentational
split); banner DOM tests stay component-scoped instead of migrating into the container spec.

### D5 — Validar gate composes as a named list, server stays authoritative

```ts
readonly bloqueosValidar = computed<readonly string[]>(() => { /* DUPLICADO | PROVEEDOR_GENERICO */ });
readonly puedeValidar = computed(() => this.bloqueosValidar().length === 0);
```
`[disabled]="!puedeValidar()"`. The SPA gate is defence-in-depth only: `ServicioDeFacturas` already
returns 409 `DuplicadoNoResuelto` / `ProveedorGenericoNoResuelto`. Handoff ack-checkbox not adopted.

### D6 — TC indicator watches **venta**, not compra (ADR 0018 pt.1)

The engine converts the pasivo at TC **venta**. The red "tipo de cambio faltante" indicator fires on
`factura().moneda !== 'PEN' && asiento()?.tipoCambioVenta === null` — the rate the engine actually
uses. `fact.TipoCambio.Compra` is unprojected reference data requiring a new `IUnidadDeTrabajo` read;
**deferred entirely**. The read-only field is labelled "Tipo de cambio (venta)".

## Data Flow

```
GET /api/facturas/{id}         ─┐
GET /api/facturas/{id}/asiento ─┼→ DetallePage signals ─→ indicadores-factura (banners, above split)
                                │        │                └→ bloqueosValidar → [disabled] Validar
                                │        └→ factura-form  ─(cambios)→ borradorFactura
                                │        └→ asiento-lineas (per-línea, eager)
   "Guardar avance" ────────────┴→ PATCH /api/facturas/{id}  (+ tipoComprobante, numero)
```

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNetWeb/src/styles.css` | Modify | Ramp + role aliases, 4 surfaces, radii 8/12/16/20, shadow scale, hairlines, Segoe integer scale |
| `src/app/shared/paleta.ts` | Create | Pure `leerTokens` + `componer` (alpha compositing) |
| `src/app/shared/paleta.spec.ts` | Create | Reads `styles.css`, asserts token presence + AA per pair, both themes |
| `src/app/shared/contraste.ts` / `.spec.ts` | Modify | Accepts composited values; pair table driven by parsed tokens |
| `src/app/app.html` / `app.css` | Modify | Header/marca; theme control stays `<select>` (decision 5) |
| `login/feature/login-page/*` | Modify | GF badge, subtitle, placeholder inputs, full-width button, footer |
| `detalle/feature/detalle-page/*` | Modify | Page header + back, top-right actions, 42%/flex:1 split (visor no longer sticky), banners hoisted, `fecha-corte-contable` next to asiento block, `bloqueosValidar` |
| `detalle/ui/indicadores-factura/*` | Create | 4 files: duplicado (amber), P00000 (accent), TC faltante (red) |
| `detalle/ui/factura-form/*` | Modify | 2-col field grid, per-field `.campo--resaltado`, banners removed |
| `detalle/ui/asiento-lineas/*` | Modify | Tabular grid, total row, "+ Agregar línea" accent link, cuadre pill |
| `detalle/ui/{visor-documento,conflicto-banner,historial-correccion}/*` | Modify | Token/radius/shadow follow-through |
| `detalle/models/factura.model.ts` | Modify | `tipoComprobante?`/`numero?` on `CorreccionFacturaRequest` |
| `detalle/models/asiento.model.ts` | Modify | `basePEN`/`igvPEN` (PR6 only) |
| `SmartNet.Api/FacturaEndpoints.cs` | Modify | 2 trailing `= null` params on `CorreccionFacturaRequest` + `ACorreccion()` |
| `SmartNet.Facturacion.Core/CorreccionFactura.cs` | Modify | 2 trailing optional params |
| `SmartNet.Facturacion.Core/ValidacionDeCorreccion.cs` | Create | Pure guard (no DB/HTTP/clock — ADR 0019) |
| `SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modify | Guard call + 2 `AplicarCorreccion` blocks (audit per field) |
| `SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modify | **`GuardarFacturaAsync` UPDATE omits both columns today** — add SET + 2 params |
| `SmartNet.Api/AsientoEndpoints.cs` | Modify | `AsientoRespuesta` += `BasePEN`, `IgvPEN` (PR6, already on `AsientoContable`) |

No versioned SQL: `TipoComprobante`/`Numero` already exist and `008_usuarios_y_permisos.sql` grants
object-level `UPDATE ON fact.Factura` (verified, not assumed). `glosa` has no column → deferred
(ADR 0016 would require a new script + rollback).

## Interfaces / Contracts

```csharp
public sealed record CorreccionFactura(
    string? ProveedorCodigo = null, /* … existing 6 … */ string? Afectacion = null,
    string? TipoComprobante = null, string? Numero = null);   // trailing, source-compatible

// pure — CHAR(2) NOT NULL and VARCHAR(20) are schema facts, validated in the core
public static class ValidacionDeCorreccion
{
    public static ResultadoComando? Validar(CorreccionFactura c);  // null = OK, else Conflicto
}
```
`null` means "untouched", so **`Numero` can never be cleared to NULL via PATCH** — accepted
limitation, consistent with every existing nullable field on this DTO.

## `factura-form` field grid — binding model

| Group | Fields | Source | Editable |
|---|---|---|---|
| Pure SPA binding | `monto` (`totalOrig`), `moneda`, `fechaEmision`, `proveedorCodigo` + picker | `FacturaRespuesta`, already in GET **and** PATCH | ✓ now |
| Needs .NET delta (PR5) | `tipoComprobante`, `numero` | in GET, **not** in PATCH | ✓ after PR5 |
| Read-only projection (PR6) | base imponible, IGV | `AsientoContable.BasePEN/IgvPEN` — **absent from `AsientoRespuesta`**, additive field needed | ✗ |
| Read-only, already projected | TC (venta) + SBS note | `asiento().tipoCambioVenta` | ✗ |
| Derived, no backend | mes / día contable | `computed` over `asiento().fechaContable` | ✗ |
| Deferred | `glosa` | no column anywhere | — |

Money formatting: 2 decimals everywhere via one shared pure helper; never 3 (CONVENTIONS.md).

## Testing Strategy (Strict TDD — RED first, every slice)

| Layer | What | Runner |
|---|---|---|
| Palette guard | token exists in `styles.css`; every pair ≥ AA floor in both themes; `componer()` alpha math | `ng test` (vitest/jsdom) |
| Component DOM | login structure; banners render **in detalle-page, not factura-form**; Validar `disabled` for duplicado, P00000, and both; each new field renders + emits `cambios`; `.campo--resaltado` bound to real data; cuadre pill; derived mes/día | `ng test` |
| Core (no infra) | `AplicarCorreccion` emits one audit row per changed field for `TipoComprobante`/`Numero`; resend of same value audits nothing; `ValidacionDeCorreccion` rejects blank / non-2-char / >20 | `dotnet test` |
| API contract | `PATCH` with `tipoComprobante`/`numero` returns 200 and the GET reflects it; 7-arg positional construction still compiles | `dotnet test` |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The API delta is an additive DTO field, not a new process boundary.

## PR sequencing (400-line budget, `ask-on-risk`)

| PR | Scope | Est. lines | Depends on |
|---|---|---|---|
| 1 | Token layer + palette/WCAG guard | ~480 ⚠ | — |
| 2 | Shell header + login-page | ~180 | 1 |
| 3 | detalle-page restructure + `indicadores-factura` + asiento-lineas | ~380 | 1 |
| 4 | factura-form field grid (zero-backend fields) | ~300 | 3 |
| 5 | .NET delta + `tipoComprobante`/`numero` binding | ~280 | 4 |
| 6 | *(conditional)* base/IGV read-only projection | ~120 | 4 |

`Decision needed before apply: Yes` (PR1 ~480 > 400) · `Chained PRs recommended: Yes` ·
`400-line budget risk: High`. PR1 can split into 1a (`styles.css`) / 1b (guard) if the reviewer
prefers, but they are only meaningfully reviewable together — the guard is what proves the palette.
Feature Branch Chain: PR1 → tracker; each later PR targets its predecessor.

## Migration / Rollout

No data migration, no schema change, no feature flag. Every PR independently revertible; PR5's DTO
fields are optional and additive, so reverting the SPA does not break the API and vice versa.

## Open Questions

- [ ] Dark `--accento-texto`: confirm `#409cff` over ratified `#0a84ff` (3.82:1 on `#2c2c2e` fails AA).
- [ ] Editing `TipoComprobante`/`Numero` changes the duplicate identity `(RucProveedor, TipoComprobante, Numero)`, but `PosibleDuplicado` is a **stored** column written at ingestion and is not recomputed — the banner goes stale until re-ingestion. Recompute on PATCH is a domain rule change; out of #18 unless ratified.
- [ ] `base imponible` / `IGV` editability (REGLAS.md normative) — read-only in PR6 by decision 3; raise separately.
- [ ] "TC compra" display: deferred (needs a new reference-data read; ADR 0018 makes venta the operative rate). Confirm the "(venta)" label is acceptable against the handoff.
