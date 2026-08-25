# Verification Report: Errores, notificaciones y operación (#17)

**Verdict**: PASS (re-verification confirms both prior CRITICAL findings closed; no regressions)

## Re-verification Context

This is a second verify pass. The first pass (superseded content below the line) found 2 CRITICAL
findings blocking archive. Both were corrected by the orchestrator directly (not via a fresh
sdd-apply sub-agent run) and are re-confirmed independently in this pass.

## Gap 1 — spec.md persistence target (CLOSED, confirmed)

`specs/clasificacion-errores-outbox/spec.md`'s "Error and retry persistence" requirement was
reread in full. It now reads: persistence lands on `fact.OutboxEventIntegracion`
(`Estado`/`Intentos`/`UltimoError`/`Clasificacion`/`ProximoIntentoEn`), explicitly **not**
`fact.ProcesamientoError`, with the D1 rationale inlined (outbox has no `ProcesamientoId` to
satisfy that table's `NOT NULL` FK; deriving one would require reading `fact.Factura`, denied to
`fact_worker` under ADR 0003). This matches design.md's ratified D1 verbatim and matches the
actual implementation in `outbox_repo.py::marcar_fallo`, which writes exactly those five columns
on `OutboxEventIntegracion`. Spec/design/code are now consistent. CLOSED.

## Gap 2 — checksums.txt regeneration (CLOSED, confirmed)

Ran independently: `dotnet test --filter "FullyQualifiedName~ChecksumManifestTests"` in
`SmartNet/db/runner/SmartNet.Db.Runner.Tests` →

```
Correctas! - Con error: 0, Superado: 6, Omitido: 0, Total: 6, Duración: 79 ms
```

6/6 pass, deterministic (no DB dependency in this test). `git diff` on `checksums.txt` shows only
hash-value changes for `018_permiso_lectura_procesamiento_error.sql`, `019_permiso_secuencia_seqoutbox.sql`
(previously stale/incorrect hashes recomputed), plus a new line for
`020_outbox_clasificacion.sql`. `git diff --stat` on the two 018/019 `.sql` files themselves is
empty — content untouched, only the manifest was regenerated. CLOSED.

## Regression Sanity Check

`git status --short` shows no changes since the prior verify pass other than
`openspec/changes/errores-notificaciones-operacion/specs/clasificacion-errores-outbox/spec.md`
(text fix) and `SmartNet/db/schema/checksums.txt` (regenerated manifest). No application/worker/API
code changed. Reread the remaining four specs
(`notificaciones-telegram-correo`, `consumidor-command-queue`, `configuracion-api-spa`,
`panel-errores`) end to end — all still consistent with design.md and with the implementation
evidence gathered in the first verify pass. `panel-errores`'s explicit "no delta" resolution (D7)
still holds with zero code changes. Full re-run of the entire live-DB test matrix was not repeated
since no source outside the two gap-closing files changed; this is consistent with the skill's
graceful-degradation intent for a narrow re-verification, not a full re-apply.

## Completeness (tasks.md)

35/35 tasks marked `[x]`, unchanged from the first pass. No unchecked tasks.

## Spec Compliance (unchanged from first pass except Gap 1)

- `clasificacion-errores-outbox`: pure core PASS; TRANSITORIO/DIFERIBLE/OBSOLETO PASS; error/retry
  persistence requirement now textually correct (Gap 1) and PASS against implementation.
- `notificaciones-telegram-correo`: PASS, full coverage.
- `consumidor-command-queue`: PARTIAL/documented — SINCRONIZAR_GMAIL/SBS `NotImplementedError`,
  documented not hidden (accepted debt, see below); REPROCESAR/RECONECTAR + lease semantics PASS.
- `configuracion-api-spa`: PASS.
- `panel-errores`: PASS — zero code delta, consistent with D7.

## ADR Compliance

- ADR 0019 (pure core): PASS.
- ADR 0003 (data partition): PASS.
- ADR 0016 (SQL versionado): PASS — `020_outbox_clasificacion.sql` + rollback are pure guarded
  T-SQL, idempotent; checksums manifest now correctly regenerated (Gap 2 CLOSED).

## CRITICAL Issues

None. Both prior CRITICAL findings are confirmed closed by independent re-inspection and live test
execution in this pass.

## WARNING (accepted, non-blocking)

1. `dotnet test SmartNet.sln` (full solution) is non-deterministic under local SQL Server
   parallelism/contention — pre-existing repo-wide debt, not introduced by item #17, this item's
   own tests pass in isolation (7/7 + 7/7 + 6/6). Not blocking archive.

## SUGGESTION (accepted debt, documented not hidden, non-blocking)

2. `SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS` are explicit `NotImplementedError` with no wiring —
   documented in `cli_command_queue.py` docstring and `tasks.md` 4.4. Accepted: out of scope until
   the underlying integrations (#15/#16) exist.
3. DIFERIBLE dedupe is best-effort (read-then-write race on `(OutboxEventId, Integracion)`, no
   lock) — documented in `outbox_repo.py` docstring and `design.md` "Riesgos abiertos". Accepted:
   5-minute READPAST lease makes the window unlikely; failure mode is a duplicate alert, not
   incorrect data.
4. Wrapper contract (`decidir`/handler raises on failure) is unvalidated against a real Drive/Sheets
   handler — `REGISTRO_HANDLERS` is empty until #15/#16. Documented in `design.md` "Riesgos
   abiertos" with an explicit mitigation instruction (contract test required when #15/#16 land).

## Recommendation

Both CRITICAL findings from the first verify pass are independently confirmed closed. No new gaps
found. Accepted debt (items 1–4 above) is documented, non-blocking, and does not gate archive.
`next_recommended`: `sdd-archive`.
