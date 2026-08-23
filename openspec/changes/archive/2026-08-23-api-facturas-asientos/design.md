# Design: API de facturas y asientos (BACKLOG #11)

## Technical Approach

New area `SmartNet/facturacion/`: **`SmartNet.Facturacion.Core`** (application services + ports,
pure, PurityScan-guarded) and **`SmartNet.Facturacion.Infrastructure`** (SQL adapters).
`SmartNet.Api` keeps only thin `*Endpoints.cs` plus HTTP translation. Precedents followed:
`ServicioDeSugerencia` (#9, services over ports only) and `IPromocionRepository.PromoverAsync`
(a whole `SqlTransaction` behind one port).

## Architecture Decisions

### D1 — Orchestration in Core, driven through a transaction-scoped session port

| | |
|---|---|
| **Choice** | `ServicioDeFacturas`/`ServicioDeAsientos`/`ServicioDeIntegraciones` in Core. Each command opens `IUnidadDeTrabajo` (from `IFacturacionStore`), an `IAsyncDisposable` that owns one `SqlTransaction` (rollback unless `CommitAsync`). Load aggregate → call `ComposicionDeAsiento`/`InvariantesDeConfirmacion` → write → commit, all inside it. |
| **Rejected** | One coarse `ConfirmarAsync(...)` port — ADR 0006 requires invariants evaluated *inside* the transaction, so a coarse port would re-encode them in SQL. Orchestration in `SmartNet.Api` — untestable without HTTP. `TransactionScope` — ambient, DTC risk. |
| **Rationale** | Core references interfaces only, so ADR 0019 stays structurally enforced. |

### D2 — Concurrency: shared token codec + per-adapter compare-and-swap (RESOLVED)

The proposal's open question is closed as a **split**, not all-or-nothing:

| Layer | Decision |
|---|---|
| Token | **Shared**: `TokenDeConcurrencia.Codificar(byte[8]) / TryDecodificar(string)` — pure static codec in Core, ETag = `"` + Base64(rowversion) + `"`. **No `IConcurrencyToken` interface** (a value has no polymorphism to hide). |
| Header | **Shared**: `IfMatch.Requerido(HttpContext)` in `SmartNet.Api` — missing/`*`/unparsable ⇒ `428 Precondition Required` (ADR 0008 addendum to record). |
| CAS | **Duplicated deliberately**: each adapter writes its own `... WHERE Id=@id AND Version=@expected`. Hiding it behind a helper would hide *which columns* each command may touch. |

Volume justifies the codec: ≥6 mutating surfaces (2 `PATCH`, 3 line routes, `validar`/`reabrir`/`anular`) plus every `GET` emitting `ETag` for #12/#13.
`@@ROWCOUNT = 0` ⇒ re-`SELECT` the row: exists ⇒ `412`, absent ⇒ `404`, wrong `Estado` ⇒ `409`.

### D3 — Invariant → HTTP mapper lives only in `SmartNet.Api`

`ProblemasDeNegocio.Map(InvarianteIncumplida)`. ADR 0008's `409` table and ADR 0006's invariants
**overlap on two values**; ADR 0008 wins because it is the HTTP contract:

| `InvarianteContable` | Status | `type` suffix |
|---|---|---|
| `SumaDebeIgualHaber` | 422 | `asiento-descuadrado` |
| `LineaSinCuenta` | 422 | `linea-sin-cuenta` |
| `Principal` | 422 | `bloque-principal-invalido` |
| `Destino` | 422 | `bloque-destino-incompleto` |
| `TipoLineaInconsistente` | 422 | `linea-inconsistente` |
| `FechaAnteriorAlCorte` | **409** | `fecha-anterior-al-corte` |
| `ProveedorVarios` | **409** | `proveedor-generico-sin-resolver` |

Payload: exactly one failure ⇒ ADR 0008's flat shape (`type/title/status/detail` + `importeEsperado`/`importeReal` when non-null). Two or more ⇒ `type=.../asiento-invalido` with an `errors[]` array of the same objects. `InvariantesDeConfirmacion` returns all failures; none are dropped.

### D4 — `409` gate before the engine

`CasoConflicto` enum in Core, one value per ADR 0008 row (duplicado, domingo, sin tipo de cambio,
P00000, fecha de corte, NC con referencia interna irresoluble, asiento ya confirmado, afectación
mixta, afectación no verificada). Evaluated in the unit of work from `fact.Factura` +
`IX_Factura_Identidad` + `ITipoCambioRepository.ObtenerVigenteAsync` before composing. NC accumulated
cap (ADR 0006 SQL) runs inside the transaction and maps to `422`.

### D5 — One transaction for `validar` (= ADR 0006 "confirmar")

`SET XACT_ABORT ON`, then: CAS on `AsientoContable` (`Estado='BORRADOR'`, `Version=@expected`) → `409` gate → freeze catalog columns → invariants (pure) → NC cap → `UPDATE fact.CorrelativoAsiento WITH (UPDLOCK) ... OUTPUT inserted.Ultimo` (0 rows ⇒ `INSERT` seeding, catching 2601/2627 and retrying — the `SqlPromocionRepository` anti-TOCTOU idiom) → `NumeroAsiento = '{Origen}-{Anio}-{Mes:00}-{Ultimo:000000}'` → `fact.Factura.Estado='VALIDADA'` → suggestion counter → `AuditoriaCorreccion` (when applicable) → `OutboxEvent` (`Secuencia = NEXT VALUE FOR fact.SeqOutbox`) + `OutboxEventIntegracion` → `COMMIT`. **No compensation path exists or is needed**: every step is in the one transaction, and a rollback returns the correlativo (the reason ADR 0006 refused `SEQUENCE`). The suggestion counter is issued inline here, not through `ISugerenciaCuentaRepository.RegistrarUsoAsync`, which opens its own connection.

### D6 — `AuditoriaCorreccion` writes

Only the seven `Accion` values (ratified). `abrir`/`sincronizar`/`reconectar`/`reprocesar` write nothing.

| Command | Accion | EntidadTipo | Campo | ValorOriginal → ValorNuevo | Motivo |
|---|---|---|---|---|---|
| `PATCH` factura/asiento | `CORRECCION` | FACTURA/ASIENTO | field name | scalar, invariant culture, **one row per changed field** | null |
| `reabrir` | `REAPERTURA` | ASIENTO | `Estado` | `CONFIRMADO`→`BORRADOR` | required |
| `anular` | `ANULACION` | ASIENTO | `Estado` | `CONFIRMADO`→`ANULADO` | required |
| reopened asiento changing month | `TRASLADO_PERIODO` | ASIENTO | `NumeroAsiento` | old → new | required |
| `validar` with unverified afectación | `CONFIRMACION_AFECTACION` | FACTURA | `Afectacion` | `NULL`→value | null |
| `DELETE` adjunto | `ELIMINACION_ADJUNTO` | ADJUNTO | `EliminadoEn` | `NULL`→timestamp | required |
| manual split override | `REPARTO_MANUAL` | ASIENTO | `Cargos` | JSON array → JSON array | null |

### D7 — `sincronizar`/`reconectar`/`reprocesar` enqueue only

`ICommandQueueRepository.EncolarAsync(tipo, referencia, payload, correlationId, ct)` → `INSERT fact.CommandQueue`, `202 Accepted` + `{ correlationId }`. `CorrelationId` is generated by the API before the write (schema comment). `.NET never calls Python` (ADR 0003). `{nombre}` is whitelisted → `SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS`; anything else `404`. `reconectar` needs a **new** `Tipo` value `RECONECTAR_GOOGLE` via versioned SQL `015_*.sql` (no EF migration, ADR 0016); overloading `SINCRONIZAR_GMAIL` with a payload flag was rejected as intent-hiding. `GET /api/integraciones/estado` reads `fact.EstadoIntegracion` read-only.

### D8 — Endpoints and DI

Four static classes (`FacturaEndpoints`, `AsientoEndpoints`, `TipoCambioEndpoints`,
`IntegracionEndpoints`), `BandejaEndpoints` shape: map, `RequireAuthorization()`, delegate, translate.
`Program.cs` registers the store/repositories with the existing **lazy `IConfiguration`** factory
delegates; services are `AddScoped`.

## Data Flow

    Endpoint ──If-Match──▶ Servicio (Core) ──▶ IUnidadDeTrabajo ──▶ SqlTransaction
        │                      │                                        │
        │                      ├─▶ ComposicionDeAsiento (pure)          ├─ CAS Version
        │                      └─▶ InvariantesDeConfirmacion (pure)     ├─ UPDLOCK correlativo
        │                                   │                           ├─ AuditoriaCorreccion
        ▼                                   ▼                           └─ OutboxEvent
    ProblemasDeNegocio ◀── ResultadoComando (Ok | Conflicto | VersionEnConflicto | Invariantes)

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/facturacion/SmartNet.Facturacion.Core/` | Create | `ServicioDeFacturas`, `ServicioDeAsientos`, `ServicioDeIntegraciones`, `IFacturacionStore`, `IUnidadDeTrabajo`, `ICommandQueueRepository`, `TokenDeConcurrencia`, `ResultadoComando`, `CasoConflicto` |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/` | Create | `SqlFacturacionStore`, `SqlUnidadDeTrabajo`, `SqlCommandQueueRepository`, `SqlEstadoIntegracionRepository` |
| `SmartNet/api/SmartNet.Api/{Factura,Asiento,TipoCambio,Integracion}Endpoints.cs` | Create | 15 routes, thin |
| `SmartNet/api/SmartNet.Api/ProblemasDeNegocio.cs`, `IfMatch.cs` | Create | RFC 9457 mapper; header parse |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modify | DI + 4 `Map*Endpoints()` |
| `SmartNet/db/schema/015_commandqueue_reconectar_google.sql` | Create | `ALTER` `CK_CommandQueue_Tipo` + grants |
| `SmartNet.sln`, `.github/workflows/ci.yml` | Modify | New projects |

## Interfaces / Contracts

```csharp
public interface IUnidadDeTrabajo : IAsyncDisposable   // owns the SqlTransaction (Infrastructure)
{
    Task<AsientoPersistido?> CargarAsientoAsync(long asientoId, CancellationToken ct);
    Task<ResultadoEscritura> GuardarAsientoAsync(long id, byte[] versionEsperada, /*...*/ CancellationToken ct);
    Task<int> AsignarCorrelativoAsync(short anio, byte mes, string origen, CancellationToken ct);
    Task RegistrarAuditoriaAsync(EntradaAuditoria entrada, CancellationToken ct);
    Task EmitirOutboxAsync(string tipo, long facturaId, string payload, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}

public enum ResultadoEscritura { Aplicado, VersionEnConflicto, EstadoInvalido, NoEncontrado }
```

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit (Core) | Command sequencing, `TokenDeConcurrencia` round-trip, `CasoConflicto` selection | Fake `IUnidadDeTrabajo`; no DB |
| Unit (Api) | Exhaustive `InvarianteContable` → status/`type` map; missing header ⇒ 428 | Enum-coverage test fails when a value is added |
| Integration | 409 / 412 / 422 / 428, `ETag` round-trip, correlativo gapless under rollback, `CommandQueue` row shape | `SmartNet.Api.Tests` over the real DB (`SmartNetApiFactory` + `SesionEndpointsTestBase`), never touching the pure core |
| Structural | `PurityScanTests` copy for `SmartNet.Facturacion.Core`; no `dbo.*` write; no Python invocation | NetArchTest / Mono.Cecil, existing pattern |

Concurrency test: two clients hold the same `ETag`; the second `PATCH` must return `412` and leave the row untouched.

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. Python integration is a database row (`fact.CommandQueue`), never a
process invocation (ADR 0003/0004).

## Migration / Rollout

Additive. One versioned SQL file (`015`) widening a `CHECK` constraint; no data migration, no EF
migration. Rollback = revert the PR slice; endpoints are new routes.

## Open Questions

- [ ] `428 Precondition Required` for a missing `If-Match` is outside ADR 0008's four-code table —
      record as an ADR 0008 addendum during apply rather than silently.
