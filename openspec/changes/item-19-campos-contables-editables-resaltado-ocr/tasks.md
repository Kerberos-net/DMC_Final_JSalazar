# Tasks: Campos contables editables y resaltado OCR por campo (BACKLOG #19)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1300–1600 (SQL ~40, promotion ~120, domain pure ~160, API write/promotion contract ~120, infra ~150, SPA ~200, tests ~450) |
| 400-line budget risk | High |
| Chained PRs recommended | No — `size:exception` accepted by owner |
| Suggested split | Single PR; 4 internal commit-sequencing units |
| Delivery strategy | single-pr |
| Chain strategy | size-exception (accepted) |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

Exceeds the 800-line budget (~1450 est.). Owner ACCEPTED the `size:exception` — ships as **one PR**. The 4 units below are an INTERNAL commit-sequencing guide only, not separate PRs.

### Internal Commit-Sequencing Units (one PR)

| Unit | Goal | Focused test command | Runtime harness | Rollback boundary |
|------|------|----------------------|-----------------|-------------------|
| 1 | SQL 021 (Glosa, CamposNoExtraidos) + rollback + checksums | `dotnet test SmartNet.*.Tests --filter Checksum|Rollback|PermissionMatrix` | Apply 021 + 021_down against local SQL Server | Drop 2 columns, restore checksums.txt |
| 2 | Promotion carries per-field list (Inbox.Core/Infrastructure) | `dotnet test SmartNet.Inbox.Core.Tests SmartNet.Inbox.Infrastructure.Tests` | Promoción against seeded EventoInbox fixture | Revert CalculoDeIndicadores/PayloadInboxParser/SqlPromocionRepository |
| 3 | Domain: ProyeccionDeImportes + ValidacionDeCorreccion + ServicioDeFacturas ladder/D4/D6/D7 + infra + API contract | `dotnet test SmartNet.Contable.Core.Tests SmartNet.Facturacion.Core.Tests SmartNet.Facturacion.Infrastructure.Tests SmartNet.Api.Tests` | Integration fixture: trio ladder, recompute, scalar write, NC07 carve-out | Revert Facturacion/Contable/API diffs |
| 4 | SPA detalle: per-field highlight, editable base/IGV/glosa, refetch | `ng test --include='**/detalle/**/*.spec.ts'` | `ng serve` manual guardar-avance → duplicate/PEN-scalar refresh | Remove detalle factura-form/model/detalle-page diffs |

## Phase 1: SQL Foundation (Unit 1) — satisfies slice "glosa" + "per-field OCR highlight" storage

- [x] 1.1 RED: extend `PermissionMatrixTests` — no new GRANT on `fact.Factura`; `fact_api` UPDATE still scoped, `fact_worker` unchanged (ADR 0003)
- [x] 1.2 RED: `ChecksumManifestTests` + `RollbackAdvisoryTests` expect `021_glosa_y_campos_no_extraidos.sql` + `rollback/021_down.sql`
- [x] 1.3 GREEN: CREATE `SmartNet/SmartNetBD/schema/021_glosa_y_campos_no_extraidos.sql` — `ALTER TABLE fact.Factura ADD Glosa NVARCHAR(250) NULL, CamposNoExtraidos NVARCHAR(500) NULL` (own GO batch, no GRANT)
- [x] 1.4 GREEN: CREATE `rollback/021_down.sql` (drop both columns); regenerate `checksums.txt` via `generate-checksums.ps1` / `.sh`
- [x] 1.5 RED→GREEN: idempotency test — 021 re-run is a no-op

## Phase 2: Promotion carries per-field list (Unit 2) — satisfies "promotion stops collapsing the worker list"

- [x] 2.1 RED: `CalculoDeIndicadores` test — `IndicadoresFactura` exposes `CamposNoExtraidos` list beside derived `TieneCamposNoExtraidos`; consistency invariant (non-empty iff bool true)
- [x] 2.2 GREEN: MODIFY `Inbox.Core/IndicadoresFactura.cs` + `CalculoDeIndicadores.cs` — carry the list, no collapse
- [x] 2.3 RED: `SqlPromocionRepository` test — promotion persists `fact.Factura.CamposNoExtraidos` from `EventoInbox.CamposNoExtraidos`; UBL-XML-present + non-empty list is valid (D8)
- [x] 2.4 GREEN: MODIFY `PayloadInboxParser.cs` + `SqlPromocionRepository.cs` — parse + persist; no API-side derivation

## Phase 3: Accounting core + write pipeline (Unit 3)

- [x] 3.1 RED: `ProyeccionDeImportes.Derivar` vs REGLAS §10.1/10.2/10.3 numbers — gravada (3789.50+682.11=4471.61), boleta/EXONERADA/INAFECTA (IgvPEN=0, BasePEN=NetoPEN=conv(TotalOrig)), NetoPEN=BasePEN+IgvPEN always; PEN TCventa=1
- [x] 3.2 GREEN: CREATE `SmartNet/.../Contable.Core/ProyeccionDeImportes.cs` — pure, delegates to `ConversionDeMoneda.Convertir` + §5 IGV-to-cost collapse; no infra
- [x] 3.3 RED: purity scan test — Contable.Core / Facturacion.Core stay DB/HTTP/clock-free (ADR 0019)
- [x] 3.4 RED: `ValidacionDeCorreccion.Validar(original, cambios)` guards on MERGED values — 422 on base<0, IgvOrig>TotalOrig, blank numero, unknown tipoComprobante; edit of base/igv/glosa rejected unless `Estado == PENDIENTE_VALIDACION` (D2). Non-zero IGV → hard 422 ONLY for boleta `03` and non-NC no-gravada (EXONERADA/INAFECTA on `01`/`03`) — owner decision (a). The guard does NOT fire for NC `07` (§6 TC-inheritance / boleta-mirror path) — owner decision (b): NC `07` con referencia interna follows §6, no IGV rejection
- [x] 3.5 GREEN: MODIFY `CorreccionFactura.cs` (trailing `BaseImponible`/`Igv`/`Glosa`), `ValidacionDeCorreccion.cs` (new arg), `FacturaPersistida.cs` (+`IgvOrig`/`Glosa`/`CamposNoExtraidos`)
- [ ] 3.6 RED: integration — `PatchAsync` trio ladder writes `TotalOrig=base+igv`, `IgvOrig=igv` only; `{base,igv}`+`totalOrig` same PATCH → 422; one `AuditoriaCorreccion` row per changed persisted column (TotalOrig, IgvOrig, Glosa), no synthetic BaseImponible row (D1/D7)
- [ ] 3.7 RED: integration — scalar `BasePEN`/`IgvPEN`/`NetoPEN` written onto vigente BORRADOR asiento in same tx, ONLY when TotalOrig/IgvOrig/Moneda changed, Version bumps; missing applicable rate → skip write, PATCH still 200 (D4)
- [ ] 3.8 RED: integration — `PosibleDuplicado` recomputed in `PatchAsync` iff identity triple (RucProveedor, TipoComprobante, Numero) changed; excludes self + DESCARTADA; in same PATCH response; no AuditoriaCorreccion row (D6)
- [ ] 3.9 RED: integration — edit on `VALIDADA` → 409 zero rows; DESCARTADA not editable; tipo/numero keep audited-Correccion post-validation behavior
- [ ] 3.10 GREEN: MODIFY `IUnidadDeTrabajo.cs` (+`ExisteIdentidadPreviaAsync`, +`ActualizarProyeccionEscalarAsync`), `ServicioDeFacturas.cs` (ladder + D4/D6/D7)
- [ ] 3.11 GREEN: MODIFY `SqlUnidadDeTrabajo.cs` — SELECT/UPDATE column lists, `ExisteIdentidadPreviaAsync`, scalar projection write
- [ ] 3.12 RED: `SqlUnidadDeTrabajo.EvaluarHechosDeConflicto` — `SinTipoCambio` narrowed: foreign-currency still 409; PEN unaffected; NC `07` `EsReferenciaExterna=0 AND FacturaReferenciaId IS NOT NULL` NOT blocked; NC `07` referencia externa still 409; PATCH path unaffected (dormant branch, §6 TC-inheritance)
- [ ] 3.13 GREEN: MODIFY `SqlUnidadDeTrabajo.cs:107` predicate
- [x] 3.14 RED→GREEN: MODIFY `Api/FacturaEndpoints.cs` — `CorreccionFacturaRequest` (+`baseImponible`/`igv`/`glosa`), `FacturaRespuesta` (+`CamposNoExtraidos: string[]`, +`Glosa`), trailing additive; `TieneCamposNoExtraidos` retained. NOTE: repository population of `CamposNoExtraidos`/`Glosa` in `SqlUnidadDeTrabajo.CargarFacturaAsync` is task 3.11 (still pending) — `De()` defaults to empty array / null until then
- [ ] 3.15 RED→GREEN: integration — accepted §7 consequence: populating asiento `BasePEN` makes `validar` reject an invoice whose hand-built líneas ≠ edited base; surfaced distinctly

## Phase 4: SPA detalle (Unit 4)

- [ ] 4.1 RED (Vitest): `campoResaltado(campo)` true only for fields in `FacturaRespuesta.camposNoExtraidos`; pre-021 null → falls back to `tieneCamposNoExtraidos`; `.campo--resaltado` class only on listed fields
- [ ] 4.2 GREEN: MODIFY `detalle/models/factura.model.ts` — `camposNoExtraidos: readonly string[]`, `glosa: string | null`; replace invoice-wide `computed()` with `campoResaltado`
- [ ] 4.3 RED (Vitest): base/IGV/glosa inputs editable only when `estado === 'PENDIENTE_VALIDACION'` (read-only formatted otherwise); IGV disabled/forced 0 for boleta `03` / EXONERADA / INAFECTA; TC compra read-only
- [ ] 4.4 RED (Vitest): "Guardar avance" draft emits `{baseImponible, igv}` pair, strips `totalOrig`; after PATCH adopts returned `FacturaRespuesta` + fresh ETag
- [ ] 4.5 GREEN: MODIFY `detalle/ui/factura-form/*` + `feature/detalle-page/detalle-page.ts` — editable fields, per-field highlight, `cargarTodo()` refetch after guardar (D5) so recomputed PosibleDuplicado / PEN scalars show without reload
- [ ] 4.6 RED→GREEN: scenario — correcting `numero` clears stale duplicate and re-enables Validar; missing-TC 409 on Validar surfaced distinctly from 412, edits kept, Guardar avance still available; newly-live §7 422 surfaced distinctly

## Phase 5: Docs & Verification

- [ ] 5.1 Open BACKLOG #24 "wire `ComposicionDeAsiento.Componer` into confirm pipeline" (already referenced by orchestrator) — confirm it exists before apply
- [ ] 5.2 Run full suite (.NET pure + integration, Vitest) — all RED tests green, PermissionMatrix/Checksum/Rollback green
- [ ] 5.3 Manual smoke — seeded PENDIENTE_VALIDACION factura: edit base/IGV, guardar avance, observe PEN scalar + duplicate refresh; edit numero to a duplicate and back

## Owner decisions (RESOLVED 2026-08-31 — all tasks unblocked)

- **(a) boleta / non-NC no-gravada IGV != 0** → hard **422 reject**. Implement as spec'd in Task 3.4.
- **(b) NC `07`** → does NOT reject non-zero IGV. The 422 IGV guard is scoped to boletas `03` + non-NC no-gravada only; NC `07` con referencia interna follows §6 TC-inheritance. Task 3.4 guard scoped accordingly.
- **NetoPEN = BasePEN + IgvPEN** → VERIFIED against goldens (3789.50 + 682.11 = 4471.61) and the non-gravada fixture. Task 3.7 unblocked.
- **D4 §7 invariant goes live** → owner ACCEPTED. Task 3.7 / 3.15 unblocked; keep the behavior-change test (populated `BasePEN` may make `validar` reject invoices with mismatched hand-built líneas).
