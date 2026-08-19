# Tasks: Ingesta Gmail (BACKLOG #5)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1250–1450 (WU1 migration+gmail.py+tests ~470; WU2 gmail_client+almacenamiento+config ~165; WU3 documento_repo+estado_integracion (breaks #4) ~205; WU4 cli_gmail+integration tests+docs ~405) |
| 400-line budget risk | High — WU1 and WU4 each individually approach/exceed the 400-line budget, same shape as item #4's forecast |
| Chained PRs recommended | Yes |
| Suggested split | WU1 → WU2 → WU3 → WU4 (four PRs, strictly sequential) |
| Delivery strategy | ask-on-risk — this forecast flags risk, so chained delivery is a stop-and-ask, not a silent decision |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Migration 013 (`ETIQUETA_PROCESADO` + `UNIQUE(EmailId, HashContenido)`) + `gmail.py` (pure: query, parse, candidacy, hash, sanitize, path) + its adversarial unit suite | PR 1 | `pytest tests/unit/test_gmail.py` | `dotnet test SmartNet.Db.Runner.Tests` (`BaseDataTests` InlineData) against a fresh `fact_test_<id>` | Revert `013_*.sql`/`013_down.sql`; delete `gmail.py`, `test_gmail.py`, fixtures; revert `BaseDataTests.cs` |
| 2 | `gmail_client.py` (IO: labels/messages/attachments/modify) + `almacenamiento.py` (IO: contained write) + `config.py` Gmail env vars + `pyproject.toml` deps | PR 2 | No dedicated unit file (design routes their testing through WU4's fakes/`tmp_path`); smoke: `python -c "import smartnet_worker.gmail_client, smartnet_worker.almacenamiento"` | N/A — no network/disk call exercised standalone; real IO only proven once wired in WU4 | Delete `gmail_client.py`, `almacenamiento.py`; revert `config.py`/`pyproject.toml` diffs |
| 3 | `documento_repo.py` (cursor-based INSERTs) + `estado_integracion.py` generalized to required `nombre` (breaks #4's `cli_tipo_cambio.py` + its test) | PR 3 | `pytest tests/unit/test_documento_repo.py tests/unit/test_estado_integracion.py` | N/A — fake-cursor unit tests only, no real DB in this slice | Revert `documento_repo.py`, `estado_integracion.py`, `cli_tipo_cambio.py`, and their tests to pre-PR3 state |
| 4 | `cli_gmail.py` orchestrator, fixtures, `test_cli_gmail.py`, structural + real-DB integration test updates, `README.md` | PR 4 | `pytest tests/unit/test_cli_gmail.py tests/unit/test_no_dbo_structural.py` (unit); `pytest -m integracion` (real pyodbc) | Ephemeral `fact_test_worker_<id>` + real `CREATE LOGIN usr_worker`, same harness item #4's WU3/WU4 built | Delete `cli_gmail.py`, `test_cli_gmail.py`, fixtures used only here; revert structural/integration test + README diffs |

---

## Phase 1 (WU1): Migration + `gmail.py` pure module

- [x] 1.1 Create `SmartNet/db/schema/013_configuracion_etiqueta_procesado.sql` — seed `INGESTA.ETIQUETA_PROCESADO` (`NOT EXISTS`-guarded) + `ALTER TABLE fact.DocumentoRecibido ADD CONSTRAINT UQ_DocumentoRecibido_Email_Hash UNIQUE (EmailId, HashContenido)` (`IF NOT EXISTS`-guarded), per design.md's migration content. Also regenerated `checksums.txt` via `generate-checksums.ps1` (13 entries).
- [x] 1.2 Create `SmartNet/db/schema/rollback/013_down.sql` — `DELETE` the key + `DROP CONSTRAINT`, advisory "CANNOT UNDO" note per 009's pattern.
- [x] 1.3 Modify `SmartNet/db/runner/.../BaseDataTests.cs` — add `[InlineData("INGESTA", "ETIQUETA_PROCESADO")]`; run to confirm the seeded row has `Valor`/`ValorPorDefecto` both `NULL`. Evidence: `dotnet test SmartNet.Db.Runner.Tests --filter "FullyQualifiedName~BaseDataTests|FullyQualifiedName~ChecksumManifestTests"` → 38/38 passed (includes the new InlineData case and `RealManifest_MatchesTheRealScripts_Exactly`).
- [x] 1.4 Create `SmartNet/worker/tests/fixtures/gmail_mensaje_simple.json` and `gmail_mensaje_multipart.json` — redacted real-shape `messages.get` responses (nested multipart/alternative + multipart/related with an inline empty-filename image, and a flat one-attachment message). All addresses/ids/content are synthetic and redacted, per fixtures/README.md's existing convention.
- [x] 1.5 RED: `SmartNet/worker/tests/unit/test_gmail.py::test_construir_consulta_*` — label with spaces quoted, `after:aaaa/mm/dd` reformatting from ISO date. RED evidence: `ModuleNotFoundError: No module named 'smartnet_worker.gmail'` (collection error, all 37 tests in the file failed to collect).
- [x] 1.6 RED (same file): `parsear_mensaje` over both fixtures (nested multipart walk, `internalDate`→UTC, missing `From`→`ParseoGmailError`, `Asunto`>500 truncated). Same RED evidence as 1.5 (single collection failure covering the whole file).
- [x] 1.7 RED (same file): `extensiones_permitidas`/`es_candidato` table — `pdf`✓, `.PDF`✓, `docx`✗, `factura.pdf.exe`✗, no-extension✗, empty `filename`✗, list with spaces/dots/empties. Same RED evidence as 1.5.
- [x] 1.8 RED (same file): `calcular_hash` — known vector `sha256(b"")`, 64 lowercase hex chars. Same RED evidence as 1.5.
- [x] 1.9 RED (same file), adversarial: `sanitizar_nombre_archivo`/`ruta_relativa` — `../../etc/passwd`, `..`, `.`, `....`, `C:\x`, `a:b`, `CON.pdf`, `NUL`, 300-char name, emoji-only name, two same-name-different-content attachments → distinct paths; path ≤400, component ≤255. Same RED evidence as 1.5.
- [x] 1.10 Confirmed all of 1.5–1.9 RED: `pytest tests/unit/test_gmail.py -q` → `ImportError ... ModuleNotFoundError: No module named 'smartnet_worker.gmail'`, 1 error during collection (`gmail.py` did not exist yet).
- [x] 1.11 GREEN: created `SmartNet/worker/src/smartnet_worker/gmail.py` — `AdjuntoGmail`, `MensajeGmail` dataclasses; `construir_consulta`, `parsear_mensaje`, `extensiones_permitidas`, `es_candidato`, `calcular_hash`, `sanitizar_nombre_archivo`, `ruta_relativa`, `ParseoGmailError`, per Interfaces/Contracts. Zero IO/DB/network/clock — same purity discipline as `sbs.py`.
- [x] 1.12 Confirmed `pytest tests/unit/test_gmail.py -q` fully GREEN: `37 passed in 0.14s`. Also `ruff check src/ tests/` → `All checks passed!`, and full unit suite `pytest tests/unit -q -m "not integracion and not externa"` → `55 passed in 0.24s` (no regression in item #4's SBS/estado_integracion/structural tests).

## Phase 2 (WU2): Gmail/storage IO + config/deps

- [x] 2.1 Modify `SmartNet/worker/pyproject.toml` — added `google-api-python-client>=2.140`, `google-auth>=2.34` to `dependencies`; registered `smartnet-gmail = "smartnet_worker.cli_gmail:main"` under `[project.scripts]` (target module ships in WU4; `pip install -e ".[dev]"` does not validate entry-point targets at install time — confirmed by a clean install in this WU).
- [x] 2.2 Modify `SmartNet/worker/src/smartnet_worker/config.py` — added `GMAIL_CREDENTIALS_ENV_VAR` (`SMARTNET_WORKER_GMAIL_CREDENTIALS`), `STORAGE_ROOT_ENV_VAR` (`SMARTNET_WORKER_STORAGE_ROOT`), `GMAIL_SCOPES=["https://www.googleapis.com/auth/gmail.modify"]`, plus `obtener_credenciales_gmail_json()` (parses the env JSON, `ConfiguracionError` on missing/malformed) and `obtener_raiz_almacenamiento()` (`ConfiguracionError` on missing) — no default in code, same shape as the pre-existing `obtener_connection_string`. No dedicated `test_config.py` added: item #4 never unit-tested `obtener_connection_string`'s own error branch either (verified: no such file exists pre-WU2), so this mirrors the established convention rather than introducing a new one.
- [x] 2.3 Create `SmartNet/worker/src/smartnet_worker/gmail_client.py` — `ClienteGmail` with `resolver_etiquetas`, `buscar_mensajes` (paginated via `nextPageToken`), `obtener_mensaje`, `obtener_adjunto` (base64url decode), `aplicar_etiqueta`; one method per API call, zero decision/parsing beyond mechanical pagination looping (design Decision 1 & 3 — missing-label handling is the *caller's* job, not this class's). No dedicated unit test file, per design's Testing Strategy (exercised via WU4's fake `ClienteGmail`).
- [x] 2.4 Create `SmartNet/worker/src/smartnet_worker/almacenamiento.py` — `escribir(raiz, ruta_relativa, datos)` with `resolved.is_relative_to(raiz)` containment guard (`ContencionError`), defense in depth beyond `gmail.py`'s sanitizer. **Deviation from the WU2 forecast's "no dedicated unit file"**: the containment guard is genuine decision logic (raise vs. write), not pass-through IO, so strict TDD applies per this session's explicit instruction. RED: `SmartNet/worker/tests/unit/test_almacenamiento.py` written first → `ModuleNotFoundError: No module named 'smartnet_worker.almacenamiento'` (1 collection error). GREEN: `pytest tests/unit/test_almacenamiento.py -q` → `4 passed` (happy path with intermediate dirs, idempotent same-path rewrite, relative-escape rejection, absolute-path-outside-root rejection — none of the negative cases write anything outside the root).
- [x] 2.5 Smoke check: both new modules import cleanly after `pip install -e ".[dev]"` pulled the new Gmail deps — `python -c "import smartnet_worker.gmail_client, smartnet_worker.almacenamiento"` → `ok`. `ruff check src/ tests/` → `All checks passed!`. Full non-integration unit suite: `pytest tests/unit -q -m "not integracion and not externa"` → `59 passed` (55 from WU1 + 4 new `test_almacenamiento.py`, no regression).

## Phase 3 (WU3): `documento_repo.py` + `estado_integracion.py` generalization (breaks item #4)

- [ ] 3.1 RED: `SmartNet/worker/tests/unit/test_documento_repo.py` — `insertar_email(cursor, m, fecha_deteccion)` returns an int id on success, `None` on fake `IntegrityError` (idempotency gate); exact SQL text + parameters, `fact.` qualified, `'CANDIDATO'` literal.
- [ ] 3.2 RED (same file): `insertar_documento(cursor, email_id, m, a, hash_hex, ruta_relativa)` — exact SQL/params, `'DESCARGADO'` literal, duplicate `(EmailId, HashContenido)` `IntegrityError` treated as no-op (design Decision 4).
- [ ] 3.3 Confirm 3.1–3.2 RED (`documento_repo.py` absent).
- [ ] 3.4 GREEN: create `documento_repo.py` per Interfaces/Contracts; catches `pyodbc.IntegrityError` on both inserts, never a pre-`SELECT`.
- [ ] 3.5 RED: modify `SmartNet/worker/tests/unit/test_estado_integracion.py` — `registrar_exito`/`registrar_fallo` now require `nombre: str`; add `Nombre='GMAIL'` case; `rowcount != 1` (including a name outside `CK_EstadoIntegracion_Nombre`) still raises. Confirm fails against the current `Nombre='SBS'`-hardcoded signature.
- [ ] 3.6 GREEN: modify `estado_integracion.py` — `nombre` becomes a required, parameterized (`WHERE Nombre = ?`) argument to both functions.
- [ ] 3.7 GREEN: modify `SmartNet/worker/src/smartnet_worker/cli_tipo_cambio.py` — pass `'SBS'` explicitly at both call sites (consequence of 3.6's new signature).
- [ ] 3.8 Confirm `pytest tests/unit/test_documento_repo.py tests/unit/test_estado_integracion.py tests/unit/test_cli_tipo_cambio.py` (or equivalent existing #4 test) all GREEN — no regression in item #4's SBS path.

## Phase 4 (WU4): `cli_gmail.py` orchestrator + integration + docs

- [ ] 4.1 RED: `SmartNet/worker/tests/unit/test_cli_gmail.py` — fake `ClienteGmail` + fake cursor: label applied only after commit; failed message left unlabeled and does not abort the run; message with no candidate attachments → 0 rows/0 writes/no label; `insertar_email → None` → no download, label reapplied (idempotent re-run); missing `ETIQUETA_ORIGEN`/`_PROCESADO` at Gmail → fails before `messages.list`. Use `tmp_path` for real file writes (exercises `almacenamiento.escribir`).
- [ ] 4.2 Confirm 4.1 RED (`cli_gmail.py` absent).
- [ ] 4.3 GREEN: create `cli_gmail.py` — config → `ClienteGmail` → per-message transaction (`insertar_email` → download/hash/write → `insertar_documento` → COMMIT → `aplicar_etiqueta`) → per-message failure isolation → `registrar_exito`/`registrar_fallo` (design Decision 7).
- [ ] 4.4 Confirm `pytest tests/unit/test_cli_gmail.py` fully GREEN.
- [ ] 4.5 RED: modify `SmartNet/worker/tests/unit/test_no_dbo_structural.py` — extend docstring/scans: no `dbo.` (existing), plus no `.delete(`/`.trash(`, plus no `fact.Factura`/`fact.AdjuntoManual`/`fact.Procesamiento`/`fact.DatosExtraidos` mention anywhere in `src/`. Confirm it fails or is a no-op pass documented as compression (source already clean).
- [ ] 4.6 GREEN/confirm 4.5 passes against the complete `src/smartnet_worker/` package.
- [ ] 4.7 RED (marker `integracion`): modify `SmartNet/worker/tests/integration/test_pyodbc_integracion.py` — real `usr_worker`: `insertar_email` inserts then duplicate `GmailMessageId` returns `None`; `insertar_documento` with a real FK; `EstadoIntegracion` `Nombre='GMAIL'` UPDATE affects 1 row; negative case: `UPDATE fact.Configuracion` fails under `usr_worker` (SELECT-only grant, 008).
- [ ] 4.8 Confirm 4.7 GREEN against a real ephemeral `fact_test_worker_<id>` database + real `usr_worker` login (same harness item #4 built); confirm zero orphaned test databases/logins after the run.
- [ ] 4.9 Modify `SmartNet/worker/README.md` — document `SMARTNET_WORKER_GMAIL_CREDENTIALS`, `SMARTNET_WORKER_STORAGE_ROOT`, and the `smartnet-gmail` command.
- [ ] 4.10 Confirm `.github/workflows/ci.yml` needs no change (design: `pytest` already discovers `tests/`, DbUp already applies `schema/*.sql` in lexical order) — verify by inspection, no diff expected.
- [ ] 4.11 `ruff check src/ tests/` clean pass over the full worker package.
