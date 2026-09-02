# Proposal: Wire ComposicionDeAsiento into the productive asiento lifecycle

## Intent

`ComposicionDeAsiento.Componer` is fully implemented and golden-tested against REGLAS §10.1–§10.7, but has **zero production callers**. Asientos are hand-built headers with NULL `BasePEN/IgvPEN/NetoPEN` and no lines, so REGLAS §7 invariants (PRINCIPAL, DESTINO, Global-1/2/5) pass vacuously. BACKLOG #19 half-opened the gap: editing base/IGV on a line-less asiento now fails `validar`. Symptoms to fix: (1) no asiento is generated for a factura; (2) base/IGV never appear on the detalle screen.

## Scope

### In Scope
- **Core (`facturacion`)**: new `EntradaAsiento` builder from a `FacturaPersistida` + resolved catálogo / `ProveedorAtributo` / TC venta, via ports (ADR 0019 — pure, no infra).
- **`ServicioDeFacturas.AbrirAsync`**: compose header + PRINCIPAL/DESTINO lines as a **seed** on first create only (stays idempotent); new explicit `recomponer` command regenerates.
- **`SqlUnidadDeTrabajo`**: `CrearAsientoBorradorAsync` persists composed header + lines (bulk line insert / `ReemplazarLineasAsync`); `CargarFacturaAsync` also loads `EsRelacionada` + motivo description.
- **`PromocionBackgroundService`**: fire `/abrir` automatically at promotion so the SPA always has an asiento. Coordinate with shipped #25/#26 on this seam.
- **`sugerencia` module**: wire `ServicioDeSugerencia` into API DI (`Program.cs`); seed the default cargo line with the suggested account (compose-time call only).
- **#19 `ProyeccionDeImportes` / `PatchAsync` (D4)**: unchanged; the header↔lines divergence it can create becomes the intended "`validar` blocks with descuadre message until reconciled" (owner decision 4).
- **`validar`**: runs `InvariantesDeConfirmacion` §7 against the **persisted** asiento (seed + audited `REPARTO_MANUAL` deltas), NOT a re-composition.
- **SPA**: `detalle-page` (asiento always present), `factura-form` (base/IGV populated), `asiento-lineas` (stays editable), "recomponer asiento" button, cabecera↔detalle descuadre marker (reuse #23's read-only one).
- **Tests**: `InvariantesDeConfirmacionTests` + `ServicioDeFacturasPhase2Tests` move from vacuous-pass to real-asiento fixtures; new abrir→compose→validar integration; golden re-use of REGLAS §10.1–§10.3 through the real pipeline; Strict TDD.

### Out of Scope (non-goals)
- NC composition (`HerenciaNotaCredito` branch) — needs #10/#11 association flow first; additive later.
- Percepción / REGLAS §10.4 — deferred; no `fact.Factura.PercepcionOrig` column; §10.4 stays unreachable this cycle.
- Any change to a REGLAS rule — this only wires §5–§7 into production.
- `sugerencia` endpoints / SPA suggestion UI.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `nucleo-contable`: `abrir` seeds an engine-composed asiento (header + PRINCIPAL/DESTINO); §7 invariants stop passing vacuously.
- `api-facturas`: `AbrirAsync` composes on create; new `recomponer` command; `validar` gates real balanced asiento; base/IGV edit unbalancing a manual split blocks `validar`.
- `factura-promotion`: promotion fires `/abrir` (compose + seed).
- `sugerencia-cuenta`: module wired into API DI; consumed at seed time to pick the default cargo account.
- `pantalla-detalle-validacion`: asiento always present; base/IGV shown; "recomponer" action + descuadre marker.

## Approach

Owner-fixed **Option 3 (hybrid)**: engine seeds header + lines at `abrir`; manual line edits (#12) and #19 editable base/IGV/glosa layer on top, audited as `REPARTO_MANUAL`; `validar` evaluates §7 against the persisted asiento. `abrir` never recomposes an existing BORRADOR (idempotent); an explicit `recomponer` regenerates. Account suggestion comes from wiring `sugerencia` into DI. No data migration (dev/demo asientos only).

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `facturacion/SmartNet.Facturacion.Core` | New | `EntradaAsiento` builder + `recomponer` command |
| `facturacion/SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modified | `AbrirAsync` composes; `validar` gates real asiento |
| `facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | Persist composed lines; load `EsRelacionada` + motivo |
| `SmartNet.Api/Program.cs` | Modified | Wire `sugerencia` DI |
| `PromocionBackgroundService` | Modified | Fire `/abrir` at promotion (overlaps #25/#26) |
| SPA `detalle-page` / `factura-form` / `asiento-lineas` | Modified | Asiento always present; base/IGV shown; recomponer button + descuadre marker |
| `contable` Core tests / `facturacion` tests | Modified | Vacuous fixtures → real-asiento fixtures; new integration + golden pipeline |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| §7 de-vacuuming makes existing dev asientos unconfirmable | High | Dev/demo only — no migration; `recomponer` regenerates seeds |
| `sugerencia` DI wiring scope creep | Med | Compose-time call only; no endpoints/UI |
| Manual-split ↔ base/IGV reconciliation rule ambiguity | Med | Owner decision 4: block `validar` with descuadre message; design specifies message + reconcile paths |
| SPA scope creep | Med | Minimal: auto `/abrir` at promotion + one button + reused descuadre marker |
| Test churn in invariant/Phase2 suites | High | Expected; Strict TDD; reuse REGLAS §10 goldens |
| Promotion-seam overlap with shipped #25/#26 | Med | Coordinate at design; different call paths, only `/abrir`-at-promotion touches shared seam |
| REGLAS §12 points 1 & 5 (TC venta, NC hereda TC) unratified | Low | Flagged for owner awareness; this change wires rules, does not alter them; NC path stays out |
| Likely exceeds 800-line review budget | High | Flag `size:exception` / split decision to orchestrator at tasks phase |

## Rollback Plan

Single feature branch `item-19-...`-lineage (new branch for #24). Revert the merge commit: `abrir` returns to header-only seeding, `sugerencia` DI unregisters, SPA falls back to the current no-asiento state. No schema changes, no data migration, so rollback is code-only. Golden tests for `Componer` remain green regardless (engine untouched).

## Dependencies

- Shipped #25/#26 (promotion seam) — coordinate, do not conflict.
- BACKLOG #19 (editable base/IGV/glosa) — already shipped; this reconciles its latent divergence.
- Follow-up: BACKLOG #28 (bandeja-dedup) runs next for the third reported symptom — out of scope here.

## Success Criteria

- [ ] Opening a factura produces an engine-composed asiento (header `BasePEN/IgvPEN/NetoPEN` populated + PRINCIPAL/DESTINO lines).
- [ ] REGLAS §7 invariants evaluate real data; a freshly seeded asiento with a valid single-account cargo is confirmable.
- [ ] REGLAS §10.1–§10.3 goldens pass through the real abrir→compose→validar pipeline.
- [ ] Editing base/IGV so lines no longer match `BasePEN` blocks `validar` with a descuadre message; `recomponer` or line edits clear it.
- [ ] Promotion seeds the asiento so the detalle screen always shows base/IGV.
- [ ] `sugerencia` resolves the default cargo account via prefix cascade (REGLAS §3) at seed time.
- [ ] `dotnet test` and SPA `npm test` green.

## Lineage & Notes

BACKLOG #19 → #24. Siblings #25, #26 shipped this session. This makes REGLAS §5–§7 executable in production for the first time (flag for owner: §12 points 1 & 5 unratified — not a blocker, no rule changes here). Percepción / §10.4 explicitly deferred.
