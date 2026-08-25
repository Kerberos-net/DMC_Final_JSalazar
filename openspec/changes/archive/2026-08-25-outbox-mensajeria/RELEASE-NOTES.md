# Release Note: Outbox y mensajería (BACKLOG #14)

This item ships two **visible behavior changes on existing endpoints**. Both were flagged in
`proposal.md` and `design.md` as changes that must be called out explicitly, not silently
discovered by a caller. Neither is a new business rule — both are ADR 0008's documented intent
("VALIDADA no puede descartarse", read together with its mirror "DESCARTADA no puede validarse")
finally taking effect now that `fact.Factura.Estado` becomes a live, observed column (D10).

## 1. `POST /descartar` on a `VALIDADA` factura now returns 409

**Before**: `DescartarAsync`'s `Estado == VALIDADA` guard (`ServicioDeFacturas.cs:292`) existed in
code but was unreachable — nothing before this item ever wrote `Estado = 'VALIDADA'`, so the guard
was permanently dead.

**After**: once `MarcarFacturaValidadaAsync` (D10) starts writing `Estado = 'VALIDADA'` during
`validar`, the guard goes live. Discarding a factura that has already been validated now returns
**409** (`application/problem+json`) instead of applying the discard.

**Who is affected**: only facturas validated *after* this deploy (D10 is forward-only, not
backfilled — see design.md Migration/Rollout). Any caller relying on discarding an already-validated
factura will start seeing 409 where it previously succeeded.

## 2. `POST /validar` on a `DESCARTADA` factura now returns 409 and rolls back

**Before**: `ValidarPorFacturaAsync` never read factura state, so the reachable production path
`abrir → descartar → validar` silently produced a `CONFIRMADO` asiento hanging off a discarded
factura — a contradiction the code allowed by omission.

**After**: `ValidarInternoAsync` now transitions through `MarcarFacturaValidadaAsync`, which returns
`NoTransicionable` for a `DESCARTADA` factura. That branch returns **409**
(`CasoConflicto.FacturaDescartada`) *before* `CommitAsync`; the ambient `IUnidadDeTrabajo` rolls
back, so the asiento stays `BORRADOR` and no `FACTURA_VALIDADA` event is emitted.

**Who is affected**: any caller currently relying on (or unknowingly triggering) the
`abrir → descartar → validar` path. This closes design.md's Open Question 5 (resolved by the
project owner: reject, don't preserve the reachable contradiction).

## Not a behavior change

All other outbox-mensajeria work (event emission for the other 3 catalog events, the Python
consumer, the `OBSOLETO` guard, and the bidirectional contract/permission tests) is new,
previously-nonexistent infrastructure with no consumer registered yet (items #15/#16 build the
first ones) — it has no observable effect on any existing caller.

## Regression evidence (task 6.1/6.2)

Closed suites for #7 (Inbox y promoción) and #11 (API de facturas y asientos) re-run after Phase 5,
zero regressions:

| Suite | Result |
|---|---|
| `SmartNet.Inbox.Core.Tests` | 49/49 passed |
| `SmartNet.Inbox.Infrastructure.Tests` (real schema) | 41/41 passed |
| `SmartNet.Facturacion.Core.Tests` | 88/88 passed |
| `SmartNet.Facturacion.Infrastructure.Tests` (real schema) | 46/46 passed |
| `SmartNet.Api.Tests` | 143/143 passed |
| `SmartNet.Db.Runner.Tests` — `PermissionMatrixTests` only | 27/27 passed |
| worker `pytest tests/unit` | 210/210 passed |

`PermissionMatrixTests.cs:254-309` (the outbox-related rows of item #11's permission matrix,
`UsrApi_CanInsertAndSelect_OutboxEvent_ButNotUpdate`,
`UsrWorker_CanSelectAndUpdate_OutboxEvent_ButNotInsert`,
`UsrApi_CanInsertAndSelect_OutboxEventIntegracion_ButNotUpdate`,
`UsrWorker_CanSelectAndUpdate_OutboxEventIntegracion_ButNotInsert`) is confirmed **untouched** by
this item — `git diff --stat` against it is empty and its last modifying commit
(`72b8bd5`) predates this item's work (BACKLOG #13). Item #14 does not duplicate this matrix
(design.md's explicit instruction: "the .NET side is already covered ... do not duplicate"); Phase
5's `test_usr_worker_no_puede_insertar_en_outboxeventintegracion` etc. are the Python-side mirror
of the same rows, run against the real `usr_worker`/`usr_api` logins for the first time (Phase 5).

The only assertions in the #7/#11 suites that changed shape (not count) are the 3 D9 sequence
assertions in `SmartNet.Facturacion.Core.Tests` (`ValidarAsync_...CommitsInOrder`,
`ValidarPorFacturaAsync_...`) extended in Phase 2 to assert the new `MarcarFacturaValidadaAsync`
call in the `Llamadas` sequence — already applied and green since batch 2, reconfirmed green here.
