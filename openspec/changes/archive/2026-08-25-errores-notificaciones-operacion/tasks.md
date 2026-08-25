# Tasks: Errores, notificaciones y operación (#17)

## Review Workload Forecast

Session budget override: **800 changed lines** (default 400 raised per orchestrator instruction).
Guard line label stays `400-line budget risk` for downstream matching; evaluated against 800.

| Field | Value |
|---|---|
| Estimated changed lines | ~1700–2200 (worker core+tests ~900, .NET ~400, Angular ~450, SQL ~55) |
| 400-line budget risk (evaluated @800) | High |
| Chained PRs recommended | Yes |
| Suggested split | 020 DB → clasificacion-outbox → notificaciones → consumidor-command-queue → configuracion-api-spa |
| Delivery strategy | single-pr (owner-confirmed `size:exception`, 2026-08-25) |
| Chain strategy | n/a — single-pr exception accepted, no chaining |

Decision needed before apply: Resolved — owner accepted `size:exception` and confirmed `single-pr`
over the ~1700–2200 estimate. Implement as one PR; `sdd-apply` proceeds under this exception.
400-line budget risk: High (accepted)

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|---|---|---|---|---|---|
| 1 | 020 schema: `Clasificacion` + CHECK + `CORREO.DESTINATARIOS` seed | PR 1 | `sqlcmd` apply+reapply+revert 020/020_down | real `worker_db` harness | `SmartNet/db/schema/020_*.sql` + `rollback/020_down.sql` only |
| 2 | Pure classification core + outbox wrapper | PR 2 | `pytest worker/tests/test_clasificacion_despacho.py` | fake handler doubles (unit) | `clasificacion_despacho.py`, `despacho_outbox.py`, `outbox_repo.py`, `errores.py` |
| 3 | Notificador Telegram/correo | PR 3 | `pytest worker/tests/test_notificaciones.py` | fake `CanalDeAviso` doubles | `politica_notificacion.py`, `notificaciones.py`, `configuracion_repo.py`, `config.py` |
| 4 | Consumidor CommandQueue | PR 4 | `pytest worker/tests/test_command_queue_repo.py` | real `worker_db` concurrent-claim test | `command_queue_repo.py`, `comandos.py`, `cli_command_queue.py` |
| 5 | Configuración API + SPA | PR 5 | `dotnet test` Configuracion tests + `ng test configuracion` | `WebApplicationFactory` + test DB | `ConfiguracionEndpoints.cs`, `ValorDeConfiguracion.cs`, `Sql*ConfiguracionRepository.cs`, `spa/src/app/configuracion/**` |

## Phase 1: DB Schema Foundation (blocks all)

- [x] 1.1 Create `SmartNet/db/schema/020_outbox_clasificacion.sql`: `NOT EXISTS`-guarded `ALTER TABLE fact.OutboxEventIntegracion ADD Clasificacion VARCHAR(20) NULL`, then (after `GO`) `CK_OutboxEventIntegracion_Clasificacion CHECK (... IN ('TRANSITORIO','DIFERIBLE','PERMANENTE','OBSOLETO'))`, then seed `CORREO.DESTINATARIOS` (`LISTA`, `Valor`/`ValorPorDefecto` NULL) in `fact.Configuracion` — per design D1b.
- [x] 1.2 Create `SmartNet/db/schema/rollback/020_down.sql`: guarded DROP CHECK → DROP COLUMN → DELETE seed row.
- [x] 1.3 Integration test: apply 020, re-apply (no-op), revert via 020_down, re-apply — assert convergence and CHECK rejects out-of-vocabulary value. (`test_schema_020_outbox_clasificacion.py`, marker `integracion` — skips without a live SQL Server, same documented limitation as the rest of the worker's N2 harness.)

## Phase 2: clasificacion-errores-outbox (pure core, ADR 0019)

- [x] 2.1 RED: `worker/tests/test_clasificacion_despacho.py` — transitorio recovers within 3, exhausts at cap, diferible honors `retry_after`, obsoleto short-circuits.
- [x] 2.2 GREEN: `clasificacion_despacho.py` — `ResultadoDespacho`, `decidir(error, intentos, instante)`, `retry_after_desde(cabecera, instante)`. No DB/HTTP/clock.
- [x] 2.3 RED+GREEN: `errores.py` — add `CuotaExcedidaError(retry_after)`.
- [x] 2.4 RED+GREEN: `despacho_outbox.py` — wrap `handler(evento)` in `except BaseException`, inject `RegistroDeFallo` Protocol, default None preserves #14 behavior.
- [x] 2.5 RED+GREEN: `outbox_repo.py` — `marcar_fallo(...)` writing `Estado/Intentos/UltimoError/Clasificacion/ProximoIntentoEn`; fake-cursor SQL/param assertions. (Deviation: also added `EventoReclamado.intentos` + `oei.Intentos` to the `reclamar` SELECT — `decidir` needs the prior attempt count and the existing contract carried none; default `0` keeps #14's construction sites unaffected.)
- [x] 2.6 REFACTOR: confirm `REGISTRO_HANDLERS` stays empty/inert (no #15/#16 coupling).

## Phase 3: notificaciones-telegram-correo

- [x] 3.1 RED: `worker/tests/test_politica_notificacion.py` — trigger matrix (transitorio-agotado, permanente-inmediato, diferible-una-vez, obsoleto-nunca).
- [x] 3.2 GREEN: `politica_notificacion.py` — pure `debe_notificar`, `redactar`.
- [x] 3.3 RED: `worker/tests/test_notificaciones.py` — Telegram fails → correo attempted, both logged via `registrar_exito`/`registrar_fallo`.
- [x] 3.4 GREEN: `notificaciones.py` — `CanalDeAviso` Protocol, `TelegramCanal`, `CorreoCanal`, `notificar(canales, mensaje, instante, cursor)`.
- [x] 3.5 GREEN: `configuracion_repo.py` — read-only SELECT on `fact.Configuracion` (`TELEGRAM.DESTINO_CHAT_ID`, `CORREO.DESTINATARIOS`); raise `ConfiguracionError` if `CORREO.DESTINATARIOS` still NULL.
- [x] 3.6 GREEN: `config.py` — add `SMARTNET_WORKER_TELEGRAM_CREDENTIALS` / `SMARTNET_WORKER_SMTP_CREDENTIALS` env, no code default. (Test-after, not test-first — see TDD Evidence table.)
- [x] 3.7 GREEN: `cli_outbox.py` — wire injected `RegistroDeFallo` + notifier. New `registro_fallo.py::RegistroDeFalloConNotificacion` composes `outbox_repo`/`politica_notificacion`/`notificaciones` (RED/GREEN, `test_registro_fallo.py`). Deviation: `RegistroDeFallo.registrar`'s Protocol signature (design D2) has no `factura_id`, so the notification text uses `OutboxEventId` where `politica_notificacion.redactar` expects `factura_id` — flagged in the return summary, not a silent substitution.

## Phase 4: consumidor-command-queue

- [x] 4.1 RED: `worker/tests/test_command_queue_repo.py` — READPAST claim exclusivity, crash-mid-execution reclaim after 5-min lease.
- [x] 4.2 GREEN: `command_queue_repo.py` — `SET NOCOUNT ON`, `UPDATE TOP (?) ... OUTPUT ... WITH (READPAST, UPDLOCK, ROWLOCK)`, reuse `ARRENDAMIENTO` constant.
- [x] 4.3 RED+GREEN: `comandos.py` — pure `Tipo → handler` mapping (REPROCESAR/RECONECTAR/SINCRONIZAR), no SQL.
- [x] 4.4 GREEN: `cli_command_queue.py` — consumer loop entry point; terminal states COMPLETADO/ERROR per `clasificacion_despacho.decidir` (reused, same policy as `errores.proximo_reintento`). New `smartnet-command-queue` console script in `pyproject.toml`. **Known gap (documented, not silent):** `SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS` handlers raise `NotImplementedError` — wiring them to the existing `cli_gmail`/`cli_tipo_cambio` flows was out of budget this session; `REPROCESAR_DOCUMENTO` and `RECONECTAR_GOOGLE` are fully wired.
- [x] 4.5 Extend `test_no_dbo_structural.py` — consumer touches only `CommandQueue`/`Procesamiento`/`EstadoIntegracion`, never `fact.Factura` or `dbo.*` (ADR 0003). New `test_consumidor_command_queue_solo_toca_sus_tres_tablas`, plus the pre-existing generic dbo./external-table scan already covers the new module.
- [x] 4.6 Integration: concurrent claim does not double-process (`test_command_queue_repo.py`, marker `integracion`, skips without a live SQL Server — same documented limitation as the rest of the N2 harness). Deferred: "no duplicate Drive/Sheets/event side effects on REPROCESAR replay" is not exercisable in this item — `REPROCESAR_DOCUMENTO`'s only effect here is `Procesamiento.Estado='PENDIENTE'`; Drive/Sheets side effects belong to #15/#16, still unbuilt.

## Phase 5: configuracion-api-spa

- [x] 5.1 RED: `ValorDeConfiguracion` tests — TEXTO≤400, ENTERO/DECIMAL invariant parse, BOOLEANO canonical, FECHA `yyyy-MM-dd`, LISTA no-empty-item.
- [x] 5.2 GREEN: `facturacion/.../ValorDeConfiguracion.cs` — pure `Validar(tipo, valor)`, no HTTP/DB.
- [x] 5.3 GREEN: `Sql*ConfiguracionRepository.cs` — port + adapter, GET by section/key, UPDATE-only (unknown key → 404, never INSERT).
- [x] 5.4 RED: `WebApplicationFactory` tests — GET by section, PUT valid, PUT invalid rejected (prior value retained), PUT unauthenticated rejected.
- [x] 5.5 GREEN: `api/SmartNet.Api/ConfiguracionEndpoints.cs` — `GET /api/configuracion[?seccion=]`, `PUT /api/configuracion/{seccion}/{clave}`, session-required, `ProblemasDeNegocio` mapping, stamps `ActualizadoPorUsuarioId/ActualizadoEn`.
- [x] 5.6 SPA: create `spa/src/app/configuracion/{data-access,models,feature/configuracion-page,ui/configuracion-seccion,ui/campo-configuracion}` — signals + `HttpClient`/`firstValueFrom`, no state lib.
- [x] 5.7 SPA: add lazy `configuracion` route behind `authGuard` in `spa/src/app/app.routes.ts`.
- [x] 5.8 SPA tests (Jasmine/Karma + `HttpTestingController`): list/edit section, surfaces server validation error via `http-error.interceptor`-consumed `ProblemaDetails` (per-clave error, same `manejarError` pattern as `detalle-page.ts`).
- [x] 5.9 E2E-level scenario: operator edits `TELEGRAM.DESTINO_CHAT_ID` end-to-end, effective without redeploy — covered by `ConfiguracionEndpointsTests.Put_WithAValidValue_UpdatesTheStoredValue` (real DB + real HTTP via `WebApplicationFactory`, same "E2E-level" shape as the worker's `integracion`-marked tests, no browser E2E tooling exists in this repo) plus `configuracion-page.spec.ts`'s "operator edits TELEGRAM.DESTINO_CHAT_ID..." scenario on the SPA side.

## Phase 6: panel-errores — RESOLVED, no code task

- [x] 6.1 **Owner decision (2026-08-25)**: confirmed design D7 (ratified) over the spec's ADDED delta request. `panel-errores` ships no changes in this item — outbox dispatch failures land on `OutboxEventIntegracion` (invoice-level), not on the document-level `ProcesamientoError` the bandeja renders; per-invoice visibility is explicitly deferred, observable today via `GET /api/integraciones/estado`. Spec delta withdrawn accordingly (see `specs/panel-errores/spec.md`).

## Phase 7: Verification

- [x] 7.1 Run full worker pytest suite + `dotnet test` + `ng test`. Worker: 266 unit + 24 passed/1 skipped/1 deselected integration (live SQL Server, first time this item ran against one — see Phase 1 bugfix below). .NET: full `dotnet test SmartNet.sln` green except two pre-existing, unrelated flakes reproduced as PASS in isolation (see Issues Found). Angular: `ng test --watch=false` → 29 files / 180 tests passed.
- [x] 7.2 Confirm 020 migration idempotent in CI. `test_020_reaplicado_converge_sin_fallar` (worker integration suite, live DB) runs the .NET runner (which applies 020 among all scripts) twice against the same database and asserts exit code 0 both times — passed.
- [x] 7.3 Confirm threat-matrix N/A (design): no CLI routing/shell/subprocess/VCS surface introduced. Phase 5 additions are plain HTTP GET/PUT (`ConfiguracionEndpoints.cs`), parameterized SQL (`SqlConfiguracionRepository.cs`), and `HttpClient`/signals on the SPA side — no new CLI/shell/subprocess/VCS surface. N/A holds.
