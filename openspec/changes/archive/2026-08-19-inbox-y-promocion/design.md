# Design: Inbox y promoción (item #7)

## Technical Approach

Two independent 1-minute loops over the contract table `fact.InboxEvent`, plus a read projection for
the SPA. Python (`fact_worker`, INSERT/SELECT only) publishes one event per finished `Procesamiento`
in its own transaction, after item #6's already-committed pipeline. .NET (`fact_api`, SELECT/UPDATE
only) consumes it: `SmartNet.Inbox.Core` decides purely (ADR 0019), `SmartNet.Inbox.Infrastructure`
persists `Factura` + `FacturaExtraccion` + `InboxEvent` outcome in one transaction. No DDL: items
#1/#3 already ship every table, index and grant this needs.

## Architecture Decisions

| # | Choice | Rejected alternative | Rationale |
|---|---|---|---|
| D1 | "Sufficient data" = the four columns `fact.Factura` declares `NOT NULL` with no default — `TipoComprobante`, `TotalOrig`, `Moneda`, `FechaEmision` — plus `Procesamiento.Estado='COMPLETADO'` | REGLAS.md §1–4 weighting; "all 8 extracted fields present" | Structural per proposal Q3; `Numero`/`RucProveedor` nullability is normative (005 comments), so their absence must not block promotion |
| D2 | Idempotency = INSERT `Factura`, catch SQL 2601/2627 from `UQ_Factura_Procesamiento`, then resolve the existing `FacturaId` and mark `PROMOVIDO` | `SELECT` before `INSERT` | Engine invariant, not discipline (ADR 0005); same anti-TOCTOU rule items #4–#6 adopted in `upsert_procesamiento` |
| D3 | Producer writes one atomic `INSERT … SELECT … WHERE NOT EXISTS (SELECT 1 FROM fact.InboxEvent …)` | New `UQ_InboxEvent_Procesamiento` migration | Keeps the no-migration scope; a racing duplicate event is cosmetic because D2 still caps facturas at one |
| D4 | `evidencia[].fuente` = the document's `DocumentoRecibido.TipoDocumento`, uniform per event; **no** `confianza` key | Per-field source/confidence | #6 persists neither; emitting them would invent data (ADR 0017). **Narrows proposal Q2 — flagged, not silent** |
| D5 | Promotion computes **5** indicators; `EsReferenciaExterna` keeps its DDL default `0` | Computing all 6 | `DatosExtraidos` has no reference columns, so it is unobtainable here; notas de crédito are item #10. **Corrects ADR 0005's "six indicators at promotion"** |
| D6 | Reuse ADR 0008's `GET /api/bandeja?estado=&orden=` with a #7-shaped projection that #13 widens | New `GET /api/inbox` | A second inbox surface would fork a normative contract |
| D7 | .NET `BackgroundService` + `PeriodicTimer(1 min)` with injected `TimeProvider`; Python `cli_inbox.py` is single-run per invocation, scheduled externally each minute | In-process Python scheduler | Every existing worker CLI is explicitly "un solo ciclo por invocación, sin polling en proceso" |
| D8 | `cli_inbox.py` writes no `fact.EstadoIntegracion` row | Reuse `Nombre='WORKER'` | `CK_EstadoIntegracion_Nombre` has no `INBOX` value and reusing `WORKER` would mask #6's heartbeat; un-notified rows self-heal next run |
| D9 | JSON parsing/serialising lives in Infrastructure; Core sees records only | Core parses payload | Keeps `PurityScanTests` meaningful |

## Data Flow

    cli_procesamiento (#6, closed)         cli_inbox.py (new, 1 min)
      Procesamiento+DatosExtraidos  ──→  SELECT un-notified ──→ INSERT fact.InboxEvent
                                                                  (PENDIENTE)
                                                                      │
    PromocionBackgroundService (1 min) ←──── SELECT EstadoConsumo='PENDIENTE'
      │  parse Payload → EventoInbox (Infrastructure)
      │  PoliticaDePromocion.Decidir + CalculoDeIndicadores.Calcular (Core, pure)
      └─ one SqlTransaction:
           sufficient  → INSERT Factura(PENDIENTE_VALIDACION) + FacturaExtraccion*
                         → UPDATE InboxEvent(PROMOVIDO, FacturaId, ConsumidoEn)
           insufficient→ UPDATE InboxEvent(DESCARTADO, MotivoDescarte)  [zero Factura rows]
                                                                      │
    Angular Inbox (signals) ──→ GET /api/bandeja?estado=&orden= ──────┘

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `SmartNet/worker/src/smartnet_worker/inbox_event_repo.py` | Create | `listar_no_notificados(cursor)`, `insertar_evento(cursor, procesamiento_id, payload)`; cursor-in, no decisions |
| `SmartNet/worker/src/smartnet_worker/payload_inbox.py` | Create | Pure payload builder (no DB), unit-testable |
| `SmartNet/worker/src/smartnet_worker/cli_inbox.py` | Create | One cycle: read un-notified → build payload → INSERT → commit, per-row isolation |
| `SmartNet/worker/pyproject.toml` | Modify | New console script `smartnet-inbox` |
| `SmartNet/inbox/SmartNet.Inbox.Core/` | Create | `EventoInbox`, `ComprobanteExtraido`, `EvidenciaCampo`, `DecisionPromocion`, `IndicadoresFactura`, `FacturaPromovida`, `PoliticaDePromocion`, `CalculoDeIndicadores`, `ConstruccionDeFactura`, ports `IEventoInboxRepository`/`IPromocionRepository`/`IBandejaRepository`. Zero `PackageReference` |
| `SmartNet/inbox/SmartNet.Inbox.Infrastructure/` | Create | `PayloadInboxParser`, `SqlEventoInboxRepository`, `SqlPromocionRepository` (one `SqlTransaction`), `SqlBandejaRepository`, `PromocionBackgroundService` |
| `SmartNet/inbox/SmartNet.Inbox.{Core,Infrastructure}.Tests/` | Create | `PurityScanTests` (copy), policy/indicator tests, `PermissionSufficiencyTests`, `NoWriteToDboStructuralTests` |
| `SmartNet/api/SmartNet.Api/BandejaEndpoints.cs` | Create | Thin `GET /api/bandeja`, `.RequireAuthorization()` |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modify | Register the three repos + `AddHostedService<PromocionBackgroundService>()` |
| `SmartNet/SmartNet.sln`, `.github/workflows/ci.yml` | Modify | Core → `verificaciones-estaticas`; Infrastructure → `pruebas-de-base-de-datos` |
| `SmartNet/spa/` | Create | Angular workspace + Inbox route (see Open Questions) |
| `adrs/0005-frontera-de-promocion-*.md` | Modify | Single `Tipo` value; five computable indicators (D5) |

## Interfaces / Contracts

`InboxEvent.Payload` (versioned; `campo` ∈ the 8 `CK_FacturaExtraccion_CampoNombre` values):

```json
{ "version": 1, "estadoProcesamiento": "COMPLETADO",
  "documento": { "documentoRecibidoId": 8, "tipoDocumento": "XML", "documentoAsociadoId": 9 },
  "comprobante": { "tipoComprobante": "01", "numero": "F001-123", "rucProveedor": "20100000001",
                   "nombreProveedor": "…", "monto": "1180.00", "moneda": "PEN",
                   "fechaEmision": "2026-08-10" },
  "evidencia": [ { "campo": "total", "valor": "1180.00", "fuente": "XML" } ],
  "afectacionMixta": false, "camposNoExtraidos": ["igv"], "advertenciasAsociacion": ["SIN_PAREJA"] }
```

```csharp
static DecisionPromocion PoliticaDePromocion.Decidir(EventoInbox evento);
static IndicadoresFactura CalculoDeIndicadores.Calcular(
    EventoInbox evento, bool proveedorResuelto, bool existeIdentidadPrevia);
static FacturaPromovida ConstruccionDeFactura.Construir(
    EventoInbox e, string proveedorCodigo, IndicadoresFactura i);
```

`FechaEnDomingo` derives from `FechaEmision`, never from a clock. `proveedorResuelto`
(`dbo.Proveedor` lookup, SELECT-only) and `existeIdentidadPrevia` (`IX_Factura_Identidad`) are
resolved in Infrastructure and passed in as facts.

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit (.NET) | Sufficiency, 5 indicators, 3-state `AfectacionMixta`, purity | `SmartNet.Inbox.Core.Tests` + `PurityScanTests`, no DB |
| Unit (Python) | Payload builder, SQL text shape | pytest, no marker |
| Integration | Double promotion → 1 `Factura`; insufficient → 0 `Factura` rows + `DESCARTADO`; `usr_api` denied on `Procesamiento`; `usr_worker` denied on `Factura` | `TestDatabaseFixture` / `-m integracion` under real logins |
| Contract (ADR 0019 L2) | Python-written payload parses into `EventoInbox` | Shared golden JSON asserted by both suites |
| E2E | `GET /api/bandeja` returns promoted + discarded, filtered and sorted; 401 without cookie | `WebApplicationFactory` |

## Threat Matrix

N/A — no routing-by-path, shell, subprocess, VCS/PR automation, or executable-file classification
boundary. The new HTTP route is a cookie-authorised in-process minimal-API endpoint; the background
service is hosted, not spawned.

## Migration / Rollout

No migration. Rollback = remove the `AddHostedService` line; unconsumed events stay `PENDIENTE` and
replay safely.

## Open Questions

- [x] **No Angular workspace exists** (no `package.json`, no SPA folder, no CI frontend job) and
      `BACKLOG.md` places SPA screens at #12/#13. **Decided:** bootstrap it now, as its own chained
      PR slice within this item's stacked-to-main chain (WU5). Do not defer to #12/#13.
- [x] D5 contradicts the proposal's "6 indicators" and ADR 0005 — **confirmed:** design.md's 5
      computable indicators stand. `EsReferenciaExterna` keeps its DDL default; proposal/ADR 0005
      text is the one that needs correcting, not the code.
- [x] D4 drops `confianza` from proposal Q2's payload shape — **confirmed:** design.md stands.
      `FacturaExtraccion` (ADR 0017, item #6, already closed) has no confidence field and no
      component computes one; emitting it would fabricate data (ADR 0017 boundary). Proposal Q2 is
      the one that needs correcting.
