```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:444d2eb0ee91c9adc5907efeec95de0b10d6f727c41b9481b3e424ad2855ddde
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 4/4
scenarios: 16/16
test_command: python -m pytest -q -m "not integracion and not externa and not ocr"
test_exit_code: 0
test_output_hash: sha256:4f6b31e639a19acba5c85396a773338bec7558b17a1a97f620edd6a1c255f936
build_command: python -m ruff check src/smartnet_worker/pdf_texto.py src/smartnet_worker/comprobante.py src/smartnet_worker/procesamiento_repo.py src/smartnet_worker/cli_procesamiento.py src/smartnet_worker/inbox_event_repo.py src/smartnet_worker/cli_inbox.py tests/unit/test_pdf_texto.py tests/unit/test_comprobante.py tests/unit/test_procesamiento_repo.py tests/unit/test_cli_procesamiento.py tests/unit/test_inbox_event_repo.py tests/unit/test_cli_inbox.py tests/integration/test_pyodbc_integracion.py
build_exit_code: 0
build_output_hash: sha256:82b3e6a6c090a57601d22943bd23fca9218d1031dbe5a7b754092f9a156b4f18
```

# Verification Report: asociacion-pdf-clave-desde-xml

Change: keyless orphan PDF associates to orphan XML by unambiguous filename containment, plus 2
riders (widened serie regex, PDF-only InboxEvent re-emit).
Mode: Strict TDD verify (worker Python, pytest). No dotnet test - .NET untouched by this change.
Verdict: PASS WITH WARNINGS.
Artifact store: hybrid (this file + Engram sdd/asociacion-pdf-clave-desde-xml/verify-report).

## Completeness

- 1.1-1.2 serie regex: complete, regex matches design D8 verbatim.
- 2.1-2.8 pure containment pass: complete, 11 new unit tests green.
- 3.1-3.5 repo/CLI wiring: complete, residue computed separately, exact path byte-unchanged.
- 4.1-4.6 PDF-only re-emit: complete, unit-covered.
- 4.7 integration test: WRITTEN, NOT RUN - 2 tests collectable, env-blocked (W1).
- 5.1 ADR 0017: complete, Estado to Revision 3.
- 5.2 BACKLOG #26: deferred to owner (owner-managed file), draft recorded (W3).
- 6.1 unit gate: 285 passed, 28 deselected (+19 vs baseline 266).
- 6.2 integration gate: NOT RUN, env-blocked (W1).
- 6.3 ruff: 11 pre-existing E501 only, none in changed files.

## Build / Tests

- pytest unit gate: exit 0, 285 passed, 28 deselected.
- pytest -m integracion: ALL 27 integration tests skip - pre-existing harness bug (W1).
- ruff check (13 changed files): exit 0, All checks passed.
- ruff check src tests (whole tree): exit 1, 11 E501 all pre-existing in unrelated files
  (test_outbox_contrato_bidireccional, test_schema_020_outbox_clasificacion, test_payload_inbox_contract),
  byte-identical to baseline, zero in changed files.
- No coverage tool configured - coverage analysis skipped (not a failure).

## Spec Compliance Matrix

### extraccion-y-asociacion (2 ADDED requirements, 10 scenarios)

1. Unambiguous containment (3 distinct tokens) associates -> PASS.
   test_comprobante::test_containment_inequivoco_asocia_con_la_clave_del_xml_como_autoridad;
   test_cli_procesamiento::test_segunda_pasada_asocia_pdf_sin_clave_por_containment_del_nombre.
2. Tipo token absent / non-standard (fa) still associates -> PASS.
   test_comprobante::test_tipo_token_ausente_o_no_estandar_igual_asocia.
3. Two qualifying XML for one PDF -> refuse -> PASS.
   test_comprobante::test_mas_de_un_xml_califica_para_un_pdf_no_asocia_ninguno.
4. Two qualifying PDF for one XML -> refuse -> PASS.
   test_comprobante::test_mas_de_un_pdf_califica_para_un_xml_no_asocia_ninguno.
5. Near-miss token (1230 vs 12300) -> no match -> PASS (see W2 for 01230).
   test_comprobante::test_token_casi_igual_no_matchea (12300);
   companion test_comprobante::test_numero_con_ceros_a_la_izquierda_si_matchea (00001230/01230 genuine).
6. One token (001) satisfying serie AND numero is not two matches -> PASS.
   test_comprobante::test_un_solo_token_no_cuenta_como_serie_y_numero_a_la_vez.
7. Incomplete-key XML never a candidate -> PASS.
   test_comprobante::test_xml_con_clave_incompleta_nunca_es_candidato.
8. Exact four-component path unchanged (regression guard) -> PASS.
   test_comprobante::test_pdf_con_clave_propia_no_entra_en_la_segunda_pasada;
   test_cli_procesamiento::test_pasada_exacta_de_cuatro_componentes_no_cambia_con_la_segunda_pasada;
   git diff shows the asociar() body byte-unchanged.
9. F96X-00001230 -> serie F96X, numero 1230 -> PASS.
   test_pdf_texto::test_serie_alfanumerica_sunat_f96x_produce_clave;
   regression test_pdf_texto::test_serie_electronica_clasica_sigue_funcionando (F001).
10. ABCDE- / prose garbage -> nothing -> PASS.
   test_pdf_texto::test_colocaciones_de_prosa_no_se_toman_como_serie (NOTA-/FACT-);
   task 1.1 also covers ABCDE-123 and AB-123 rejection.

### inbox-event-publishing (1 MODIFIED + 1 ADDED requirement, 6 scenarios)

1. Re-run with a reflecting event -> no duplicate (kept) -> PASS.
   existing test_inbox_event_repo::test_listar_no_notificados_filtra_por_not_exists_inboxevent
   and test_insertar_evento_es_insert_select_where_not_exists_atomico.
2. Association reflected in first event -> no second event (same-run XML+PDF regression) -> PASS.
   test_inbox_event_repo::test_listar_asociacion_no_notificada_es_pdf_only_con_guarda_payload_aware
   (payload-aware NOT EXISTS); full flow corroborated by the env-blocked integration test.
3. Late PDF association -> one new event, EstadoConsumo PENDIENTE, payload carries association -> PASS.
   test_cli_inbox::test_asociacion_tardia_de_pdf_reemite_un_evento_con_la_asociacion
   (asserts documentoAsociadoId 16 and advertenciasAsociacion empty). PENDIENTE is the schema
   default and is asserted in the env-blocked integration test.
4. XML side NOT re-emitted (candidate query filters TipoDocumento PDF) -> PASS.
   test_inbox_event_repo::test_listar_asociacion_no_notificada_es_pdf_only_con_guarda_payload_aware
   (asserts the PDF filter in the SQL); test_cli_inbox::test_reemision_no_toca_el_lado_xml.
5. Re-emit not repeated once reflected (idempotency) -> PASS.
   test_inbox_event_repo::test_insertar_evento_asociacion_repite_el_not_exists_payload_aware
   (NOT EXISTS repeated in the INSERT WHERE, anti-TOCTOU). The twice-run loop itself is the
   env-blocked integration test test_reemision_pdf_only_candidate_query_y_no_repeticion.
6. Re-emit touches only fact.Procesamiento + fact.DocumentoRecibido (read) + fact.InboxEvent
   (insert), no .NET-owned table -> PASS at unit level; integration corroboration deferred (W1).
   Unit evidence: both repo tests assert dbo. is absent from the SQL and the statements are
   SELECT/INSERT only. Full data-partition proof is the env-blocked integration test
   test_segunda_pasada_containment_toca_solo_procesamiento_y_documentorecibido.

Scenario totals: 16/16 have a passing unit-level runtime test. Inbox #6 (fact_worker data
partition) is proven at the unit level (SELECT/INSERT-only statements, `dbo.` absent from SQL);
integration-level corroboration is deferred behind the pre-existing conftest bug (W1).
Requirement totals: 4/4 verified at the unit level; the two new integration tests add deferred
corroboration only.

## Correctness (spec vs implementation)

- Second pass runs on the residue of the exact 4-tuple pass, never replaces it: OK.
  cli_procesamiento._asociar_pendientes computes residuo = huerfanos minus exact-paired ids and
  concatenates Par tuples; the asociar() body is unchanged.
- XML key is sole authority; tipo not required from filename: OK (_nombre_confirma_clave matches
  RUC + serie + numero only).
- Three matches must occupy three distinct token positions: OK (_hay_representantes_distintos,
  system of distinct representatives via backtracking).
- Global bilateral 1:1 exclusivity over the full residue: OK (per-node grado_xml == 1 and
  grado_pdf == 1 over all edges).
- Widened serie regex equals design D8: OK, negative-lookahead form verbatim
  (letter + 3 alnum with (?![A-Za-z]{3} boundary) OR 3 digits).
- Re-emit candidate query filters dr.TipoDocumento = PDF: OK, present in both
  _LISTAR_ASOCIACION_NO_NOTIFICADA and the _INSERTAR_EVENTO_ASOCIACION WHERE.
- Existing _INSERTAR_EVENTO / _LISTAR_NO_NOTIFICADOS untouched: OK (separate new statements,
  confirmed by git diff).
- InboxEvent payload _VERSION unchanged: OK (payload_inbox._VERSION = 1).
- comprobante.py stays pure (ADR 0019): OK, imports only collections, collections.abc,
  dataclasses, re - no pyodbc, no pathlib, no IO.

## Design Coherence

D1 separate asociar_por_nombre_archivo, exact path byte-untouched: OK.
D2 tokenize on non-alnum, normalized per-component equality, near-miss 12300 refused: OK
   (D2 also makes 01230 normalize to 1230, a genuine match - see W2).
D3 per-node deg==1 exclusivity: OK.
D4 Par carries no key, DatosExtraidos not backfilled: OK.
D5 re-emit restricted to TipoDocumento PDF: OK.
D6 re-emit predicate plus anti-TOCTOU NOT EXISTS in INSERT: OK.
D7 payload via construir_payload, advertenciasAsociacion recomputes to empty: OK, asserted.
D8 regex: OK.
D9 amend adrs/0017 only; "adrs - v2/" untouched: OK (git status shows "adrs - v2/" clean).

## ADR 0017 (amended paragraph review)

Estado changed to "Aceptado. Revision 3". The "Recuperacion" paragraph under the PDF/XML
association section is replaced with: two forms (own key from the name; containment against the
XML authoritative key); "ambas verificadas y ninguna inferida"; the comparison runs from the XML
toward the name ("se verifica una clave que ya existe, no se adivina"); "exclusividad 1:1
bilateral sobre todo el conjunto" - if more than one XML qualifies for a PDF, or more than one PDF
for an XML, none associate; fail-safe ("queda sin asociar, nunca asociado al comprobante
equivocado"). Alternativas parenthetical and one Consecuencias bullet added. "adrs - v2/0017-*.md"
is NOT touched (pre-revision-2 snapshot). The amendment preserves verificada-no-inferida,
all-or-nothing, and ambiguity-refuses.

## TDD Compliance

- TDD Evidence reported: OK (TDD Cycle Evidence table in apply-progress, 5 rows).
- All tasks have tests: OK (every production change names a RED test).
- RED confirmed (test files exist): OK (all 6 unit test files + integration file present).
- GREEN confirmed (tests pass): OK (285 passed on re-run in this phase).
- Triangulation adequate: OK (containment 11 cases, regex 3 cases, re-emit 3 cases).
- Safety net for modified files: OK (existing 266 re-run green; +19 new).
TDD Compliance: 6/6 checks passed.

## Test Layer Distribution

- Unit (pure + fake-cursor): 285 run across 6 changed files - pytest.
- Integration (pyodbc + real schema): 2 new (27 total) in 1 file - NOT run (no SQL Server /
  conftest path bug).
- E2E: 0.

## Assertion Quality

- tests/unit/test_cli_inbox.py::test_reemision_no_toca_el_lado_xml is a documentation/smoke test
  (asserts only that no XML rows were supplied and no insert ran); the real PDF-only guarantee is
  the SQL query, covered by test_listar_asociacion_no_notificada_es_pdf_only... - WARNING.
- tests/unit/test_inbox_event_repo.py uses SQL-substring assertions (implementation-string
  coupling); acceptable for this repo established repo-layer test style, behavioral proof is
  integration-deferred - WARNING.
- No tautologies, no ghost loops, no assertion-without-production-call. Containment, regex and
  residue tests assert real return values.
Assertion quality: 0 CRITICAL, 2 WARNING.

## Quality Metrics

- Linter: ruff exit 1 - 11 E501, all pre-existing in unrelated files, byte-identical to baseline,
  zero in the 12 changed files.
- Type checker: not configured for this package.

## Scope Guardrails

This change declared surface (per apply-progress "Files Changed") is clean: the 6
src/smartnet_worker/*.py modules (pdf_texto, comprobante, procesamiento_repo, cli_procesamiento,
inbox_event_repo, cli_inbox), their 6 unit test files,
tests/integration/test_pyodbc_integracion.py, adrs/0017-frontera-del-motor-de-extraccion.md, and
openspec/changes/asociacion-pdf-clave-desde-xml/**. No .sql, no BACKLOG.md, no _VERSION change,
no "adrs - v2/", no .NET source authored for this change.

WARNING (W4): the working tree (dirty branch item-19-campos-contables-editables) ALSO carries
unrelated uncommitted work from change #25 (pdf-asociado-en-documento-factura): .NET
SqlPromocionRepository.cs, PromocionBackgroundService.cs, PoliticaDeDocumentoAsociado.cs,
IPromocionRepository.cs plus their tests; SPA visor-documento; and
openspec/specs/factura-promotion + openspec/specs/pantalla-detalle-validacion spec.md. These are
NOT part of this change and MUST be excluded when staging the commit/PR for
asociacion-pdf-clave-desde-xml.

## Issues

### CRITICAL
None.

### WARNING

W1 - Integration tests written but not executed. tests/integration/conftest.py _RUNNER_PROJECT
resolves to <repo>/SmartNet/db/runner/SmartNet.Db.Runner, which does not exist; the runner is at
<repo>/SmartNet/SmartNetApi/db/runner/SmartNet.Db.Runner. Consequently ALL 27 worker integration
tests skip, including the 2 new ones
(test_segunda_pasada_containment_toca_solo_procesamiento_y_documentorecibido and
test_reemision_pdf_only_candidate_query_y_no_repeticion). Both new tests are collectable and
import-clean (verified via pytest --collect-only). Pre-existing harness bug, NOT introduced by
this change. Owner must run `pytest -m integracion` after correcting the conftest path (needs a
disposable SQL Server + dotnet). Inbox scenario 6 (fact_worker data partition) and the
twice-run idempotency loop depend on these tests.

W2 - Spec text nitpick (01230). The extraccion-y-asociacion near-miss scenario text lists 01230
next to 12300 as a non-match. Per design D2, normalizar_numero strips leading zeros (the same rule
that makes 00001230 match), so 01230 normalizes to numero 1230 and IS a legitimate match; only
12300 is a true near-miss. The implementation follows the design and the normalization rules;
this is a deliberate, documented reconciliation (CLAUDE.md rule 1). Recommendation: amend the spec
scenario text to drop 01230 from the near-miss parenthetical before archive.

W3 - BACKLOG #26 deferred to owner (task 5.2). BACKLOG.md is owner-managed and was not edited. A
draft paragraph for item #26 (Factura uniqueness guard by comprobante identity) is recorded in
apply-progress.md. Not a defect; a handoff item.

W4 - Commit hygiene. The working tree carries unrelated uncommitted change-#25 work (see Scope
Guardrails). Stage only this change declared surface when committing.

W5 - Repo-layer tests assert SQL substrings and test_reemision_no_toca_el_lado_xml is a
smoke/documentation test; full behavioral proof for those paths is the env-blocked integration
suite (W1).

### SUGGESTION

- Add an explicit unit-level twice-run assertion in test_cli_inbox.py (feed the same association
  row twice through a fake cursor) so idempotency has runtime coverage independent of the
  integration harness.
- Fix the conftest.py _RUNNER_PROJECT path as a small follow-up so the worker integration suite
  runs again for everyone.

## Verdict

PASS WITH WARNINGS. The unit gate is green (285 passed, +19 new), comprobante.py stays pure, the
exact 4-component asociar() path is byte-unchanged with a passing regression guard, the widened
regex and the PDF-only re-emit match design D8/D5/D6 exactly, ADR 0017 is correctly amended to
Revision 3, and "adrs - v2/", _VERSION, .sql and BACKLOG.md are untouched. 16/16 spec scenarios
have a passing unit-level runtime test; integration-level corroboration for the inbox re-emit
path (idempotency loop, data-partition) is deferred behind a pre-existing conftest harness bug -
a WARNING, not a blocker, since both integration tests are written and collectable and the unit
gate is green and ruff is clean on all changed files. No CRITICAL issue blocks archive.

Next recommended: sdd-archive (fold in the W2 spec-text correction and the W3/W4 handoffs during
archive).
