# Tasks: Associate keyless orphan PDF to orphan XML by filename containment

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~485–600 (authored, incl. tests) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (Phase 1–2) → PR 2 (Phase 3) → PR 3 (Phase 4–5) |
| Delivery strategy | single-pr |
| Chain strategy | size-exception |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: size-exception
400-line budget risk: High

> `delivery_strategy=single-pr` and the estimate is 400–800 lines. Orchestrator MUST obtain a `size:exception` sign-off from the owner before `sdd-apply` (same as the prior change this session). If the owner declines, fall back to the 3-way split below.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Widened serie regex + pure containment pass (comprobante) | PR 1 | `pytest tests/test_pdf_texto.py tests/test_comprobante.py` | N/A (pure unit, ADR 0019) | `pdf_texto.py`, `comprobante.py` + their tests revert alone |
| 2 | Repo/CLI wiring of the 2nd pass | PR 2 | `pytest tests/test_procesamiento_repo.py tests/test_cli_procesamiento*.py` | `pytest -m integracion` (listar_huerfanos pass) if disposable SQL Server | `procesamiento_repo.py`, `cli_procesamiento.py` revert without touching pure fn |
| 3 | PDF-only InboxEvent re-emit + ADR/BACKLOG | PR 3 | `pytest tests/test_inbox_event_repo.py tests/test_cli_inbox.py` | `pytest -m integracion` (candidate query) if disposable SQL Server | `inbox_event_repo.py` new statements, `cli_inbox.py` batch loop revert alone |

## Phase 1: Riders / Foundation

- [x] 1.1 RED `test_pdf_texto.py`: `F96X-00001230`→serie `F96X`/numero `1230`; `F001-1` still works; `NOTA-123`/`FACT-123` prose rejected; `ABCDE-123` and `AB-123` rejected. (spec: `_extraer_serie_numero` alphanumeric)
- [x] 1.2 GREEN `pdf_texto.py`: `_SERIE_NUMERO_RE` → `r"\b([A-Za-z](?![A-Za-z]{3}\b)[A-Za-z0-9]{3}|\d{3})\s*-\s*(\d{1,20})\b"`; fix docstring (drop "sanitized" per design BLOCKING CORRECTION).

## Phase 2: Core pure containment pass (`comprobante.py`, stays pure — ADR 0019)

- [x] 2.1 RED `test_comprobante.py`: happy path — XML complete key + PDF filename with RUC, serie, numero as 3 distinct `[^A-Za-z0-9]+` tokens → associates with XML key as authority.
- [x] 2.2 RED: tipo token absent / non-standard (`fa`) → still associates.
- [x] 2.3 RED: >1 qualifying XML for one PDF → refuse (stays orphan). >1 qualifying PDF for one XML → refuse. (global bilateral 1:1 exclusivity over full residue)
- [x] 2.4 RED: near-miss token (`1230` vs `12300`/`01230`) → no match.
- [x] 2.5 RED: one token `001` satisfying serie `001` AND numero `1` → NOT two matches (system-of-distinct-representatives), no association.
- [x] 2.6 RED: XML with incomplete ClaveComprobante → never a candidate.
- [x] 2.7 RED (regression): PDF that produced its own full key never enters this pass; exact 4-component path byte-unchanged.
- [x] 2.8 GREEN `comprobante.py`: `Documento` gains `nombre_archivo: str | None = None`; add `asociar_por_nombre_archivo(candidatos)` + helpers `_tokens` / `_nombre_confirma_clave` (tokenize on `[^A-Za-z0-9]+`, normalized equality per component, 3 distinct token positions, per-node deg==1 exclusivity). No IO imports.

## Phase 3: Repo + CLI wiring

- [x] 3.1 RED `test_procesamiento_repo.py` (fake cursor): `_LISTAR_HUERFANOS` result exposes `NombreArchivo`.
- [x] 3.2 GREEN `procesamiento_repo.py`: add `dr.NombreArchivo` to `_LISTAR_HUERFANOS` SELECT; map into row.
- [x] 3.3 GREEN `cli_procesamiento.py`: thread `nombre_archivo` into the `Documento` build.
- [x] 3.4 RED `test_cli_procesamiento*.py`: after `asociar((), huerfanos)`, second pass runs on the residue (huerfanos minus exactly-paired ids); `asociar_documentos` write loop reused; `asociar_documentos` signature unchanged.
- [x] 3.5 GREEN `cli_procesamiento._asociar_pendientes`: compute residue, concat `Par` tuples from both passes, single write loop.

## Phase 4: PDF-only InboxEvent re-emit (`fact_worker`, SELECT/INSERT only)

- [x] 4.1 RED `test_inbox_event_repo.py` / `test_cli_inbox.py`: late PDF association → exactly one new `fact.InboxEvent`, `EstadoConsumo='PENDIENTE'`, payload carries `documentoAsociadoId` + recomputed `advertenciasAsociacion`.
- [x] 4.2 RED (regression): XML side of the same association is NOT re-emitted (candidate query filters `dr.TipoDocumento='PDF'` — design D5).
- [x] 4.3 RED: running the re-emit twice emits no third row (NOT EXISTS on `JSON_VALUE(Payload,'$.documento.documentoAsociadoId')` non-null).
- [x] 4.4 RED (regression): same-run XML+PDF whose first event already reflects the association → no second event.
- [x] 4.5 GREEN `inbox_event_repo.py`: add `_LISTAR_CANDIDATOS_ASOCIACION` (predicate D6: `p.DocumentoAsociadoId IS NOT NULL AND dr.TipoDocumento='PDF' AND NOT EXISTS(...)`) + `_INSERTAR_EVENTO_ASOCIACION` (same NOT EXISTS in WHERE, anti-TOCTOU). Leave existing `_INSERTAR_EVENTO` guard intact; separate statements.
- [x] 4.6 GREEN `cli_inbox.py`: second batch loop; build payload via existing `construir_payload` with `documento_asociado_id` populated; `_VERSION` stays 1.
- [~] 4.7 Integration test WRITTEN (`-m integracion`): `test_segunda_pasada_containment_toca_solo_procesamiento_y_documentorecibido` + `test_reemision_pdf_only_candidate_query_y_no_repeticion` in `tests/integration/test_pyodbc_integracion.py`. NOT RUN here — no disposable SQL Server in this environment (3 skipped). User must run `pytest -m integracion`.

## Phase 5: Docs

- [x] 5.1 `adrs/0017-frontera-del-motor-de-extraccion.md`: apply design's replacement paragraph verbatim (§"Asociación PDF ↔ XML" ¶"Recuperación": two forms, containment vs XML authority, bilateral 1:1 global exclusivity, fails safe) + Alternativas parenthetical + 1 Consecuencias bullet + Estado → Revisión 3. Not a test task. `adrs - v2/0017-*.md` left UNTOUCHED (pre-revision-2 snapshot).
- [~] 5.2 DEFERRED TO OWNER — `BACKLOG.md` is owner-managed and was NOT edited. Draft paragraph for item #26 recorded in apply-progress.md for the orchestrator to hand to the owner.

## Phase 6: Verification sweep

- [x] 6.1 `pytest -m "not integracion and not externa and not ocr"` (unit gate) green.
- [~] 6.2 `pytest -m integracion` — NOT RUN (no SQL Server in this environment; tests skip). User must run: `cd SmartNet/SmartNetWorker && pytest -m integracion`.
- [x] 6.3 `ruff check src tests` clean. No `dotnet test` — .NET untouched (shipped #25 handles the re-emitted PDF event).
