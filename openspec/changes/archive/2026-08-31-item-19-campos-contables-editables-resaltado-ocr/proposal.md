# Proposal: Campos contables editables y resaltado OCR por campo (BACKLOG #19)

## Intent

Item #18 left `base imponible`, `IGV`, `glosa` and per-field OCR highlight read-only because making
them editable crosses into the accounting core. Today the detalle shows one invoice-wide
"campos no extraídos" boolean (coarse highlight), no base/IGV editing, no glosa, and
`PosibleDuplicado` is frozen at promotion so "corregir el número" never clears the §8 duplicate
gate. This change closes those gaps for `BORRADOR` invoices, faithfully to REGLAS §5–§10.

## Scope

### In Scope (4 slices, one PR)

1. **Per-field OCR highlight** — promote the worker's `camposNoExtraidos` list (8 canonical fields:
   tipoComprobante, numero, ruc, nombreProveedor, total, igv, moneda, fechaEmision) into a new
   `fact.Factura.CamposNoExtraidos` column; `FacturaRespuesta` exposes `camposNoExtraidos: string[]`
   additively (keep `tieneCamposNoExtraidos`); SPA highlights each field individually.
2. **Glosa editable** — new `fact.Factura.Glosa NVARCHAR(250) NULL`, free text; optional in
   `CorreccionFacturaRequest`, projected in `FacturaRespuesta`.
3. **`PosibleDuplicado` recompute** — recomputed synchronously inside `PatchAsync` whenever the
   identity triple (RucProveedor, TipoComprobante, Numero — matching `IX_Factura_Identidad` and §8)
   changes; SPA `guardarAvance` refetches the factura after PATCH.
4. **Base imponible / IGV editable** — writes the ORIGINAL trio (`TotalOrig` = base+IGV, `IgvOrig`),
   never the PEN projection (§6: basePEN stays derived, no adjustment line). New pure guards
   (base ≥ 0, `IgvOrig ≤ TotalOrig`, IGV = 0 for boleta/no-gravada). On edit, recompute ONLY the
   scalar `BasePEN/IgvPEN/NetoPEN` as a pure §5/§6 derivation of the invoice's own values.

### Resolved constraints (product-owner decisions)

- **Tipo de cambio is NOT user-editable** — stays SBS>MANUAL, frozen at confirm (ADR 0018).
- **New guard**: an invoice that *requires a rate lookup* (foreign currency, and — for NC `07` with
  internal reference — its inherited rate) with a zero/missing applicable tipo de cambio CANNOT be
  validated or confirmed (`POST /validar` and confirm reject). A PEN invoice converts at 1 with no
  lookup, so the guard never blocks local-currency invoices (§6). The guard does NOT block
  `PATCH` / "Guardar avance". This aligns the existing api-facturas 409 at `abrir`/`validar`.
- **VALIDADA invoice is immutable**: `PATCH` MUST reject base/IGV/glosa edits unless
  `estado = BORRADOR` (§9 — editing a confirmed asiento needs `reabrir` first). `numero` /
  `tipoComprobante` keep current post-validation behavior (audited `Correccion`); only the new
  contable fields get the stricter BORRADOR-only rule.

### Out of Scope / Non-goals

- Wiring `ComposicionDeAsiento.Componer` into the pipeline; regenerating or touching the hand-built
  asiento líneas (item #12 UX). Base-vs-líneas divergence stays caught by existing `validar` §7
  invariants.
- Fixing the latent gap where `Componer` is unwired so asiento `BasePEN/IgvPEN` are unpopulated and
  several §7 invariants may be vacuous — recorded as a **new BACKLOG item** (see Dependencies).
- Editable / override tipo de cambio; bounding-box or OCR-confidence data (worker captures none);
  worker changes (list already computed and in golden fixtures); data migration.
- §12 unratified rules (venta rate, NC TC inheritance) — untouched.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `api-facturas`: `CorreccionFacturaRequest` / `CorreccionFactura` / `ServicioDeFacturas.PatchAsync`
  additively accept `baseImponible`, `igv`, `glosa`; new pure guards in `ValidacionDeCorreccion`;
  BORRADOR-only gate for the new fields; `PosibleDuplicado` recompute on identity-triple change;
  scalar `BasePEN/IgvPEN/NetoPEN` recompute; tipo-de-cambio-missing guard blocks `validar`/confirm
  for lookup-requiring invoices; `FacturaRespuesta` adds `camposNoExtraidos: string[]` and `glosa`.
- `factura-respuesta-asiento-respuesta`: `FacturaRespuesta` gains `camposNoExtraidos` and `glosa`
  (additive, non-breaking).
- `pantalla-detalle-validacion`: `base imponible` / `IGV` / `glosa` become editable inputs in
  `BORRADOR`; per-field highlight via `campoResaltado(campo)`; `guardarAvance` refetches.

## Approach

- **SQL** (ADR 0016): one numbered forward script in `SmartNet/SmartNetBD/schema/` adding
  `Glosa` and `CamposNoExtraidos` to `fact.Factura`; regen `checksums.txt`; matching `NNN_down.sql`.
  No new GRANT (ADR 0003 partition unchanged).
- **Promotion** (`SmartNet.Inbox.Core`): stop collapsing `EventoInbox.CamposNoExtraidos` to a
  boolean in `CalculoDeIndicadores` — persist the list alongside the existing flag via
  `IndicadoresFactura` / `SqlPromocionRepository` / `PayloadInboxParser`.
- **API contract**: extend `CorreccionFacturaRequest` / `FacturaRespuesta` in `FacturaEndpoints.cs`;
  add fields to `CorreccionFactura.cs`, pure guards to `ValidacionDeCorreccion.cs`, field-ladder +
  recompute in `ServicioDeFacturas.AplicarCorreccion`; extend `FacturaPersistida` and the
  `SqlUnidadDeTrabajo` SELECT/UPDATE lists; add a `PosibleDuplicado` SELECT in Infrastructure.
  `SmartNet.Contable.Core` stays DB/HTTP/clock-free (ADR 0019): new validation is pure, the scalar
  recompute reuses existing pure `ConversionDeMoneda.Convertir`, the duplicate SELECT lives in
  Infrastructure.
- **SPA**: `detalle/models/factura.model.ts`, `detalle/ui/factura-form/factura-form.ts` (editable
  inputs + per-field lookup), `detalle/feature/detalle-page/detalle-page.ts` (batch new fields,
  refetch after `guardarAvance`). Vitest specs. Domain named in Spanish (CONVENTIONS.md).

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/SmartNetBD/schema/NNN_*.sql` + `checksums.txt` + `NNN_down.sql` | New | `Glosa`, `CamposNoExtraidos` on `fact.Factura` |
| `SmartNet.Inbox.Core/CalculoDeIndicadores.cs`, `IndicadoresFactura.cs`, `SqlPromocionRepository`, `PayloadInboxParser` | Modified | Persist per-field not-extracted list |
| `api/SmartNet.Api/FacturaEndpoints.cs` | Modified | `CorreccionFacturaRequest`, `FacturaRespuesta` |
| `facturacion/SmartNet.Facturacion.Core/CorreccionFactura.cs`, `ValidacionDeCorreccion.cs`, `ServicioDeFacturas.cs` | Modified | New fields, pure guards, BORRADOR gate, recomputes |
| `SmartNet.Facturacion.Infrastructure` (`FacturaPersistida`, `SqlUnidadDeTrabajo`) | Modified | SELECT/UPDATE lists, `PosibleDuplicado` SELECT |
| `SmartNet/SmartNetWeb/src/app/detalle/**` | Modified | Editable inputs, per-field highlight, refetch |
| `openspec/specs/{api-facturas,factura-respuesta-asiento-respuesta,pantalla-detalle-validacion}/spec.md` | Modified | Delta specs |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Edited base/IGV diverges from hand-built asiento líneas | Med | Accepted: caught by existing `validar` §7 invariants; not regenerating líneas is deliberate |
| §7 line-sum invariants vacuous because asiento `BasePEN/IgvPEN` unpopulated | Med | Out of scope; logged as separate BACKLOG item; scalar recompute here at least populates the factura projection |
| TC-missing guard wrongly blocks PEN invoices | Low | Guard scoped to lookup-requiring invoices only (foreign currency / NC inherited rate); §6 explicit |
| Editing contable fields on VALIDADA factura bypasses §9 reabrir | Low | `PATCH` rejects new-field edits unless `estado = BORRADOR` |
| `checksums.txt` regen forgotten | Med | Task checklist + verify step asserts manifest consistency |
| Review budget (4 slices × ~4 layers) exceeds 800 lines | Med | Slices are independently revertible; sdd-tasks forecasts and may recommend stacked PRs |
| `guardarAvance` still stale after refetch race | Low | Refetch returns fresh `ETag`; duplicate flag recomputed server-side inside the same transaction |

## Rollback Plan

- SQL: run `NNN_down.sql` (drops the two nullable columns), restore prior `checksums.txt`. No data
  loss for existing invoices (columns are additive/nullable).
- Code: `git revert` the API, promotion and SPA commits — all additions are additive contract
  changes; no existing `FacturaRespuesta` / request field changes name, type or meaning.
- Promotion revert re-collapses the list to the boolean; the persisted column is simply ignored.

## Dependencies

- BACKLOG #12 (detalle screen), #18 (visual layer + coarse highlight binding).
- REGLAS.md §5–§10, `Cuentas.xlsx` / plan de cuentas — mandatory context for sdd-spec / sdd-design
  (slice 4 guards, boleta/no-gravada IGV-to-cost rule).
- ADR 0003 (partition), ADR 0016 (versioned SQL), ADR 0018 (frozen TC), ADR 0019 (core purity).
- **New BACKLOG item to open**: "Wire `ComposicionDeAsiento.Componer` into the confirm pipeline so
  asiento `BasePEN/IgvPEN` are populated and §7 line-sum invariants are non-vacuous."

## Success Criteria

- [ ] In `BORRADOR`, a user edits `base imponible`, `IGV` and `glosa`, saves avance, and the values
      persist with an `AuditoriaCorreccion` row per changed field.
- [ ] `PATCH` rejects base/IGV/glosa edits when `estado != BORRADOR`.
- [ ] Each of the 8 canonical fields highlights individually per `camposNoExtraidos`.
- [ ] Correcting `numero` (or ruc/tipo) clears a stale `PosibleDuplicado` flag without page reload.
- [ ] A foreign-currency invoice with no applicable tipo de cambio cannot be validated/confirmed; a
      PEN invoice is unaffected; "Guardar avance" still works in both cases.
- [ ] `SmartNet.Contable.Core` / `SmartNet.Facturacion.Core` stay DB/HTTP/clock-free (purity tests).

## Design Questions for sdd-spec / sdd-design

1. **Scalar recompute trigger set** — recompute `BasePEN/IgvPEN/NetoPEN` on edit of base/IGV only,
   or also on moneda change? (moneda is already SPA-editable today.) Default: base/IGV/moneda.
2. **Boleta / no-gravada IGV guard** — reject a non-zero `igv` edit outright (422), or accept and
   fold it into cost per §5? Default: reject non-zero `igv` for boleta/EXONERADA/INAFECTA.
3. **NC `07` internal reference** — does the new base/IGV editing apply at all, given the NC mirrors
   the rectified doc (§5)? Default: editable in BORRADOR, but line structure stays hand-built.
4. **Per-field highlight for XML-sourced invoices** — should `camposNoExtraidos` ever be non-empty
   when a UBL XML is present? Default: trust the worker's list as-is; no API-side derivation.
5. **`AuditoriaCorreccion` granularity** — one row per changed field (current `AplicarCorreccion`
   behavior) for base/IGV/glosa too? Default: yes, keep per-field rows.
