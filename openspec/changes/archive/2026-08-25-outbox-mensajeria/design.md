# Design: Outbox y mensajería (BACKLOG #14)

## Technical Approach

Producer stays mechanical in Core: **four** new `EmitirOutboxAsync` calls plus a retrofit of the two
pre-existing ones, all six payloads built by a single Core serializer (`PayloadOutbox`) fed from
existing `IUnidadDeTrabajo` reads (ADR 0019 — port, not infrastructure). One non-mechanical addition:
`ValidarInternoAsync` now also drives `fact.Factura.Estado` to `VALIDADA` in its own transaction
(D10), which is what makes the emission predicate of `DOCUMENTACION_ACTUALIZADA`/`FACTURA_CORREGIDA`
true in production for the first time. Infrastructure closes the missing enabler:
`SqlUnidadDeTrabajo.EmitirOutboxAsync` also inserts the `OutboxEventIntegracion` fan-out rows in the
same transaction (today it does not — see D3). Consumer is five new Python modules: `READPAST`
confined to one repo, a pure obsolescence guard before dispatch, and a single-run CLI scheduled
externally each minute.

Decisions D3, D4, D5, D6 and now **D10** are architectural enough to leave the change folder — they
are recorded as **ADR 0020** (`adrs/0020-reclamo-de-lote-y-guarda-de-obsolescencia.md`, Propuesto,
**revisión 2**). This revision *does* change ADR 0020: the owner's resolution of Open Question 4 adds
a fifth decision (the state-transition port member) plus its consequences. The lease number resolved
by Open Question 3 lands inside ADR 0020's existing decision 2, not as a new one.

## Architecture Decisions

### D1 — `ASIENTO_CORREGIDO` is emitted at reconfirm, not at reopen

**Choice**: emit in `ServicioDeFacturas.ValidarInternoAsync` (`:110`, beside `FACTURA_VALIDADA`) when
`persistido.NumeroAsiento is not null` before the write — the only route back to BORRADOR from
CONFIRMADO is `ReabrirAsync`, so a pre-existing `NumeroAsiento` *is* "reconfirmed after reopening".
**Rejected**: emitting in `ServicioDeAsientos.ReabrirAsync`.
**Rationale**: ADR 0004's catalog says "Reconfirmar un asiento tras reapertura". Reopening only moves
to BORRADOR — no corrected fact exists yet, and a reopen never reconfirmed would publish a lie.

### D2 — One envelope for **all five** events, built by re-reading through the port (OQ 1 resolved)

**Choice**: `PayloadOutbox` (Core, `System.Text.Json` — same precedent as
`ServicioDeAsientos.SerializarLineas` and `payload_inbox`) is the sole producer of every payload,
including the retrofit of `FACTURA_VALIDADA` and `DOCUMENTACION_ACTUALIZADA`, which today pass bare
scalars (`numeroAsiento`, `NombreArchivo`, `adjuntoId.ToString()`).
**Rejected**: (a) leaving the two old events as-is — the owner closed this; (b) passing the
in-memory post-write records (`confirmado`, `actualizada`) into the serializer.
**Rationale for (b)**: the transaction sees its own writes, so a re-read *is* the state that will
commit; passing in-memory records risks a payload describing state the CAS write did not actually
produce. One code path, one golden shape, no per-event branching.

`ConstruirAsync` is the only member that touches the port; `Serializar` is pure and is what the
golden-fixture test exercises:

```csharp
// Core, SmartNet.Facturacion.Core/PayloadOutbox.cs
internal static async Task<string> ConstruirAsync(
    IUnidadDeTrabajo uow, string tipo, long facturaId, long? asientoContableId, CancellationToken ct);
internal static string Serializar(EnvolturaOutbox envoltura);   // pure, no I/O
```

`asientoContableId` is passed explicitly by the two asiento-rooted sites; the others pass `null` and
the collector resolves the vigente asiento. **This is load-bearing for `ASIENTO_ANULADO`**:
`ObtenerAsientoVigenteIdAsync` excludes ANULADO, so resolving "vigente" after the annulment write
would return `null` for the very event that is about that asiento.

A `null` factura at collect time is an impossible state (FK); `ConstruirAsync` throws
`InvalidOperationException` rather than emit a hollow payload — inside the transaction, so it rolls
back.

### D8 — Four emission points, at most one event per `(Tipo, FacturaId)` per transaction (OQ 2 resolved)

**Choice**: `ConfirmarAfectacionAsync` (`ServicioDeFacturas.cs:384`) is a fourth `FACTURA_CORREGIDA`
site alongside `PatchAsync` (`:219`). Both emit **iff** the transaction actually changed persisted
factura state **and** the factura counts as already-validated: `PatchAsync` iff `entradas.Count > 0`
(its `Auditar` already skips unchanged values — a no-op PATCH emits nothing);
`ConfirmarAfectacionAsync` iff `factura.AfectacionMixta != esMixta` (it audits unconditionally today,
by design; the *event* is a business fact and a resubmitted identical value changed nothing).
**Rejected**: emitting per changed field; emitting unconditionally on `ConfirmarAfectacionAsync`.

**Double emission cannot occur across the two sites** — this is structural, not a convention: each
public method opens its own `IUnidadDeTrabajo` via `_store.AbrirAsync` (one transaction per command,
`ServicioDeAsientos.cs:9-10`), they are reachable only from two distinct routes
(`PATCH /api/facturas/{id}` and `POST /api/facturas/{id}/confirmar-afectacion`,
`FacturaEndpoints.cs:22,26`), and each requires its own `If-Match` ETag. Since both writes bump
`fact.Factura.Version`, a client cannot even fire both with the same ETag — the second gets 412. Two
transactions therefore mean two corrections and two events, which is correct, not duplication.

To keep that true under future refactors, `SqlUnidadDeTrabajo` holds a per-transaction
`HashSet<(string Tipo, long FacturaId)>` and **throws** on a repeat instead of inserting a second
row. Fail-loud inside the transaction rolls back; a silent dedupe would hide the design error and
could swallow a legitimately distinct event. `FakeUnidadDeTrabajo` mirrors the guard so unit tests
catch it without a database.

### D9 — The retrofit's cost is paid in three exact-sequence assertions, deliberately

**Choice**: keep every *behavioural* assertion of items #7/#11 byte-identical (result type, audit
entries, `Committed`, saved state, event `Tipo`/`FacturaId`) and extend the three tests that assert
the exact `Llamadas` order, because a self-sufficient payload cannot be built without reading the
factura and the document list.
**Rejected**: a "compatibility" payload that omits the extra reads — that is the ADR 0004 violation
the owner just closed.

| Closed test | File:line | Delta |
|---|---|---|
| `ValidarAsync_...CommitsInOrder` | `ServicioDeFacturasTests.cs:147-156` | +4 reads before `EmitirOutboxAsync` |
| `ValidarPorFacturaAsync_...` | `ServicioDeFacturasPhase2Tests.cs:263-273` | +4 reads |
| `ConfirmarAfectacionAsync_WhenApplied_...` | `ServicioDeFacturasPhase2Tests.cs:354-362` | +4 reads, +`EmitirOutboxAsync` |

Two `FakeUnidadDeTrabajo` changes are required and are test-double fidelity fixes, not behaviour
changes: (1) `FacturaACargar` needs a non-null default (every `ValidarAsync` test sets only
`AsientoACargar`; tests that need absence already set `null` explicitly — verified at
`Phase2Tests.cs:37,313`); (2) `CargarFacturaAsync`/`CargarAsientoAsync` must return
`UltimaFacturaGuardada ?? FacturaACargar` / `UltimoAsientoGuardado ?? AsientoACargar`, so the fake
models "the transaction sees its own writes" — otherwise the golden payload would carry
`numeroAsiento: null` for `FACTURA_VALIDADA`.

### D3 — Fan-out rows are written by .NET Infrastructure, only for applicable destinations

**Choice**: `SqlUnidadDeTrabajo` inserts one `OutboxEventIntegracion` row per `(Tipo → Integracion)`
pair from an ADR-0004-sourced applicability map held in Infrastructure.
**Rejected**: consumer-created child rows; Core-held map.
**Rationale**: `fact_worker` has **no INSERT** on that table (008:110-111) — only `fact_api` can. A
map in Core would leak destination knowledge into the accounting core. Creating rows only where the
event applies satisfies ADR 0004's "marcar aplicado sin avanzar la secuencia" **by construction**.

### D4 — Lease-based claim on `ProximoIntentoEn`, **5 minutes** (OQ 3 resolved)

**Choice**: short claim transaction sets `ProximoIntentoEn = ahora + arrendamiento`; dispatch runs
outside it; a second short transaction writes the terminal state. The lease is **5 minutes**,
confirmed by the owner, as a single named constant `ARRENDAMIENTO = timedelta(minutes=5)` in the pure
`reclamo.py` (imported by `outbox_repo.py`, referenced by the reclaim-after-lease test) — not a
literal inside the claim SQL, and not a `reclamar()` parameter, so the `ReclamoDeLote` Protocol
signature stays as published.
**Rejected**: holding the claim transaction across dispatch (`UPDLOCK` for the whole cycle).
**Rationale**: `CK_OutboxEventIntegracion_Estado` has no `EN_PROCESO` and schema change is out of
scope. Holding a SQL transaction across a Drive/Sheets call (#15/#16) is unacceptable.
**Why 5 minutes** — it is bounded on both sides: it must exceed the 1-minute cadence by a wide margin
so a slow run is never double-claimed by the next tick, and it must stay well under ADR 0005's
15-minute visibility budget so a process that dies mid-dispatch still frees the row within that
budget (5 min lease + next 1-min tick ≈ 6 min worst case). Between those bounds the number is a
latency bet on #15's Drive handler; if that handler ever exceeds 5 minutes per event the constant is
the only thing that moves, and the test that pins it will say so.

### D5 — Guard is a pure verdict before dispatch, structurally disjoint from #17

**Choice**: `guarda_obsolescencia.evaluar(secuencia, progreso) -> Vigente | Obsoleto`, pure, no I/O,
never raises. `Obsoleto` → `marcar(..., 'OBSOLETO')` and return; handler is never called.
**Rejected**: an `EventoObsoleto` exception caught by the dispatcher.
**Rationale**: #17 classifies **raised handler exceptions**. Disjoint by type, not by convention.

### D6 — `ReclamoDeLote` is a `typing.Protocol`, not an ABC or `I`-prefixed class

**Choice**: structural Protocol in `reclamo.py` (pure, no `pyodbc`); `outbox_repo.py` is the only
module containing `READPAST`. Python has no `I` prefix convention; the worker uses plain modules and
frozen dataclasses.

### D7 — Own CLI, own schedule

**Choice**: `cli_outbox.py`, single-run per invocation (`smartnet-outbox` script), scheduled
externally each minute — same shape as `cli_inbox.py`, a **separate** schedule entry.

### D10 — `MarcarFacturaValidadaAsync`: one hard-coded transition, state-CAS instead of ETag (OQ 4 resolved)

**Choice**: a single new port member with no version parameter and no state parameter:

```csharp
// Core, IUnidadDeTrabajo.cs
Task<TransicionEstadoFactura> MarcarFacturaValidadaAsync(long facturaId, CancellationToken ct);

public enum TransicionEstadoFactura { Aplicada, YaValidada, NoTransicionable }
```

```sql
-- Infrastructure, SqlUnidadDeTrabajo.cs. 'VALIDADA' is a LITERAL, never a parameter.
UPDATE fact.Factura SET Estado = 'VALIDADA'
WHERE FacturaId = @id AND Estado = 'PENDIENTE_VALIDACION';
-- @@ROWCOUNT > 0            -> Aplicada
-- 0, re-SELECT = 'VALIDADA' -> YaValidada          (reconfirm after reopen — see below)
-- 0, anything else / no row -> NoTransicionable    (today: only 'DESCARTADA')
```

**Rejected (a)**: reuse `GuardarFacturaAsync` after re-reading `Version` inside the transaction. The
CAS would be tautological — we would be comparing against a version we read microseconds earlier
under READ COMMITTED, so a concurrent PATCH between our `SELECT` and our `UPDATE` yields a **412 on a
`validar` request whose `If-Match` was for the asiento**, which is a lie to the client. Worse, that
`UPDATE` rewrites all eight editable columns from our snapshot, turning a state transition into a
full-row lost-update window.
**Rejected (b)**: have `SqlUnidadDeTrabajo.GuardarAsientoAsync` write the factura state whenever the
asiento lands `CONFIRMADO` — no new port member at all, impossible to misuse. Rejected because
"confirming an asiento validates its factura" is an **accounting rule**, and ADR 0019 keeps
accounting rules out of infrastructure; `FakeUnidadDeTrabajo` would have to re-implement the rule for
Core tests to see it, which is the definition of a rule living in the wrong layer. Note this is the
mirror image of D3: destinations are *not* a domain rule, so that map correctly went to
Infrastructure.
**Rejected (c)**: a generic `TransicionarEstadoFacturaAsync(id, esperado, nuevo)`. That *is* the
second, weaker write path the proposal warned about — it can express every transition, including ones
no rule allows.

**Does this create a second, weaker write path to `fact.Factura`? — Yes, literally; no, in the
dimension that matters.** Stated plainly, since the proposal asked design to confirm it:

- It is the **third** column-scoped write to that table (`ConfirmarAfectacionAsync` was the second,
  D10 of item #12), and the **first one without a rowversion CAS**. That much is real.
- It is not unguarded: it trades the version predicate for a **state predicate**. Concurrency safety
  is preserved — two racing validaciones cannot both apply, the loser reads `@@ROWCOUNT = 0`. What is
  lost is *client-supplied* optimistic concurrency, which `ValidarInternoAsync` never had for the
  factura row: the endpoint's `If-Match` is the **asiento's** ETag.
- Its blast radius is closed by construction: one column, one destination value (a SQL literal), one
  legal source state. It cannot resurrect a `DESCARTADA` factura, cannot write `PENDIENTE_VALIDACION`
  back, and cannot be repurposed by a later caller wanting a different transition.
- The command as a whole is still ETag-protected end to end: the member only runs after
  `GuardarAsientoAsync` returned `Aplicado`, i.e. after the asiento's version CAS already succeeded.
  The factura write is a *consequence* of a CAS-protected transition, not an independent mutation.
- The dedicated return enum is deliberate: `ResultadoEscritura` would have handed this member a
  `VersionEnConflicto` case it can never return. Making the absence of version CAS visible in the
  type is cheaper than a comment nobody reads.

**Where it is invoked** — inside `ValidarInternoAsync` (`ServicioDeFacturas.cs:60-113`), in the same
`IUnidadDeTrabajo` and therefore the same commit as the asiento confirmation, at exactly one point:

    GuardarAsientoAsync -> Aplicado          (:102-108, unchanged)
    MarcarFacturaValidadaAsync               <-- NEW, here
    PayloadOutbox.ConstruirAsync             (D2 re-read)
    EmitirOutboxAsync(FACTURA_VALIDADA)      (:110)
    CommitAsync                              (:111)

Both neighbours are load-bearing. **After** `GuardarAsientoAsync` because `VALIDADA` is the
consequence of a confirmation that actually applied — on a 412/404 the factura row is never touched.
**Before** `PayloadOutbox.ConstruirAsync` because D2 builds the payload by re-reading through the
port ("the transaction sees its own writes"); emitting first would ship a `FACTURA_VALIDADA` envelope
carrying `"estado": "PENDIENTE_VALIDACION"` — an event contradicting itself. Ordering the state write
first is what makes the envelope honest. It adds one `Llamadas` entry to **two** of D9's three
exact-sequence assertions (`ValidarAsync_...CommitsInOrder`, `ValidarPorFacturaAsync_...`); the
`ConfirmarAfectacionAsync` one is not a validar path and is unaffected by D10.

Against the guards: every `Estado == VALIDADA` reader (`:292` `DescartarAsync`, `:327`
`RegistrarAdjuntoAsync`, `:366` `EliminarAdjuntoAsync`, plus the new `FACTURA_CORREGIDA` gates of D8)
lives in a **different command and therefore a different transaction**, so all of them observe the
committed state. No guard reads `Estado` inside the validar transaction itself, so there is no
ordering hazard within it.

**No audit row.** `ValidarInternoAsync` writes no `EntradaAuditoria` today (unlike `ReabrirAsync`/
`AnularAsync`); this member does not add one. Whether `validar` should be audited at all is an item
#11/#13 audit-scope question, and answering it here would silently change three closed tests.

`YaValidada` is **not** an error and does not roll back: after `ReabrirAsync` the factura keeps
`Estado = VALIDADA` (reopen only moves the *asiento* to BORRADOR), so the reconfirm path the owner
already blessed in D1 hits this branch every time. `NoTransicionable` is **terminal**: the owner
resolved Open Question 5, so this branch aborts `ValidarInternoAsync`, rolls back the asiento
confirmation (nothing commits — no `CONFIRMADO` asiento, no `FACTURA_VALIDADA` event) and the
endpoint answers **409**. Rationale and consequences in Open Question 5 (resolved) below.

## Data Flow

    .NET Core (ValidarInterno / Anular / Patch / ConfirmarAfectacion / Adjuntos)
        ├─ (solo ValidarInterno) uow.MarcarFacturaValidadaAsync(facturaId)  [D10, misma tx]
        │        └─ UPDATE fact.Factura SET Estado='VALIDADA' WHERE Estado='PENDIENTE_VALIDACION'
        └─ PayloadOutbox.ConstruirAsync(uow, tipo, facturaId, asientoId?)   [re-read, same tx]
              └─ uow.EmitirOutboxAsync(tipo, facturaId, json)               [<= 1 per (Tipo,FacturaId)]
                    └─ Infra: INSERT OutboxEvent (SeqOutbox)
                              INSERT OutboxEventIntegracion × destinos aplicables
                                     │
    Python (cada minuto)             ▼
    cli_outbox ─→ ReclamoDeLote.reclamar(destinos_registrados)   [READPAST + lease]
                        ├─→ progreso(facturaId, destino) ─→ guarda.evaluar()
                        │         └─ Obsoleto ─→ marcar OBSOLETO ─┐ (fin, sin handler)
                        └─ Vigente ─→ despacho.enviar(destino) ───┴─→ marcar COMPLETADO

Registro de destinos vacío en #14 → nada se reclama, las filas se acumulan `PENDIENTE` para #15/#16.

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `SmartNet.Facturacion.Core/PayloadOutbox.cs` | Create | `ConstruirAsync` (port reads) + pure `Serializar` + `EnvolturaOutbox` (D2) |
| `SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modify | `ASIENTO_CORREGIDO` at reconfirm (D1); `FACTURA_CORREGIDA` in `PatchAsync` **and** `ConfirmarAfectacionAsync` (D8); rebuild `FACTURA_VALIDADA` + both `DOCUMENTACION_ACTUALIZADA` payloads onto the envelope (D2); `MarcarFacturaValidadaAsync` call in `ValidarInternoAsync` (D10), with a `NoTransicionable` arm returning `ResultadoComando.Conflicto(CasoConflicto.FacturaDescartada, …)` before `CommitAsync`, plus its `DescribirCaso` arm (OQ5) |
| `SmartNet.Facturacion.Core/CasoConflicto.cs` | Modify | New case `FacturaDescartada` (OQ5). Reusing `AsientoYaConfirmado` is rejected: its detail text ("la factura ya fue validada") is the mirror rule, not this one, and the 409 table of ADR 0008 is one row per case |
| `SmartNet.Api/ProblemasDeNegocio.cs` | Modify | New arm in the exhaustive `DescribirCaso` switch → `Base + "factura-descartada"` / "Factura descartada" (OQ5); the existing `Conflicto(…)` path already yields 409 + `application/problem+json` |
| `SmartNet.Facturacion.Core/ServicioDeAsientos.cs` | Modify | `ASIENTO_ANULADO` in `AnularAsync`, passing `asientoId` explicitly (D2) |
| `SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs` | Modify | New member `MarcarFacturaValidadaAsync` + `TransicionEstadoFactura` enum (D10) — the only new port surface in #14 |
| `SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modify | Fan-out INSERT + applicability map (D3); per-tx emission guard (D8); state-CAS `UPDATE fact.Factura` (D10) |
| `SmartNet.Facturacion.Core.Tests/FakeUnidadDeTrabajo.cs` | Modify | Non-null `FacturaACargar` default; reads see own writes; emission guard (D9); `MarcarFacturaValidadaAsync` recorded in `Llamadas` **and** reflected in the next `CargarFacturaAsync` (D10 — otherwise the golden `FACTURA_VALIDADA` payload carries the pre-transition `estado`) |
| `ServicioDeFacturasTests.cs`, `ServicioDeFacturasPhase2Tests.cs` | Modify | 3 exact-sequence assertions extended (D9) |
| `tests/fixtures/outbox_event_payload.golden.json` | Create | Shared .NET/Python envelope fixture |
| `worker/src/smartnet_worker/reclamo.py` | Create | `ReclamoDeLote` Protocol + `EventoReclamado` dataclass + `ARRENDAMIENTO = timedelta(minutes=5)` (D4); no `pyodbc` |
| `worker/src/smartnet_worker/outbox_repo.py` | Create | Only module with `READPAST`; claim/progress/mark |
| `worker/src/smartnet_worker/guarda_obsolescencia.py` | Create | Pure verdict (D5) |
| `worker/src/smartnet_worker/despacho_outbox.py` | Create | Destination-agnostic dispatch + empty registry |
| `worker/src/smartnet_worker/cli_outbox.py` | Create | Single-run entry point (D7) |
| `worker/pyproject.toml` | Modify | `smartnet-outbox` console script |
| `worker/tests/integration/conftest.py` | Modify | Real `usr_api` LOGIN + `api_connection_string` |

## Interfaces / Contracts

Envelope — camelCase Spanish + `version`, matching the `inbox_event_payload` precedent. Identical
shape for all five `Tipo`s; the consumer treats `Payload` as an **opaque string** in #14.

```json
{ "version": 1, "evento": "FACTURA_CORREGIDA", "facturaId": 100,
  "factura": { "estado": "...", "proveedorCodigo": "...", "rucProveedor": "...",
               "tipoComprobante": "...", "numero": "...", "totalOrig": 118.00, "moneda": "PEN",
               "fechaEmision": "2026-08-10", "motivo": null, "afectacion": "GRAVADA",
               "afectacionMixta": false, "esProveedorGenerico": false,
               "posibleDuplicado": false, "tieneCamposNoExtraidos": false },
  "asiento": { "asientoContableId": 5, "numeroAsiento": "02-2026-08-000007", "estado": "CONFIRMADO",
               "fechaContable": "2026-08-10", "lineas": [ /* LineaId, bloque, tipo, debe, haber, cuentaCodigo */ ] },
  "documentos": [ { "origen": "INGESTA" | "ADJUNTO", "id": 9, "nombreArchivo": "f.pdf",
                    "rutaRelativa": "2026/08/f.pdf", "mimeType": "application/pdf" } ] }
```

`asiento` is `null` only when the factura has no asiento at all. `documentos` carries **both**
origins complete (ADR 0004: "la lista de rutas de documentos viaja completa y de ambos orígenes,
para que Python no lea tablas de .NET").

The only new .NET port surface is D10's `MarcarFacturaValidadaAsync` / `TransicionEstadoFactura`
(signature and SQL in D10 above); no other `IUnidadDeTrabajo` member changes shape.

```python
ARRENDAMIENTO: Final = timedelta(minutes=5)   # reclamo.py — D4, OQ 3

class ReclamoDeLote(Protocol):
    def reclamar(self, destinos: Sequence[str], limite: int, ahora: datetime) -> tuple[EventoReclamado, ...]: ...
    def progreso(self, factura_id: int, destino: str) -> int | None: ...
    def marcar(self, evento_id: int, destino: str, estado: str, ahora: datetime) -> None: ...
```

Claim SQL shape (`outbox_repo.py` only) — `OUTPUT` cannot reference joined columns, so it lands the
claimed keys in a table variable and re-selects with the join:

```sql
UPDATE TOP (?) oei SET ProximoIntentoEn = DATEADD(SECOND, ?, ?), ActualizadoEn = ?
OUTPUT inserted.OutboxEventId, inserted.Integracion INTO @reclamadas
FROM fact.OutboxEventIntegracion AS oei WITH (READPAST, UPDLOCK, ROWLOCK)
WHERE oei.Estado = 'PENDIENTE' AND oei.Integracion IN (...)
  AND (oei.ProximoIntentoEn IS NULL OR oei.ProximoIntentoEn <= ?);
```

Progress = `MAX(oe.Secuencia)` over the same `FacturaId` + `Integracion` with `oei.Estado='COMPLETADO'`.
Stale iff `progreso is not None and secuencia <= progreso` (ADR 0004: "no supera la registrada").

## Testing Strategy

| Layer | What to Test | Approach |
|-------|--------------|----------|
| Unit (.NET) | 4 emission points fire/don't fire; **exactly one** `FACTURA_CORREGIDA` per tx; no-op PATCH and unchanged `AfectacionMixta` emit nothing | `FakeUnidadDeTrabajo.EventosOutbox`; no DB |
| Unit (.NET) | Emission guard throws on a repeated `(Tipo, FacturaId)` in one tx (D8) | Direct fake/`SqlUnidadDeTrabajo` assertion |
| Unit (.NET) | Envelope self-sufficiency for all **5** types, incl. the 2 retrofitted | `PayloadOutbox.Serializar` against golden fixtures |
| Unit (.NET) | `ASIENTO_ANULADO` payload still carries its asiento after annulment (D2) | Fake returns ANULADO for the explicit id; vigente lookup unused |
| Unit (.NET) | `ValidarInternoAsync` calls `MarcarFacturaValidadaAsync` **before** the payload build, and the `FACTURA_VALIDADA` envelope carries `"estado": "VALIDADA"` (D10) | `Llamadas` order + golden payload; no DB |
| Unit (.NET) | **Production-shaped guard test**: validar → then adjunto/PATCH on the *same* fake state, with `Estado` never set by hand — asserts `DOCUMENTACION_ACTUALIZADA` and `FACTURA_CORREGIDA` now fire (the closed gap) | Two sequential commands over one fake store |
| Unit (.NET) | `YaValidada` (reconfirm after reopen) does not roll back and still emits `ASIENTO_CORREGIDO` (D1+D10) | Fake returns `YaValidada` |
| Unit (.NET) | `NoTransicionable` (validar on a `DESCARTADA` factura) → 409, transaction rolled back, **no** `FACTURA_VALIDADA` emitted and the asiento stays BORRADOR (OQ5) | Fake returns `NoTransicionable`; assert no `CommitAsync` + empty `EventosOutbox` |
| Integración (.NET) | State CAS is real: second `MarcarFacturaValidadaAsync` on an already-VALIDADA row returns `YaValidada` and writes nothing; `DESCARTADA` row returns `NoTransicionable` | `SqlUnidadDeTrabajoFacturaTests.cs` against real schema |
| Integración (.NET) | The write bumps `fact.Factura.Version`, so a PATCH holding the pre-validation ETag gets 412 afterwards (accepted consequence, not a regression) | Real-schema round trip |
| Unit (py) | Guard verdicts; dispatcher never imports `outbox_repo` | Pure asserts + import-graph assert (mirrors `test_no_dbo_structural.py`) |
| Unit (py) | `OBSOLETO` never calls handler, never touches `Intentos`/`UltimoError` | Fake `ReclamoDeLote` + spy handler |
| Contrato N2 | Lease is 5 min: a claimed row is invisible to a second claim at `ahora + 4 min` and reclaimable at `ahora + 6 min` (D4) | Injected `ahora`, real row; assertion references `ARRENDAMIENTO`, never a literal `300` |
| Contrato N2 | Wire format agreement | Shared `tests/fixtures/outbox_event_payload.golden.json` read by BOTH the .NET producer test and the Python test — same mechanism as `test_payload_inbox_contract.py` |
| Contrato N2 | Bidirectional over real schema | `worker_db` fixture: `usr_api` INSERTs event + child rows → `usr_worker` claims/marks → `usr_api` reads back |
| Contrato N2 | Permission matrix, Python side | `usr_worker` INSERT on `OutboxEventIntegracion` denied; `usr_api` UPDATE denied; `usr_worker` write to `fact.Factura` denied. (.NET side already covered by `PermissionMatrixTests.cs:254-309` — do not duplicate) |
| Contrato N2 | `READPAST` actually skips | Two concurrent `pyodbc` connections claim simultaneously; assert disjoint sets and no blocking |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary is introduced. Dispatch is in-process function dispatch; scheduling is
deployment configuration, not code; the `subprocess` call in `conftest.py` is pre-existing test
harness, unchanged by this design.

## Migration / Rollout

No data migration, no schema change. Fan-out rows (D3) exist only for events emitted *after* deploy;
pre-existing `OutboxEvent` rows have no child row and are never claimed — acceptable because no
destination has ever consumed them. The payload retrofit has no consumer to break (item #14 builds
the first one). Consumer is inert until #15/#16 register a destination.

**D10 is forward-only and is not backfilled.** Facturas validated before this deploy have a
`CONFIRMADO` asiento but keep `Estado = 'PENDIENTE_VALIDACION'` forever; they will never emit
`DOCUMENTACION_ACTUALIZADA`/`FACTURA_CORREGIDA` and remain discardable via `POST /descartar`. #14
does not rewrite historical rows — a repair pass over existing data is a decision with its own
audit and ops shape (see Open Question 7), not a side effect of an outbox item.

**Two behaviour changes ship on existing endpoints**, both user-visible and both belonging in the
item's release note, not to be discovered by an assistant:

1. `POST /descartar` — `DescartarAsync`'s `Estado == VALIDADA` guard (`:292`) is the third dead gate
   and goes live too. Discarding a factura validated post-deploy returns 409 instead of applying.
2. `POST /validar` — per the owner's resolution of Open Question 5, validating a `DESCARTADA` factura
   now returns 409 and rolls back the asiento confirmation, instead of silently producing a
   `CONFIRMADO` asiento on a discarded factura.

Both are the documented intent of ADR 0008 ("VALIDADA no puede descartarse", read with its mirror
"DESCARTADA no puede validarse") finally taking effect, not new rules. Both are forward-only: they
apply to facturas whose `Estado` this change actually writes.

## Open Questions

Questions 1–5 are **all resolved** and folded into the decisions above: **1** → D2/D9 (payload
retrofit of the two pre-existing events happens inside #14); **2** → D8 (`ConfirmarAfectacionAsync`
is the fourth emission point); **3** → D4 (lease = 5 minutes); **4** → D10 (`ValidarInternoAsync`
writes `Estado = VALIDADA` through a single-transition port member); **5** → D10's
`NoTransicionable` branch (409 + rollback, detailed below). **Nothing in this design waits on a
product answer.**

Three further questions were **surfaced by D10** — they exist because making a dead column live
exposes transitions nobody had to think about while it was dead. Question **5**, the only one that
narrowly blocked apply, has since been **resolved by the project owner** and is folded into D10 above.
Questions 6 and 7 block nothing in #14 and ship as described.

- [x] **5. RESOLVED (owner)** — **`validar` on a `DESCARTADA` factura REJECTS: 409 + rollback of the
      asiento confirmation.** `NoTransicionable` is a terminal verdict; `DESCARTADA` is a terminal
      state. `ValidarInternoAsync` aborts before `PayloadOutbox.ConstruirAsync`, so the transaction
      commits nothing: the asiento stays BORRADOR and no `FACTURA_VALIDADA` is emitted.
      **Rejected alternative**: proceed and leave the state as-is — it preserves current behaviour but
      keeps a `CONFIRMADO` asiento hanging off a discarded factura, i.e. it preserves the
      contradiction instead of the invariant.
      **Rationale**: this mirrors `DescartarAsync`'s own `Estado == VALIDADA` guard (`:292`) and
      completes ADR 0008's documented intent ("VALIDADA no puede descartarse" read with "DESCARTADA
      no puede validarse"). Correctness of the libro de compras outranks preserving a reachable bug.
      **Context** (why it was a question at all): `abrir` → `descartar` → `validar` is reachable in
      production today — `DescartarAsync` leaves the BORRADOR asiento untouched and
      `ValidarPorFacturaAsync` never reads factura state — and D10's `NoTransicionable` is the first
      time that contradiction becomes *observable*.
      **Cost / shape**: one `switch` arm on `TransicionEstadoFactura` in `ValidarInternoAsync`
      returning `ResultadoComando.Conflicto(CasoConflicto.FacturaDescartada, …)` and returning
      *before* `CommitAsync` — the `await using var uow` already rolls back on non-commit, so no new
      rollback machinery; one new `CasoConflicto` case with its Core `DescribirCaso` arm and its
      `SmartNet.Api/ProblemasDeNegocio.DescribirCaso` arm (exhaustive switch); one unit test (Testing
      Strategy, `NoTransicionable` row) plus the existing integration row asserting a `DESCARTADA`
      row returns `NoTransicionable`. The 409 status itself is free: `ProblemasDeNegocio.Conflicto`
      already maps every `CasoConflicto` to 409 `application/problem+json`.
      **Visible behaviour change on an existing endpoint** (`POST /validar`) → release note, see
      Migration / Rollout.
- [ ] **6.** *(blocks nothing in #14)* **Should `ReabrirAsync`/`AnularAsync` move `Estado` back?**
      Neither touches `fact.Factura.Estado`, so after this change a reopened or annulled factura
      stays `VALIDADA`. D1's reconfirm path *depends* on that being acceptable (D10's `YaValidada`
      branch), and #14 ships the asymmetry. But "the asiento is ANULADO and the factura is still
      VALIDADA" is a statement about the libro de compras, and only the owner can say whether it is
      true. If the answer is "no", the reverse transition is a separate item — it needs its own audit
      entry and its own outbox consequences, neither of which is in #14's scope.
- [ ] **7.** *(blocks nothing in #14, ops decision)* **Backfill of historical validated facturas?**
      See Migration/Rollout: pre-deploy validated facturas keep `PENDIENTE_VALIDACION` permanently.
      Leaving them is safe for the outbox (no destination ever consumed it) but leaves the bandeja
      and `descartar` behaving differently for old and new rows indefinitely.
