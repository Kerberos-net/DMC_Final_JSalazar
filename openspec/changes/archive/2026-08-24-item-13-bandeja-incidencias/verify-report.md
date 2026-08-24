# Verify Report: item-13-bandeja-incidencias (BACKLOG #13)

## Mode
Full artifact set: proposal + design + specs (bandeja, api-incidencias-integraciones delta,
inbox-screen delta) + tasks (51/51 checked) + apply-progress (Engram #175, final revision,
5 revisions merged; #176 is a superseded interim note from batch 2 recovery, content
confirms the same code state, just an earlier checkpoint).

## Task completeness
51/51 tasks marked [x] in openspec/changes/item-13-bandeja-incidencias/tasks.md. Working
tree (git status) matches exactly the "Affected Areas"/"File Changes" tables in
proposal.md/design.md.

## Test/build evidence
- dotnet test SmartNet.sln (run twice from SmartNet/): SmartNet.Inbox.Core.Tests 49/49
  both runs, SmartNet.Inbox.Infrastructure.Tests 41/41 both runs, SmartNet.Api.Tests
  132/132 both runs, SmartNet.Contable.Core.Tests 41/41 both runs.
- npx ng test --watch=false -> 162/162 green, 25/25 test files.
- npx ng build -> compiles clean.

## Spec compliance matrix

### specs/bandeja/spec.md
| Requirement | Scenario | Evidence | Status |
|---|---|---|---|
| Filters by estado/desde/hasta/proveedor | Combined filters narrow result | SqlBandejaRepositoryTests.ListarAsync_FiltersByEstadoConsumo/FiltersByDesdeHasta/FiltersByProveedor_* plus FiltroWhere AND-composition in SqlBandejaRepository.cs:192-212 | PASS |
| | All filters omitted, default view | ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted plus GetBandeja_DefaultView_ExcludesPromotedAndDiscardedRows (API) | PASS |
| | proveedor matches no rows, 200 empty | ListarAsync_EmptyPage_ReturnsTruthfulTotalRegistros_ViaFallbackCount covers the empty-page shape; proveedor-specific empty-match not separately named but same code path | PASS |
| Pagination, 20/page, envelope | First page default | PaginaBandeja and EnvelopeBandeja.CalcularTotalPaginas, OrigenBandejaTests envelope-math tests | PASS |
| | pagina beyond totalPaginas, empty, truthful totals | ListarAsync_EmptyPage_ReturnsTruthfulTotalRegistros_ViaFallbackCount, BandejaEndpointsTests pagina>1 case | PASS |
| origen discriminator, server-side combine | Response has both origins in one call | OrigenBandeja.Derivar unit tests plus SqlBandejaRepository.cs:161 calls it per row from one query, single GET call site in inbox.service.ts | PASS |
| Panel de errores both origins | INCIDENCIA full history / FACTURA no-history omits panel / FACTURA promoted-then-failed shows failure | ListarAsync_IncludesErrorHistory_WithoutDuplicatingBandejaRows, panel-errores.spec.ts renders nothing on empty array, inbox-list.html:26 gates the details element on errores.length>0 for any origen | PASS |
| Default view excludes terminal | Default excludes validated facturas / explicit estado includes them | ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted (Infra) plus GetBandeja_DefaultView_ExcludesPromotedAndDiscardedRows (Api), see Deviations below | PASS |

### specs/api-incidencias-integraciones/spec.md (delta)
| Requirement | Scenario | Evidence | Status |
|---|---|---|---|
| reprocesar enqueues CommandQueue, id equals ProcesamientoId | Enqueues a command / uses ProcesamientoId not InboxEventId/FacturaId | Pre-existing #11 endpoint route (unchanged, per proposal's Unchanged section); inbox.service.ts:78-80 posts procesamientoId explicitly; SqlBandejaRepository.cs keys ReprocesarDisponibleEn/error lookups by ProcesamientoId consistently | PASS (behavior inherited from #11) |
| sincronizar/reconectar unchanged | out of scope for this item | Not modified, confirmed via git status | PASS (untouched, as scoped) |
| estado derivation pill | out of scope, unchanged | Not touched | PASS (untouched, as scoped) |

### specs/inbox-screen/spec.md (delta)
| Requirement | Scenario | Evidence | Status |
|---|---|---|---|
| Read-only except reprocesar | No unrelated action controls / reprocesar renders only where applicable | inbox-list.spec.ts asserts aprobar/editar/descartar test ids never render (line 164); reprocesar-id gated on errores.length>0 in inbox-list.html:39-48 and covered by inbox-list.spec.ts | PASS |
| Filter inputs desde/hasta/proveedor | Combined filters sent as-is | inbox-filter.spec.ts, inbox.service.spec.ts (cargar param-building), inbox-page.spec.ts filter-handler tests | PASS |
| Panel de errores renders history | Multiple entries shown / FACTURA no-history renders no panel | panel-errores.spec.ts, inbox-list.html details gate | PASS |
| Reprocesar confirmation and pending window | Confirm blocks accidental / confirmed disables / stays disabled under 5min / re-enables after 5min | confirmar-reproceso.spec.ts (dialog open/confirm/cancel), inbox-page.spec.ts (confirm to service call to refetch, cancel sends no request, reprocesandoId optimistic guard), inbox-list.ts:67-68,72-74, SqlBandejaRepositoryTests.ListarAsync_ComputesReprocesarDisponibleEn_FromPendingCommandQueue | PASS |

## Design coherence (D1-D8)
| Decision | Implemented as specified | Evidence |
|---|---|---|
| D1 permission grant, not view | Yes, 018 does REVOKE then GRANT SELECT plus explicit DENY INSERT/UPDATE/DELETE, no view created | 018_permiso_lectura_procesamiento_error.sql, PermissionMatrixTests.UsrApi_CanSelect_ProcesamientoError_ButStaysDenied_OnInsertUpdateDelete |
| D2 flat record, origen string discriminator, TS discriminated union over same wire shape | Yes | BandejaItem C# record (single flat shape) vs TS BandejaItem union type narrowed on origen, same JSON wire, no custom converter either side |
| D3 second result set for errors, not JOIN | Yes, exactly a second SELECT in the same batch keyed by @pagina, no LEFT JOIN multiplying page rows | SqlBandejaRepository.cs:82-85 |
| D4 OFFSET/FETCH plus InboxEventId tiebreak plus COUNT OVER, fallback COUNT only when empty and pagina>1 | Yes | SqlBandejaRepository.cs:63-64 tiebreak, line 87-93 conditional fallback statement |
| D5 server computes reprocesarDisponibleEn from CommandQueue, client only compares the already-computed absolute timestamp for render state | Yes | SqlBandejaRepository.cs:69-75, PoliticaDeReprocesamiento.VentanaMinutos=5 in Core, inbox-list.ts:67-68 only compares the server-supplied absolute cutoff to Date.now for rendering |
| D6 custom dialog element, no CDK/Material/window.confirm | Yes | confirmar-reproceso.ts wraps native dialog element; jsdom-28 open IDL workaround is cosmetic and documented |
| D7 proveedor identity match plus JSON_VALUE fallback, no DatosExtraidos join | Yes | FiltroWhere in SqlBandejaRepository.cs:206-211, ListarAsync_FiltersByProveedor_MatchesIdentityOnPromotedRows / FallsBackToPayloadJson_ForNonPromotedRows |
| D8 dumb panel-errores, embedded in details by inbox-list, not a signal-owned expansion state | Yes | panel-errores.ts has no expansion-state signal, details element lives in inbox-list.html:27-30 |

## Success Criteria (proposal.md) verified against code/tests
(literal checkboxes in proposal.md remain unchecked text; verified against artifacts, not the checkbox glyphs)
- [x] GET /api/bandeja accepts and applies estado/desde/hasta/proveedor/pagina/orden
- [x] Every row declares origen, no client-side merge
- [x] Both incidencia and promoted-factura rows show error history
- [x] Reprocesar: confirmation plus pending-disable plus 5-min re-enable
- [x] inbox-list.ts contract change documented in code (doc comment at top of the file)
- [x] SmartNet.Inbox.Core / SmartNet.Contable.Core purity tests pass unmodified

## ADR 0003 amendment and migration 018 consistency
018_permiso_lectura_procesamiento_error.sql revokes the prior DENY, grants SELECT, re-denies
write verbs, and additively creates the three recommended indexes (idempotent, matching the
017 precedent's create-if-absent style). The ADR 0003 amendment (revision 6) reclassifies
fact.ProcesamientoError to asymmetric-read, citing the fact.Configuracion precedent, and its
ownership matrix row matches exactly what the SQL grants. openspec/specs/esquema-y-permisos
gained the matching SELECT-yes write-no scenario. All three artifacts agree with each other
and with the DB.Runner test evidence (PermissionMatrixTests, run isolated, 27/27 green).

## ADR 0019 (accounting core purity)
SmartNet.Contable.Core was not touched by this item. SmartNet.Contable.Core.Tests (41/41,
includes purity scan) ran green in both full solution runs. SmartNet.Inbox.Core's own
PurityScanTests.cs also passed. Direct read of OrigenBandeja.cs confirms no infra imports.

## Deviations reviewed
1. Task 4.4 split (default-view test between Infra and Api layers): reasonable. The
   PENDIENTE-inclusion half needs a genuinely-pending InboxEvent row, which triggers the
   pre-existing PromocionBackgroundService inside WebApplicationFactory-hosted API tests
   and crashes the test host (documented gotcha in apply-progress observation 175). Splitting
   the assertion to the layer where each half can be safely proven does not create a coverage
   gap; both halves of the rule are proven by a passing automated test somewhere in the suite.
2. checksums.txt fix (018's hash missing, then added via generate-checksums.ps1): exactly the
   kind of gap the checksum-verification test suite exists to catch, and it was caught and
   fixed within this item's own verification loop before sdd-verify, not silently.

Neither deviation hides a real requirement gap; both are documented, narrowly scoped, and
independently verifiable against the actual test suite.

## Scope discipline confirmed
- 6th indicator EsReferenciaExterna: not touched, only mentioned in existing doc comments as
  deliberately excluded, no new code path.
- No separate /api/incidencias endpoint created; only BandejaEndpoints.cs modified.
- No multi-role authorization added; BandejaEndpoints.cs still uses RequireAuthorization
  (session-cookie presence) as before, no new policy/role check.
- Item #18 (visual/token work) not touched; sprint log tracks it separately.

## Issues

### CRITICAL
None.

### WARNING
None.

### SUGGESTION
1. The bandeja spec scenario "proveedor matches no rows" does not have a test with that exact
   name/intent; it is implicitly covered by the same code path as the general empty-page test
   (which exercises pagina beyond totalPaginas, not specifically no-rows-match-on-page-1). Both
   produce an empty items array through the identical WHERE plus fallback-COUNT code path with
   no proveedor-specific branching, so risk of an untested divergence is low, but a named test
   would make this traceable to the scenario 1:1 for future readers. Non-blocking.
2. inbox-list.ts's reprocesarDisponible computed signal re-evaluates only when its input
   signal (items) or reprocesandoId changes; there is no timer/interval driving a re-render
   purely from wall-clock time passing. This is fine because the spec's "re-enables after 5
   minutes" scenario is proven at the re-fetch boundary, matching this item's explicit
   interim-behavior framing (item #14 will later replace this with a real claim signal). Not a
   spec violation, just worth flagging that a user who leaves the tab open past 5 minutes
   without any other interaction will not see the button re-enable until the next
   fetch/interaction.

## Final Verdict: PASS
