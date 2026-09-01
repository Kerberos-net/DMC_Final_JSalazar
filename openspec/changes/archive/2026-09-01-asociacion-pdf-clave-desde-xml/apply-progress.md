# Apply Progress: asociacion-pdf-clave-desde-xml

**Batch**: 1 (only) — all 6 phases attempted in one pass.
**Mode**: Strict TDD (RED → GREEN per production change).
**Delivery**: single-pr with owner-approved `size:exception`. Implemented whole change in one working tree, no split.
**Branch**: item-19-campos-contables-editables (unchanged, no commit).

## Status: partial

All code + unit tests + docs complete and green. Integration tests WRITTEN but NOT RUN
(no disposable SQL Server in this environment — 3 skipped). BACKLOG #26 deferred to owner.

## TDD Cycle Evidence

| Task | RED (test first) | GREEN | REFACTOR |
|---|---|---|---|
| 1.1/1.2 serie regex | `test_pdf_texto.py::test_serie_alfanumerica_sunat_f96x_produce_clave` failed (`clave is None`) | `_SERIE_NUMERO_RE` widened (D8) + docstring fixed → 15 passed | none |
| 2.1–2.8 pure containment | `test_comprobante.py` import of `asociar_por_nombre_archivo` failed (ImportError) | `Documento.nombre_archivo`, `asociar_por_nombre_archivo`, `_tokens`, `_nombre_confirma_clave`, `_hay_representantes_distintos` → 21 passed | renamed `l`→`lista` (E741) |
| 3.1–3.5 repo/CLI wiring | `test_procesamiento_repo.py` 5-tuple rows → 6-tuple + `dr.nombrearchivo` assert failed; `test_cli_procesamiento.py::test_segunda_pasada_asocia_pdf_sin_clave_por_containment_del_nombre` failed (no assoc updates) | `_LISTAR_HUERFANOS` + `dr.NombreArchivo`, `listar_huerfanos` maps it, `_asociar_pendientes` residue + 2nd pass concat | none |
| 4.1–4.6 PDF-only re-emit | `test_inbox_event_repo.py` import of `listar_asociacion_no_notificada`/`insertar_evento_asociacion` failed; `test_cli_inbox.py::test_asociacion_tardia_de_pdf_reemite_un_evento_con_la_asociacion` failed | `_LISTAR_ASOCIACION_NO_NOTIFICADA` (D6 predicate), `_INSERTAR_EVENTO_ASOCIACION` (payload-aware NOT EXISTS), `_fila_a_procesamiento` extract, `cli_inbox` 2nd batch loop w/ injected insert fn | `_publicar_evento` parametrized with `insertar` fn (both passes reuse it) |
| 4.7 integration | tests written, `pytest -m integracion -k containment/reemision` → 3 skipped (no SQL Server) | n/a | n/a |

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused unit command + result | `pytest -m "not integracion and not externa and not ocr"` → **285 passed, 28 deselected** (was 266 before batch; +19 new unit tests) |
| Lint | `ruff check src tests` → **11 errors, all pre-existing E501** in `test_outbox_contrato_bidireccional.py` / `test_schema_020_outbox_clasificacion.py` / `test_payload_inbox_contract.py` (identical to pre-batch baseline via `git stash` check). Zero in changed files. |
| Runtime harness command + result | `pytest -m integracion` → **NOT RUN, 3 skipped** (no disposable SQL Server). User must run: `cd SmartNet/SmartNetWorker && pytest -m integracion` (and `-m ocr` unaffected). |
| Rollback boundary | Revert the 6 `src/smartnet_worker/*.py` + their 6 unit test files + `tests/integration/test_pyodbc_integracion.py` + `adrs/0017-frontera-del-motor-de-extraccion.md`. Already-written FKs stay valid (indistinguishable from exact-path FKs). No schema/.NET change. |

## Files Changed

| File | Action | What |
|---|---|---|
| `src/smartnet_worker/pdf_texto.py` | Modified | `_SERIE_NUMERO_RE` widened to SUNAT alphanumeric with negative lookahead (D8); module docstring "sanitized" claim corrected (design BLOCKING CORRECTION / CLAUDE.md rule 1). |
| `src/smartnet_worker/comprobante.py` | Modified | `_NO_ALFANUM_RE`; `Documento.nombre_archivo: str \| None = None`; `_tokens`, `_hay_representantes_distintos` (SDR), `_nombre_confirma_clave`, `asociar_por_nombre_archivo`. Stays pure — only `re`/`collections`. |
| `src/smartnet_worker/procesamiento_repo.py` | Modified | `_LISTAR_HUERFANOS` SELECT gains `dr.NombreArchivo`; `listar_huerfanos` unpacks 6 cols, threads `nombre_archivo` into `Documento`. |
| `src/smartnet_worker/cli_procesamiento.py` | Modified | import `asociar_por_nombre_archivo`; `_asociar_pendientes` computes residue (huerfanos minus exactly-paired ids), runs 2nd pass, concats `Par` tuples, single existing write loop. Exact path byte-unchanged. |
| `src/smartnet_worker/inbox_event_repo.py` | Modified | `_LISTAR_ASOCIACION_NO_NOTIFICADA` (D6: `DocumentoAsociadoId IS NOT NULL AND dr.TipoDocumento='PDF' AND NOT EXISTS(... JSON_VALUE ... IS NOT NULL)`); `_INSERTAR_EVENTO_ASOCIACION` (same NOT EXISTS in WHERE); `listar_asociacion_no_notificada`, `insertar_evento_asociacion`, `_fila_a_procesamiento` helper. Existing `_INSERTAR_EVENTO` / `_LISTAR_NO_NOTIFICADOS` untouched. SELECT/INSERT only. |
| `src/smartnet_worker/cli_inbox.py` | Modified | import new repo fns; 2nd batch loop after 1st (separate read connection); `_publicar_evento` gains `insertar` param, reused by both passes; re-emit payload = `construir_payload` with `documento_asociado_id` populated → `advertenciasAsociacion` recomputes to `[]`. `_VERSION` unchanged. |
| `tests/unit/test_pdf_texto.py` | Modified | +3 tests (F96X, F001-1 regression, prose rejection). |
| `tests/unit/test_comprobante.py` | Modified | +`_par` helper, +10 tests for `asociar_por_nombre_archivo`. |
| `tests/unit/test_procesamiento_repo.py` | Modified | huerfanos fixtures → 6-tuples; assert `dr.NombreArchivo` in SQL + `nombre_archivo` on `Documento`. |
| `tests/unit/test_cli_procesamiento.py` | Modified | huerfanos fixtures → 6-tuples; fake cursor branch for associate UPDATE; +2 tests (2nd-pass containment, exact-path regression). |
| `tests/unit/test_inbox_event_repo.py` | Modified | +2 tests (candidate query PDF-only + payload-aware guard, `insertar_evento_asociacion` SQL/params). |
| `tests/unit/test_cli_inbox.py` | Modified | fake cursor: disambiguate the two SELECTs + tag `insertar_evento_asociacion`; +2 tests (late PDF re-emit carries assoc, no-XML-side). |
| `tests/integration/test_pyodbc_integracion.py` | Modified | +`asociar_por_nombre_archivo`/`listar_huerfanos`/re-emit imports; +2 integration tests (containment pass data-partition; PDF-only candidate query + no-repeat). NOT RUN (no SQL Server). |
| `adrs/0017-frontera-del-motor-de-extraccion.md` | Modified | §"Asociación PDF ↔ XML" ¶"Recuperación" replaced per design D9 (two forms, containment vs XML authority, bilateral 1:1 global exclusivity, fails safe); Alternativas parenthetical; +1 Consecuencias bullet; Estado → "Revisión 3". `adrs - v2/0017-*.md` UNTOUCHED. |

## Deviations from design

1. **Spec parenthetical vs design D2 on `01230`** (task 2.4). Spec scenario name lists "1230 vs 12300/01230" as near-miss. But design D2's normalization rule (`normalizar_numero` strips leading zeros — the *same* rule that makes `00001230` match, D2 table) makes `01230` == número `1230`. Treating `01230` as a genuine match; only `12300` (→ `12300` ≠ `1230`) is the near-miss. Deliberate spec-vs-code reconciliation per CLAUDE.md rule 1. Test `test_token_casi_igual_no_matchea` checks `12300`; added `test_numero_con_ceros_a_la_izquierda_si_matchea` for `00001230`. **Spec text should drop `01230` from the near-miss parenthetical.**
2. Task 3.3 ("thread `nombre_archivo` into the `Documento` build in `cli_procesamiento.py`") — the `Documento` for orphans is built inside `procesamiento_repo.listar_huerfanos`, not `cli_procesamiento`. Threading done there (3.2). `cli_procesamiento` only imports the new pure fn and computes the residue. No functional gap.

## Deferred to owner (task 5.2)

`BACKLOG.md` NOT edited (owner-managed). Draft for **item #26**:

> **#26 — Guarda de unicidad de `Factura` por identidad de comprobante.** `PromocionBackgroundService`
> llama `ExisteIdentidadPreviaAsync` sólo para *fijar un indicador* (`PromocionBackgroundService.cs:102-105`),
> no como restricción: nada impide una segunda `fact.Factura` con el mismo RUC+tipo+serie+número.
> El ciclo `asociacion-pdf-clave-desde-xml` evita el problema no re-emitiendo el evento del lado XML
> (design D5), pero la duplicación sigue siendo posible por otras vías (re-scan, doble evento). Añadir
> una UQ sobre la identidad del comprobante en `fact.Factura` o un chequeo transaccional en
> `PromoverAsync`. Depende de: decisión sobre qué hacer con el duplicado detectado (rechazar / fusionar /
> marcar para revisión). ADR 0017 rev. 3 lo menciona como follow-up.

## Remaining

- [~] 5.2 BACKLOG #26 → owner
- [~] 6.2 / 4.7 `pytest -m integracion` → user (needs disposable SQL Server + dotnet)
