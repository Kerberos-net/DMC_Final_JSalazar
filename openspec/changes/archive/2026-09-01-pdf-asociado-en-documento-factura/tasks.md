# Tasks: Associated PDF reaches the factura document viewer

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~565 (195 production + 370 test) |
| 400-line budget risk | Medium |
| Chained PRs recommended | No |
| Suggested split | Single PR |
| Delivery strategy | single-pr |
| Chain strategy | size-exception |

Decision needed before apply: Yes
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: Medium

Confirmed: budget is 800 lines; forecast ~565 fits without a stop. `single-pr` strategy requires `size:exception` approval before `sdd-apply`, per guard rules — that is the only pending decision.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Core routing policy (pure) | PR 1 (single PR) | `dotnet test inbox/SmartNet.Inbox.Core.Tests --filter PoliticaDeDocumentoAsociado` | N/A — pure unit tests, no DB | `Core/ResolucionPar.cs`, `DecisionDocumentoAsociado.cs`, `PoliticaDeDocumentoAsociado.cs` deletable independently |
| 2 | Repository port + SQL boundary | PR 1 (single PR) | `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests --filter SqlPromocionRepository` | Local SQL Server w/ versioned schema + `usr_api` role | `SqlPromocionRepository.cs` two new methods revertible without touching existing `PromoverAsync` |
| 3 | Background service wiring | PR 1 (single PR) | `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests --filter PromocionBackgroundService` | Local SQL Server, real fixture cycle | Single branch in `ProcesarPendientesAsync`, revert = delete branch, XML-only path untouched |
| 4 | SPA default-selection tweak | PR 1 (single PR) | `npm test -- visor-documento` | N/A — Angular unit test, no backend | `visor-documento.ts` `seleccionado` computed, revert = restore prior `documentos[0]` fallback |

## Phase 1: Core — Pure Routing Policy (SmartNet.Inbox.Core)

- [x] 1.1 RED: create `inbox/SmartNet.Inbox.Core.Tests/PoliticaDeDocumentoAsociadoTests.cs` with failing cases: `EsDocumentoAsociado` PDF+`DocumentoAsociadoId` set → true; **XML+`DocumentoAsociadoId` set → false (Decision 1 regression guard)**; PDF without `DocumentoAsociadoId` → false.
- [x] 1.2 RED: extend the same test file with `Decidir` map cases: `Fusionable(id)` → `Fusiona(id)`; `NoDisponible` → `Difiere`; `ParNoPromovible(motivo)` → `Descarta(motivo)`.
- [x] 1.3 GREEN: create `inbox/SmartNet.Inbox.Core/ResolucionPar.cs` — closed hierarchy `Fusionable(long FacturaId)` / `ParNoPromovible(string Motivo)` / `NoDisponible`, `private protected` ctor, mirrors `DecisionPromocion.cs`.
- [x] 1.4 GREEN: create `inbox/SmartNet.Inbox.Core/DecisionDocumentoAsociado.cs` — closed hierarchy `Fusiona(long FacturaId)` / `Difiere` / `Descarta(string Motivo)`.
- [x] 1.5 GREEN: create `inbox/SmartNet.Inbox.Core/PoliticaDeDocumentoAsociado.cs` implementing `EsDocumentoAsociado(EventoInbox)` (predicate `DocumentoAsociadoId is not null && TipoDocumento == "PDF"`) and `Decidir(ResolucionPar)` (pure 1:1 map). Run task 1.1/1.2 tests to green.
- [x] 1.6 Verify: run `dotnet test inbox/SmartNet.Inbox.Core.Tests --filter PurityScanTests` — must stay green (new types touch no SQL/JSON/clock).

## Phase 2: Infrastructure — Repository Port (SmartNet.Inbox.Infrastructure)

- [x] 2.1 GREEN (interface, no test — pure signature addition): add `Task<ResolucionPar> ResolverParAsync(long documentoAsociadoId, CancellationToken ct)` and `Task FusionarDocumentoAsync(long inboxEventId, long facturaId, DocumentoPromovido documento, CancellationToken ct)` to `inbox/SmartNet.Inbox.Core/IPromocionRepository.cs`.
- [x] 2.2 N/A — no `FakePromocionRepository`/test double exists anywhere in the codebase. `IPromocionRepository`'s only implementation (`SqlPromocionRepository`) is exercised directly against a real migrated `fact_test_<guid>` database in every existing test (`SqlPromocionRepositoryTests.cs`, `PromocionBackgroundServiceTests.cs`), never mocked. Nothing to update.
- [x] 2.3 RED: create `inbox/SmartNet.Inbox.Infrastructure.Tests/SqlPromocionRepositoryTests.cs` — `ResolverParAsync`: Query A hit → `Fusionable`; partner `Factura.Estado='DESCARTADA'` + event `EstadoConsumo='PROMOVIDO'` → `ParNoPromovible`; partner event `EstadoConsumo='DESCARTADO'` → `ParNoPromovible`; partner event `PENDIENTE`/absent → `NoDisponible`.
- [x] 2.4 RED: extend the same file — `FusionarDocumentoAsync`: inserts one `fact.DocumentoFactura` row on the given `FacturaId`, creates no `fact.Factura`, marks event `PROMOVIDO`; second call with the same `DocumentoRecibidoId` is an idempotent no-op (asserts `UQ_DocumentoFactura_DocumentoRecibidoId` catch path, no duplicate row, event stays `PROMOVIDO`).
- [x] 2.5 GREEN: implement `ResolverParAsync` in `inbox/SmartNet.Inbox.Infrastructure/SqlPromocionRepository.cs` with Query A (`fact.DocumentoFactura` JOIN `fact.Factura`, `Estado <> 'DESCARTADA'`) then Query B when A is empty (`JSON_VALUE` on `fact.InboxEvent.Payload`, `TRY_CAST` to `BIGINT`), all params as `SqlParameter`.
- [x] 2.6 GREEN: implement `FusionarDocumentoAsync` as one `SqlTransaction` reusing existing private `InsertarDocumentoFacturaAsync` (2601/2627 catch) + existing private `MarcarPromovidoAsync`; never call `InsertarFacturaAsync`/`InsertarExtraccionesAsync`. Run 2.3/2.4 tests to green.
- [x] 2.7 RED→GREEN: add/extend `inbox/SmartNet.Inbox.Infrastructure.Tests/PermissionSufficiencyTests.cs` to assert Query A and Query B execute successfully under the `usr_api` connection/role (not sysadmin) and reference neither `fact.Procesamiento` nor `fact.DocumentoRecibido`.
- [x] 2.8 Verify: run `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests --filter NoWriteToDboStructuralTests` — must stay green.

## Phase 3: Background Service Wiring

- [x] 3.1 RED: extend `SmartNet\SmartNetApi\inbox\SmartNet.Inbox.Infrastructure.Tests\PromocionBackgroundServiceTests.cs` with a new XML/PDF fixture pair (`documentoRecibidoId:1/asociado:2` XML, `documentoRecibidoId:2/asociado:1` PDF, PDF payload structurally incomplete) covering: XML-first → 1 `Factura`, 2 `DocumentoFactura`, both `PROMOVIDO`; PDF-first single cycle → PDF stays `PENDIENTE`, 0 discards; XML `Descarta` → PDF `DESCARTADO` after two cycles (split across two separate `ProcesarPendientesAsync` cycles to avoid depending on `ListarPendientesAsync`'s unordered result within one cycle; the third scenario's assertion checks the merge-branch-specific `MotivoDescarte` text, not just the state, since the PDF's own comprobante is also structurally incomplete and would discard via the unchanged path too — a weaker assertion would pass trivially).
- [x] 3.2 RED (regression guard): confirmed existing `PayloadCompleto` test case (XML with `documentoAsociadoId: 2`) stays on the unchanged sufficiency path — added an explicit `fact.Factura` total-count assertion.
- [x] 3.3 GREEN: in `inbox/SmartNet.Inbox.Infrastructure/PromocionBackgroundService.cs`, branch in `ProcesarPendientesAsync` before `PoliticaDePromocion.Decidir`: if `PoliticaDeDocumentoAsociado.EsDocumentoAsociado(evento)`, call `ResolverParAsync` then dispatch on `PoliticaDeDocumentoAsociado.Decidir` (`Fusiona` → `FusionarDocumentoAsync`; `Difiere` → no-op; `Descarta` → existing `DescartarAsync`). Otherwise fall through to the existing unchanged path. Run 3.1/3.2 to green.
- [x] 3.4 Verify: ran full `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests` — 71/71 green (all pre-existing `PromocionBackgroundServiceTests` cases plus new ones).

## Phase 4: SPA Viewer Default Selection

- [x] 4.1 RED: extended `SmartNetWeb/src/app/detalle/ui/visor-documento/visor-documento.spec.ts` — mixed list (XML + PDF) with no explicit selection → PDF selected by default; all-XML list → falls back to `documentos[0]`; explicit selection set → that document wins regardless of MIME.
- [x] 4.2 GREEN: in `SmartNetWeb/src/app/detalle/ui/visor-documento/visor-documento.ts`, added `private static readonly MIMES_RENDERIZABLES = new Set(['application/pdf', 'image/png', 'image/jpeg'])` and updated the `seleccionado` computed to prefer the first renderable document, falling back to `documentos[0]`. Ran 4.1 to green.
- [x] 4.3 Verify: ran `npm test` in `SmartNetWeb` — 479/479 green, 52/52 files, no regressions.

## Phase 5: Full Regression Sweep

- [x] 5.1 Ran `dotnet test SmartNet.sln`. All change-relevant suites green: `SmartNet.Inbox.Core.Tests`, `SmartNet.Inbox.Infrastructure.Tests` (71/71, incl. `PurityScanTests`, `NoWriteToDboStructuralTests`, `PermissionSufficiencyTests`), `SmartNet.Api.Tests` (203/203), all Facturacion/Catalogos/Auth suites. TWO pre-existing failures unrelated to this change, both confirmed failing on a clean `git stash` of this work: (a) `SmartNet.Db.Runner.Tests.RunnerFailureHaltTests.FailingScript_ExitsNonZero...` — xUnit cross-class parallelism disposal race (`TestDatabaseFixture` "database does not exist" on `DisposeAsync`), passes in isolation; (b) `SmartNet.Admin.Tests.SesionPurgarTests.DeletesRowsOlderThanTheRetentionWindow_AndLeavesNewerRowsUntouched` — session-purge retention-window boundary, fails in isolation WITH AND WITHOUT this change (admin CLI, zero overlap with inbox/viewer). Neither touches inbox, promotion, or the viewer.
- [x] 5.2 Ran `npm test` in `SmartNetWeb` — 479/479 tests green, 52/52 files, no regressions outside `visor-documento`.
- [x] 5.3 No `pytest` run needed — `git status` shows zero `SmartNetWorker/` or Python paths touched.
- [x] 5.4 Out-of-scope guardrails held: no `_VERSION` bump (parser untouched), no new/changed SQL script under `SmartNetBD/schema/`, no backfill/migration script. All production changes are `.cs` under `inbox/SmartNet.Inbox.Core` + `inbox/SmartNet.Inbox.Infrastructure` and one `.ts` under `SmartNetWeb/src/app/detalle/ui/visor-documento`.

## Out of Scope (explicit)

- Worker (Python) changes — `asociar_documentos`/`comprobante.asociar` already write the correct `DocumentoAsociadoId`/`TipoDocumento`; untouched.
- SQL schema migration — no new column, table, or `CK_InboxEvent_EstadoConsumo` value.
- `InboxEvent.Payload` / `_VERSION` change — parser already reads both fields.
- Backfill/cleanup of pre-existing duplicate or lost facturas — delivered separately as a one-off SQL script (owner decision 5).
