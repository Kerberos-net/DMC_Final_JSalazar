# Proposal: API de facturas y asientos (BACKLOG #11)

## Intent

Facturas and asientos currently have no HTTP command surface — `SmartNet.Api` only exposes login
and the read-only `GET /api/bandeja`. Nothing lets a user edit a draft, confirm a validation,
reabrir/anular an asiento, or resolve a duplicate, and there is no concurrency protection when two
tabs touch the same aggregate. This change builds that surface per ADR 0008's full contract, so
#12 (Detalle y validación) and #13 (Bandeja e incidencias) have an API to consume. #7 and #8 are
closed and merged; the schema (`Version` rowversion, `CorrelativoAsiento`, `AuditoriaCorreccion`)
was built ahead of time for exactly this item.

## Scope

### In Scope — ADR 0008 full command contract

**Concurrency-controlled REST edits** (`If-Match` required, `412` on mismatch):
- `PATCH /api/facturas/{id}`
- `PATCH /api/asientos/{id}`

**Facturas — commands:**
- `POST /api/facturas/{id}/abrir` — creates the `BORRADOR` asiento if absent (ADR 0006)
- `POST /api/facturas/{id}/validar`
- `POST /api/facturas/{id}/descartar`
- `POST /api/facturas/{id}/adjuntos`, `DELETE /api/facturas/{id}/adjuntos/{adjuntoId}` (emit
  `DOCUMENTACION_ACTUALIZADA` when factura is already validada)

**Asientos — commands:**
- `POST /api/asientos/{id}/lineas`, `PATCH/DELETE /api/asientos/{id}/lineas/{lineaId}`
  (`LineaId`-addressed, never position)
- `POST /api/asientos/{id}/reabrir` (requires `motivo` in body)
- `POST /api/asientos/{id}/anular`

**Tipos de cambio, incidencias, integraciones:**
- `POST /api/tipos-cambio` (HTTP wrapper around the existing `carga-manual` repository from #4)
- `POST /api/incidencias/{id}/reprocesar`
- `POST /api/integraciones/{nombre}/sincronizar`, `POST /api/integraciones/google/reconectar`
- `GET /api/integraciones/estado`

**Cross-cutting:** RFC 9457 `application/problem+json` mapping of `InvarianteIncumplida` → `422`,
the `409` case table, transactional correlativo assignment (`UPDLOCK`) at confirm, and
`AuditoriaCorreccion` writes for correction/reopen/cancel/period-move actions.

### Out of Scope
- **#14 (Outbox y mensajería) as consumer** — #11 only *produces* `OutboxEvent` rows in the same
  transaction as the triggering command; processing/dispatch is #14's job.
- **#12/#13 UI screens** — they consume this API; building them is not part of #11.
- **Catalog write endpoints** — `/api/motivos`, `/api/cuentas`, `/api/proveedores` stay read-only
  (ADR 0003); no `POST`/`PATCH` is added for them here.
- **`POST /api/asientos/{id}/reactivar`** — retired in ADR 0008 rev.3, not rebuilt.
- Query-only endpoints already covered by earlier items (`GET /api/bandeja`, `/motivos`,
  `/cuentas`, `/proveedores`, `/tipos-cambio`, `/configuracion`, document content) — not repeated
  here unless a command endpoint needs a matching `GET` for `If-Match` (facturas/{id},
  asientos/{id}).

## Capabilities

### New Capabilities
- `api-facturas`: command/REST-edit orchestration for the Factura aggregate (`PATCH`, `abrir`,
  `validar`, `descartar`, adjuntos).
- `api-asientos`: command/REST-edit orchestration for the AsientoContable aggregate (`PATCH`,
  líneas, `reabrir`, `anular`), correlativo assignment, `AuditoriaCorreccion`.
- `api-incidencias-integraciones`: `reprocesar`, `sincronizar`, `reconectar`,
  `GET /api/integraciones/estado`.

### Modified Capabilities
- `tipos-de-cambio`: adds the `POST /api/tipos-cambio` HTTP endpoint over the existing
  `carga-manual` repository (item #4), including the `409`/`400` problem+json mapping the earlier
  spec explicitly deferred to #11.

## Approach

New orchestration layer between `SmartNet.Api` and `SmartNet.Contable.Core`/`SmartNet.Inbox.Core`,
following the `SmartNet.Sugerencia.Core` + orchestration-service precedent from #9 (commit
`322ee0e`): a thin `*Endpoints.cs` per resource (pattern from `BandejaEndpoints.cs`) delegates to
an orchestration service that (a) loads the aggregate, (b) calls the pure engine with
`If-Match`/rowversion compare-and-swap, (c) maps `InvarianteIncumplida` → `422` problem+json (in
`SmartNet.Api`, never in `SmartNet.Contable.Core`, per ADR 0019), (d) assigns the correlativo
transactionally via `UPDLOCK` on confirm, (e) writes `AuditoriaCorreccion`, (f) emits
`OutboxEvent`. **Open for `sdd-design`**: whether concurrency compare-and-swap is a shared
`IConcurrencyToken` helper or duplicated per-endpoint (exploration option 2) — not resolved here.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/api/SmartNet.Api/FacturaEndpoints.cs` | New | Factura REST edit + commands |
| `SmartNet/api/SmartNet.Api/AsientoEndpoints.cs` | New | Asiento REST edit + commands |
| `SmartNet/api/SmartNet.Api/*Orchestration*` | New | Aggregate load, invariant→problem+json mapping, correlativo, audit, outbox |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modified | DI wiring for new services/repositories |
| `SmartNet/api/SmartNet.Api.Tests/` | New | `409`/`412`/`422` and `If-Match` round-trip tests |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Scope exceeds 400-line PR budget (11+ endpoints) | High, expected | Chained PR slices by aggregate (facturas, then asientos, then tipos-cambio/incidencias/integraciones); flagged to `sdd-tasks` |
| Correlativo `UPDLOCK` + multi-table transaction has no prior adapter precedent in this codebase | Medium | Design phase specs the transaction boundary explicitly before coding |
| Invariant→HTTP mapping drifts from ADR 0008's `409`/`422` table over time | Low | Spec phase encodes the full case table as scenarios |
| "confirmar" (ADR 0006) vs. `validar` (ADR 0008) semantic equivalence | Resolved | Confirmed same trigger by product owner — see Proposal question round below |

## Rollback Plan

New endpoints are additive; disable via feature routing or revert the PR slice. No destructive
schema change — `Version`/`CorrelativoAsiento`/`AuditoriaCorreccion` already exist from #1/#5/#6.

## Dependencies
- #7, #8 — closed and merged, confirmed in code.

## Success Criteria
- [ ] All 15 ADR 0008 command/edit endpoints implemented, each mapped to correct `409`/`412`/`422`
- [ ] `PATCH` endpoints reject mismatched `If-Match` with `412`, accept matching with 0-row-safe CAS
- [ ] Confirm path assigns correlativo transactionally, never skips a number on rollback
- [ ] Correction/reopen/cancel/period-move actions all produce a matching `AuditoriaCorreccion` row
- [ ] `SmartNet.Contable.Core`/`SmartNet.Inbox.Core` purity tests still pass unmodified

## Proposal question round — resolved

Confirmed by the product owner; ratified, not assumptions anymore:

1. **`POST /api/facturas/{id}/validar` = ADR 0006's "confirmar".** Same trigger: correlativo
   assignment + freeze happen on `validar`.
2. **`AuditoriaCorreccion` writes are limited to the `Accion` enum's listed actions**
   (CORRECCION/REAPERTURA/ANULACION/TRASLADO_PERIODO/CONFIRMACION_AFECTACION/ELIMINACION_ADJUNTO/
   REPARTO_MANUAL). `abrir`, `sincronizar`, `reconectar`, `reprocesar` do not write audit rows.
3. **`sincronizar`/`reconectar`/`reprocesar` enqueue a `CommandQueue` row (ADR 0004)** — .NET never
   calls into Python directly, per the ADR 0003 partition boundary. Processing stays out of scope.
4. **Shared concurrency helper vs. per-endpoint duplication** — still deliberately left open for
   `sdd-design` (see Approach); not a business question, an implementation-design tradeoff.
