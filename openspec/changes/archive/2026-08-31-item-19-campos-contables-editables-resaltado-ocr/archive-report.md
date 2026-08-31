# Archive Report — Campos contables editables y resaltado OCR por campo (BACKLOG #19)

**Change**: item-19-campos-contables-editables-resaltado-ocr
**Archive date**: 2026-08-31
**Mode**: hybrid (Engram + openspec filesystem)
**Verdict at close**: CLOSED — PASS WITH WARNINGS (0 CRITICAL).
**Branch**: item-19-campos-contables-editables
**Impl commits**: fdca18f, d6739e9, 38ba008, 07f78fe, 204bb0e, 82646c0 (baseline a2e7396). Cumulative vs a2e7396: 36 files, +1684/-104.

## Artifact traceability (Engram observation IDs)

| Artifact | Obs |
|----------|-----|
| explore | #259 |
| proposal | #260 |
| spec | #261 |
| design | #262 |
| tasks | #263 |
| planning-complete marker | #264 |
| apply-progress | #265 |
| verify-report | #266 (validator valid=true, verdict=pass_with_warnings) |
| archive-report | #267 |

## Task Completion Gate

PASS — 33/33 tasks marked `[x]` in tasks.md (phases 1.1–1.5, 2.1–2.4, 3.1–3.15, 4.1–4.6, 5.1–5.3).

## Final state

- 33/33 tasks complete and green.
- SPA: 476/476 (`npx ng test --watch=false`).
- .NET, all #19-relevant projects green in isolation: Contable.Core 49/49, Facturacion.Core 172/172, Facturacion.Infrastructure 65/65, Api.Tests 203/203, Inbox.Core 51/51, Inbox.Infrastructure 59/59, TiposCambio.Infrastructure 15/15.
- 5 full-solution failures are PRE-EXISTING DB-parallelism flakes, each green in isolation, NOT caused by #19: RunnerJournalTests.Runner_IsIdempotent, SesionPurgarTests.DeletesRowsOlderThanTheRetentionWindow, TiposCambio PermissionSufficiencyTests(usr_worker), catalogos SqlProveedorRepositoryTests server-sort, SqlSesionTicketStoreTests.RemoveAsync.
- BACKLOG.md #19 row revised + #24 opened (commit a2e7396).

## Specs synced to `openspec/specs/`

| Domain | Action | Details |
|--------|--------|---------|
| api-facturas | Updated | 1 requirement RENAMED+MODIFIED (`CorreccionFacturaRequest` now accepts `baseImponible`/`igv`/`glosa` + `PENDIENTE_VALIDACION`-only gate, 422 guards, NC 07 carve-out); 1 MODIFIED (`FacturaRespuesta` adds `CamposNoExtraidos` string[] + `Glosa`); 3 ADDED (PosibleDuplicado recompute on identity-triple change; scalar BasePEN/IgvPEN/NetoPEN recompute; missing-TC 409 narrowed to exclude NC 07 con referencia interna). State-model note added. |
| pantalla-detalle-validacion | Updated | 3 MODIFIED (base/IGV/glosa editable while PENDIENTE_VALIDACION with IGV lock for 03/EXONERADA/INAFECTA except NC 07 interna; per-field `campoResaltado` against `CamposNoExtraidos`; post-`PATCH` refetch on "Guardar avance"); 1 ADDED (missing-tipo-de-cambio 409 surfaced distinctly). |
| factura-respuesta-asiento-respuesta | NOT created (folded) | No standalone spec exists; the #12 delta only added `TipoCambioVenta` to `AsientoRespuesta`. The #19 `FacturaRespuesta` `CamposNoExtraidos`/`Glosa` content is fully covered by the api-facturas requirement. A near-duplicate standalone spec was rejected. The #19 delta file is retained here as the historical record. |

### Correction applied before merge (verify WARNING 1 — 409 vs 422)

The archived delta `specs/api-facturas/spec.md` said `409` in two places for a contable edit on a VALIDADA factura. Implementation and all tests use `422` (design D2 places the state gate in the pure `ValidacionDeCorreccion` → `CorreccionInvalida` → 422; tasks.md 3.9). Both occurrences changed 409 → 422 in the delta file and the merged main spec. The narrowed missing-TC conflict legitimately keeps `409` (unchanged `SinTipoCambio` response shape).

## Deferred follow-ups (tracked, not blockers)

- **(a)** IGV-guard exemption is currently coded for ALL tipo `07`; tighten to "NC 07 con referencia interna" once BACKLOG #10/#11 populate `FacturaReferenciaId`. Branch is dormant today.
- **(b)** Expose `IgvOrig` on `FacturaRespuesta` so the SPA seeds editable base/IGV from original currency instead of the PEN projection. Today the foreign-currency seed value is PEN-denominated (server re-derives; all spec scenarios are PEN).
- **(c)** Add a `SmartNet.Api.Tests` assertion on a real GET/PATCH response body for `FacturaRespuesta.CamposNoExtraidos` / `Glosa` (currently covered only transitively).
- **(d)** Task 5.3 manual guardar-avance smoke deferred (no seeded local DB this session) — perform at first deploy.
- **(e)** BACKLOG #24 (wire `ComposicionDeAsiento.Componer` into the confirm pipeline) is the real fix for the un-vacuumed REGLAS §7 line-sum invariant that design D4 activates. Owner accepted that `validar` MAY newly reject invoices with mismatched hand-built líneas.

## Accepted deviation (recorded, not a follow-up)

3rd `IUnidadDeTrabajo` port `ActualizarPosibleDuplicadoAsync` beyond design's "2 new ports" — sound (bare non-CAS single-column UPDATE, avoids clobbering the other indicator columns), integration-tested. Accept.

## SDD cycle

Planned → implemented → verified (PASS WITH WARNINGS, 0 CRITICAL) → specs synced → archived. SDD runtime attempt closed as `passed` (evidence sha256:3e2bfaa0…, ledger complete).
