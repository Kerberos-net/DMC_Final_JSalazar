# Proposal: Errores, notificaciones y operación (#17)

## Intent

ADR 0010 promises a full error-recovery loop, but it's incomplete: classification exists only on
ingesta (#6), and #11's recovery actions (`reprocesar`/`reconectar`/`sincronizar`) enqueue
`CommandQueue` rows nobody consumes. There is no proactive alert channel and no screen to configure
notification/integration thresholds. Today, an operator sees "Con error" in the SPA but has no
automated alert and clicking "reprocesar" does nothing.

## Scope

### In Scope
- Error-classification wrapper around `despacho_outbox.py::despachar_evento` (classify → persist
  `ProcesamientoError`/`ProcesamientoIntentos` → schedule retry), generic enough to not assume
  Drive/Sheets handler shapes (#15/#16 not yet built).
- `DIFERIBLE` producer: 429/Retry-After handling for the outbox path (ADR 0010 accepted cost).
- Telegram notifier + SMTP fallback, both attempts logged, triggered per class (TRANSITORIO on
  exhaustion, PERMANENTE immediate, DIFERIBLE once, OBSOLETO never) — an injected side effect
  separate from the pure `clasificar()` core (ADR 0019).
- Python `CommandQueue` consumer for `REPROCESAR`/`RECONECTAR`/`SINCRONIZAR` — closes the recovery
  loop #11 already exposes. Included per explicit owner decision.
- .NET endpoints to read/write `fact.Configuracion` by section/key, with per-`Tipo` validation.
- Angular `configuracion/` screen (feature + data-access).
- Confirm/extend `panel-errores` class distinction + reproceso action (TECH-DESIGN.md L663).
- Telegram/SMTP credentials read via existing `smartnet_worker/config.py` convention (env-based),
  same pattern already used for Gmail/Drive/Sheets.

### Out of Scope
- Deploying a secrets manager (Vault) or log aggregator — BACKLOG.md explicitly excludes this as
  deployment infra; only `EstadoIntegracion` (already built) belongs to #17.
- Drive/Sheets handlers (`REGISTRO_HANDLERS` stays empty) — #15/#16.
- `EstadoIntegracion` table/heartbeat/command-enqueue endpoints — already built (#6/#11/#14).
- New DDL — `ProcesamientoError`/`ProcesamientoIntentos` schema already exists.
- Any accounting-core change (`AsientoContable`, `REGLAS.md`) — this item is operational only.

## Capabilities

### New Capabilities
- `clasificacion-errores-outbox`: classification, retry scheduling, `DIFERIBLE` handling around
  outbox dispatch.
- `notificaciones-telegram-correo`: Telegram-primary/email-fallback notifier per error class.
- `consumidor-command-queue`: Python consumer executing queued recovery commands.
- `configuracion-api-spa`: .NET endpoints + Angular screen for `fact.Configuracion`.

### Modified Capabilities
- `panel-errores` (#13): possible delta if class distinction/reproceso action is incomplete —
  confirm during sdd-spec.

## Approach

Replicate the existing pattern: pure classification (`errores.py`-style) separate from injected
side effects (`estado_integracion.py`-style). Notification and the `CommandQueue` consumer
orchestrate around the pure core rather than being embedded in it, keeping ADR 0019's core
testable and avoiding a premature handler-interface commitment before #15/#16 exist.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `despacho_outbox.py` | Modified | Wrap dispatch with classification + persistence |
| `errores.py` | Modified | Add `DIFERIBLE` producer |
| `notificaciones.py` (new) | New | Telegram client + SMTP fallback, dual-attempt log |
| `consumidor_command_queue.py` (new) | New | Executes queued reprocesar/reconectar/sincronizar |
| `ConfiguracionEndpoints.cs` (new) | New | Read/write `fact.Configuracion` |
| `spa/app/configuracion/` (new) | New | Configuration screen |
| `spa/app/inbox/ui/panel-errores/` | Possibly Modified | Confirm class distinction + action |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Generic error-wrapper designed without a real Drive/Sheets handler to validate against | Medium | Keep contract minimal (exception in → classification out); revisit at #15/#16 |
| `CommandQueue` consumer scope creep (idempotency/lease vs. event consumption) | Medium | Reuse `reclamo.py` READPAST lease pattern where applicable |
| Endpoint home for `Configuracion` undecided | Low | Resolve in sdd-spec/design; default to new file |
| No hot-write secret rotation (env-based only) | Low | Explicitly deferred per BACKLOG.md until secrets infra exists |

## Rollback Plan

Each capability ships independently on top of existing production infra (main). Standard
`git revert` per capability; no new schema introduced (reuses `Configuracion`/`ProcesamientoError`),
so no schema-down risk.

## Dependencies

- #14 (Outbox y mensajería) — merged, provides the dispatch loop this item wraps.
- #11 (API de facturas y asientos) — merged, provides the command-enqueue routes this item consumes.
- #13 (bandeja e incidencias) — merged, provides `panel-errores` baseline.

## Success Criteria

- [ ] Outbox dispatch failures are classified, persisted, and scheduled for retry without
      handler-specific code.
- [ ] PERMANENTE errors trigger immediate Telegram alert with logged email fallback on failure.
- [ ] `POST /api/incidencias/{id}/reprocesar` results in observable worker-side re-execution.
- [ ] Operator can view/edit `fact.Configuracion` from the SPA and see it take effect.
- [ ] `errores.py` core remains testable without DB/HTTP/clock (ADR 0019 preserved).

## Proposal question round

This proposal was written non-interactively (delegated SDD executor turn, not a live chat with the
project owner). The following proposal-shaping questions are surfaced for owner review before
sdd-spec/sdd-design proceed:

1. **`CommandQueue` consumer idempotency**: if a `reprocesar` command is claimed but the process
   crashes mid-execution, reuse the `reclamo.py` READPAST lease pattern (5-min lease), or does a
   command need stricter at-most-once semantics given it can trigger side effects (e.g., resend to
   Drive)?
2. **Configuration endpoint boundary**: new `ConfiguracionEndpoints.cs` (single responsibility) or
   extend `IntegracionEndpoints.cs`? Default assumed: new file.
3. **Telegram destination scope**: single global chat (`TELEGRAM.DESTINO_CHAT_ID`, current schema)
   or per-integration/per-severity routing later? Affects whether the configuracion screen needs
   one field or a list.
4. **`DIFERIBLE` retry visibility**: when a DIFERIBLE error is deferred and later resolves, should
   panel-errores show a distinct "fue diferido y se resolvió" state, or is the current three-class
   distinction sufficient?

Assumptions used absent live answers: (1) reuse existing lease pattern unless spec/design finds a
concrete gap; (2) new `ConfiguracionEndpoints.cs`; (3) single global Telegram chat per existing
schema; (4) existing three-class distinction is sufficient. Owner should confirm or correct before
sdd-spec finalizes requirements.
