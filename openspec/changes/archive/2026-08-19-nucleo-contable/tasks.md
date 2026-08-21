# Tasks: Núcleo contable (#8)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~950–1100 (2 new csproj, 9 domain files, 4 test files w/ 7 goldens + 7 invariants x2 paths, sln + ci.yml edits) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (scaffold+types+PRINCIPAL+DESTINO+§6) → PR 2 (§7 invariantes+ResultadoConfirmacion+wiring) |
| Delivery strategy | ask-on-risk |
| Chain strategy | resolved: single PR, size:exception accepted by project owner (2026-08-19) |

Decision needed before apply: No — resolved by project owner: single PR, `size:exception`.
Chained PRs recommended: Yes (declined by owner in favor of a single exception PR)
Chain strategy: resolved — size:exception, not chained
400-line budget risk: High (accepted as size:exception)

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Scaffold + tipos + `Componer` (PRINCIPAL 4 casos, DESTINO, §6 conversión) + 7 goldens §10 | PR 1 | `dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests --filter Componer\|Golden` | N/A — pure lib, no runtime harness | Delete `SmartNet/contable/` folder, revert `SmartNet.sln` entry |
| 2 | `InvariantesDeConfirmacion.Evaluar` + `ResultadoConfirmacion` + accept/reject tests §7 + `PurityScanTests` + `ci.yml` wiring | PR 2 | `dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests --filter Invariante\|Purity` | N/A — pure lib, no runtime harness | Revert `ci.yml` line + delete `Invariantes*`/`ResultadoConfirmacion*` files (PR 1 stays intact) |

## Phase 1: Scaffolding (Work Unit 1)

- [x] 1.1 Create `SmartNet/contable/SmartNet.Contable.Core/SmartNet.Contable.Core.csproj` (`net10.0`, zero `PackageReference`, `ProjectReference` to `SmartNet.Catalogos.Core` and `SmartNet.TiposCambio.Core`).
- [x] 1.2 Create `SmartNet/contable/SmartNet.Contable.Core.Tests/SmartNet.Contable.Core.Tests.csproj`, copying the `tipos-de-cambio` test-project pattern (xunit, NetArchTest, Mono.Cecil).
- [x] 1.3 Add both projects + `contable` solution folder to `SmartNet/SmartNet.sln`.
- [x] 1.4 Copy `PurityScanTests.cs` from `SmartNet.TiposCambio.Core.Tests`, retargeted to `SmartNet.Contable.Core.dll` (NetArchTest + IL scan for `DateTime.Now/UtcNow`). RED until Phase 2 code exists.

## Phase 2: Input/Output Types (Work Unit 1)

- [x] 2.1 Create `LineaAsiento.cs`: record per Interfaces/Contracts (Bloque, TipoLinea, cuentas, `SinCuenta`).
- [x] 2.2 Create `AsientoContable.cs`: record per Interfaces/Contracts (ProveedorCodigo, FechaContable, AfectacionCongelada, Comprobante, Lineas, etc.).
- [x] 2.3 Create `TipoCambioCongelado.cs`: `DeTipoCambio(TipoCambio)` (reads `.Venta` only, ADR 0018 pt.1) and `Heredado(decimal)`.
- [x] 2.4 Create `CargoSolicitado.cs` (`CuentaContable Cuenta, decimal ImportePEN`, absolute amounts per Decision 5).
- [x] 2.5 Create `HerenciaNotaCredito.cs`: record with 4 frozen attributes (afectación, TC, cuentas de cargo con importes, cuentas destino) + `DesdeAsiento(AsientoContable)` adapter.
- [x] 2.6 Create `EntradaAsiento.cs`: DTO composing catálogo/TC/`Herencia`, per Decision 1/2.
- [x] 2.7 Write test: `EntradaAsiento`/`TipoCambioCongelado` construction guard tests (`ArgumentNullException` on required nulls) — RED then GREEN.

## Phase 3: Componer — PRINCIPAL + DESTINO + §6 (Work Unit 1, TDD)

- [x] 3.1 RED: golden test §10.1 (factura gravada en soles) in `ComponerGoldenTests.cs` — expects 631111 D 1000.00, 401111 D 180.00, 421211 H 1180.00.
- [x] 3.2 GREEN: implement `CuentaDeProveedor.cs` (moneda × EsRelacionada → cuenta) and `ComposicionDeAsiento.Componer` PRINCIPAL-gravada branch to pass 3.1.
- [x] 3.3 RED: golden test §10.4 (factura con percepción) — expects 401131 D 23.60, abono 1203.60, sin bloque DESTINO en 401131.
- [x] 3.4 GREEN: extend `Componer` PRINCIPAL-gravada branch for percepción line.
- [x] 3.5 RED: golden test §10.2 (boleta, IGV al costo) — expects 656111 D 1180.00, 421211 H 1180.00, sin 401111.
- [x] 3.6 GREEN: implement PRINCIPAL boleta/no-gravada branch (2 líneas).
- [x] 3.7 RED: golden test §10.5 (NC sobre factura gravada) — expects espejo invertido 3 líneas (proveedor D 236.00, 631111 H 200.00, 401111 H 36.00).
- [x] 3.8 GREEN: implement PRINCIPAL NC-sobre-gravada branch consuming `HerenciaNotaCredito`.
- [x] 3.9 RED: golden test §10.6 (NC sobre boleta) — expects espejo 2 líneas, sin 401111.
- [x] 3.10 GREEN: implement PRINCIPAL NC-sobre-boleta branch.
- [x] 3.11 RED: golden test §10.1 DESTINO (flete) — expects 946311 D 1000.00, 791111 H 1000.00 for cargo with `CtaReflejaCodigo`.
- [x] 3.12 GREEN: implement DESTINO generation for any PRINCIPAL cargo with non-null `CtaReflejaCodigo`.
- [x] 3.13 RED: golden test §10.5 DESTINO invertido en NC — expects 946311 H 200.00, 791111 D 200.00.
- [x] 3.14 GREEN: extend DESTINO to invert pair when `Comprobante == NotaCredito`.
- [x] 3.15 RED: golden test §10.3 (USD, redondeo derivado) — expects totalPEN 4471.61, igvPEN 682.11, basePEN 3789.50, cuenta 431212.
- [x] 3.16 GREEN: implement `ConversionDeMoneda.cs` (`totalPEN`/`igvPEN` anclados, `basePEN` derivado, sin línea de ajuste).
- [x] 3.17 RED: golden test §10.7 (NC 100% USD hereda TC de factura) — expects TC 3.712000 usado (no 3.715000), saldo proveedor = 0.00.
- [x] 3.18 GREEN: wire `TipoCambioCongelado.Heredado` through NC composition so `Componer` uses `Herencia.TipoCambioCongelado`, not `EntradaAsiento`'s own TC.
- [x] 3.19 Structural test: 4 casos de PRINCIPAL (tabla) + DESTINO ausente sin `ctarefleja` — confirms `Componer` never throws/rejects (ADR 0006 BORRADOR).

## Phase 4: InvariantesDeConfirmacion — §7 (Work Unit 2, TDD)

- [x] 4.1 Create `InvarianteContable.cs` enum (one value per §7 invariant in scope: 5 globales + PRINCIPAL + DESTINO).
- [x] 4.2 Create `ResultadoConfirmacion.cs`: abstract record, `private protected` ctor, sealed `Confirmable`/`InvariantesIncumplidas` (Decision 3).
- [x] 4.3 Create `InvarianteIncumplida.cs`: `InvarianteContable` + importes en conflicto, no HTTP code.
- [x] 4.4 RED: test global invariant 1 accept (`SUM(Debe)=SUM(Haber)`) + reject (descuadre) in `InvariantesDeConfirmacionTests.cs`.
- [x] 4.5 RED: test global invariant 2 accept/reject (`SinCuenta` on any línea).
- [x] 4.6 RED: test global invariant 3 accept/reject (`FechaContable >= fechaCorteContable` param, never `DateTime.Today`).
- [x] 4.7 RED: test global invariant 4 accept/reject (proveedor ≠ `P00000`).
- [x] 4.8 RED: test global invariant 5 accept/reject (`Tipo=D ⇒ Debe>0,Haber=0` e inverso).
- [x] 4.9 RED: test PRINCIPAL invariant accept (cargos 6x/1x=base, 401111=IGV) + reject (401111 indebido en NC sobre boleta, §10.6-derived).
- [x] 4.10 RED: test DESTINO invariant accept (par reflejo/puente presente) + reject (falta el par, aunque catálogo vivo ya no lo declare).
- [x] 4.11 GREEN: implement `InvariantesDeConfirmacion.Evaluar(AsientoContable, DateOnly)` covering 4.4–4.10, returning ALL failed invariants (not first).
- [x] 4.12 RED+GREEN: multi-fallo test — asiento con 2+ invariantes incumplidas simultáneamente produce `InvariantesIncumplidas` con 2+ entradas.
- [x] 4.13 Confirm no test/code path references the old NC precondition ("factura original validada") anywhere, per Non-Goals.

## Phase 5: Wiring & Cleanup (Work Unit 2)

- [x] 5.1 Update `.github/workflows/ci.yml`: add `Contable.Core.Tests` to `verificaciones-estaticas` job (no container needed).
- [x] 5.2 Run `PurityScanTests` (Phase 1.4) against completed `SmartNet.Contable.Core.dll` — confirm GREEN, zero infra deps (ADR 0019).
- [x] 5.3 Run full `dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests` — confirm all 7 goldens (§10) and all §7 invariants (both paths) pass.
- [x] 5.4 Update `Backlog de mejoras.md` §3 status note: confirm all 5 open questions closed by design Decisions 1–5 (already stated in design.md; verify no drift before archive).

## Phase 6: Post-verify follow-up (verify-report.md WARNING, SUGGESTION 1)

- [x] 6.1 Add a dedicated regression test in `ComponerGoldenTests.cs` (`Componer_BoletaMarcadaGravada_PineaGeneracionActualDeLinea401111`) and one in `InvariantesDeConfirmacionTests.cs` (`Principal_BoletaMarcadaGravadaConsistente_PineaAceptacionActual`) that pin the CURRENT behavior for Boleta + Gravada — both `ComposicionDeAsiento.Componer` and `InvariantesDeConfirmacion.EvaluarPrincipal` discriminate the PRINCIPAL-gravada branch using only `AfectacionCongelada == Gravada`, trusting that #3/#11 never mark a boleta as `Gravada`. These tests do NOT assert this is the contably-correct behavior — they make the assumption CI-detectable: if the discriminator changes, or the upstream guarantee breaks, these tests fail and force a conscious review instead of a silent drift. No production code was touched (business logic pinned as-is, per verify-report non-blocking recommendation). The upstream guard itself (rejecting Boleta+Gravada as an illegal state at #3/#11) remains explicitly out of scope for #8 and is a follow-up for those items. `dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests -c Release` → 41/41 passed; `dotnet build SmartNet/SmartNet.sln -c Release` → 0 warnings/0 errors.
