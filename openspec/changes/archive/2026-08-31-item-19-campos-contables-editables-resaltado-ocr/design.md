# Design: Campos contables editables y resaltado OCR por campo (BACKLOG #19)

## Technical Approach

Four additive slices over existing seams: one forward SQL script (`021`), the promotion indicator
record, the `PatchAsync` load → pure-guard → apply → write pipeline, and the `detalle` feature.
`SmartNet.Contable.Core` / `.Facturacion.Core` stay DB/HTTP/clock-free (ADR 0019): every new rule is
a pure function; every lookup is an `IUnidadDeTrabajo` port implemented in Infrastructure.

## Two corrections to the proposal's premises

1. **`fact.Factura` has no `BORRADOR` state.** `CK_Factura_Estado` allows only
   `PENDIENTE_VALIDACION | VALIDADA | DESCARTADA` (`005_negocio.sql:75`); `BORRADOR` belongs to
   `AsientoContable`. The immutability gate is `Estado == FacturaPersistida.PendienteValidacion`.
   No new state, no CHECK change.
2. **The tipo-de-cambio guard already exists.** `SqlUnidadDeTrabajo.cs:107` computes
   `HechosDeConflicto.SinTipoCambio` and `ServicioDeFacturas.EvaluarHechosDeConflicto` blocks
   `validar` (which *is* confirm); `AbrirAsync:305-313` blocks `abrir`; PEN is already exempt. #19's
   delta is a **narrowing**, not a new blocker: §6 exempts `07` con referencia interna (it inherits
   the frozen rate). Predicate becomes "requires a rate lookup" =
   `Moneda != 'PEN' AND NOT (TipoComprobante='07' AND EsReferenciaExterna=0 AND FacturaReferenciaId IS NOT NULL)`.

## Architecture Decisions

| # | Decision | Alternatives rejected | Rationale |
|---|---|---|---|
| D1 | `baseImponible` is **not** a column. The write contract carries the pair `{baseImponible, igv}` **atomically**; the ladder writes `TotalOrig = base + igv` and `IgvOrig = igv`. Sending the pair together with `totalOrig` → 422. | Store a `BaseImponible` column; accept `igv` alone | §6: base is DERIVED. `IgvOrig` is nullable, so `base` alone is undefined; the atomic pair removes the whole ambiguity class and keeps one source of truth for `TotalOrig`. |
| D2 | `ValidacionDeCorreccion.Validar(FacturaPersistida original, CorreccionFactura cambios)` — signature grows one argument, stays pure. Guards evaluate **merged** values. | Keep `cambios`-only + validate in `AplicarCorreccion` | Three new guards (state gate, `igv=0` for `03`/EXONERADA/INAFECTA, `base≥0 ∧ igv≤total`) all need `TipoComprobante`/`Afectacion`/`Estado`, which live on the loaded record. One signature change carries all three; still no DB. |
| D3 | Scalar §5/§6 derivation lives in a new pure `SmartNet.Contable.Core/ProyeccionDeImportes.Derivar(comprobante, afectacion, baseOrig, igvOrig, tcVenta)` delegating to the existing `ConversionDeMoneda.Convertir`, plus §5's IGV-to-cost collapse (`03`/no gravada → `IgvPEN = 0`, `BasePEN = TotalPEN`). | Inline the arithmetic in `ServicioDeFacturas`; call `ComposicionDeAsiento.Componer` | `Convertir` alone is §6 only and would post a phantom `401111` for a boleta (§5, §7 row 2). `Componer` is the out-of-scope full recompose. |
| D4 | `PatchAsync` writes the scalars onto the **vigente BORRADOR asiento** in the same transaction via a narrow `IUnidadDeTrabajo` port, only when `TotalOrig`/`IgvOrig`/`Moneda` actually changed. If the applicable rate is missing, skip the write and let the existing `SinTipoCambio` gate block `validar` — PATCH still succeeds. | Derive at read time in `GET .../asiento`; recompute at `validar` | §7 invariants read the *persisted* columns; read-time derivation leaves them stale. Recomputing only at `validar` keeps the screen wrong until the end. |
| D5 | `guardarAvance` refetches **everything** (`cargarTodo()`), not just the factura. | Refetch factura only | D4 bumps the asiento `Version`; a factura-only refetch leaves the asiento ETag stale and 412s the next línea edit. One refetch also delivers the recomputed `posibleDuplicado`. |
| D6 | `PosibleDuplicado` recomputed inside `PatchAsync` only when the identity triple changed, via `IUnidadDeTrabajo.ExisteIdentidadPreviaAsync` (promote the existing private `ExisteDuplicadoNoResueltoAsync`). **No** `AuditoriaCorreccion` row. | Recompute on every PATCH; audit it | Recomputing unconditionally could flip a flag the assistant just resolved. It is a derived indicator, not a user correction; `CK_AuditoriaCorreccion_Accion` has no fitting value. |
| D7 | Audit one row per changed **persisted column** — `TotalOrig`, `IgvOrig`, `Glosa`. No synthetic `BaseImponible` row. | A `BaseImponible` audit row (proposal default) | `Campo` names a column; a row no column backs is unreconcilable later. Base = `TotalOrig − IgvOrig`, both audited, so it is fully reconstructible. |
| D8 | `CamposNoExtraidos` is an **immutable extraction fact**: persisted at promotion, never mutated by PATCH; a non-empty list with UBL XML present is valid, not a bug. | Clear entries as the user fixes each field; derive server-side from `FacturaExtraccion` | The highlight means "this value did not come from the document — verify it", which stays true after a manual edit. Deriving would duplicate worker logic across the ADR 0003 partition. |

## Consequence to surface (scope boundary)

D4 **un-vacuums** §7's "cargos 6x = base imponible" invariant for any invoice that gets a base/IGV
edit: `AsientoContable.BasePEN` is populated today by nothing, so the invariant currently passes
trivially. Once populated, `validar` starts rejecting invoices whose hand-built líneas do not match.
This is correct per REGLAS but is a live behavior change, not a no-op. The general fix (wiring
`Componer`) stays out of scope in its own BACKLOG item.

## Data Flow

    worker camposNoExtraidos ─payload─→ InboxEvent ─CalculoDeIndicadores─→ fact.Factura.CamposNoExtraidos
                                                                                    │
    factura-form (per-field campoResaltado(campo)) ←── FacturaRespuesta ────────────┘

    detalle-page draft {baseImponible, igv, glosa} ──PATCH+If-Match──→ ValidacionDeCorreccion (pura)
              ↑                                                              │
              └──── cargarTodo() refetch ←── commit ←── AplicarCorreccion ──┤
                                                        ├→ TotalOrig/IgvOrig/Glosa + audit
                                                        ├→ PosibleDuplicado (iff triple changed)
                                                        └→ ProyeccionDeImportes → asiento BasePEN/IgvPEN/NetoPEN

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNetBD/schema/021_glosa_y_campos_no_extraidos.sql` | Create | `ALTER TABLE fact.Factura ADD Glosa NVARCHAR(250) NULL, CamposNoExtraidos NVARCHAR(500) NULL` (own `GO` batch). No GRANT change |
| `SmartNetBD/schema/rollback/021_down.sql`, `checksums.txt` | Create / Modify | Advisory drop of both columns; regen manifest via `generate-checksums.ps1` |
| `Inbox.Core/{IndicadoresFactura,CalculoDeIndicadores}.cs` | Modify | Carry the list alongside the derived bool |
| `Inbox.Infrastructure/{PayloadInboxParser,SqlPromocionRepository}.cs` | Modify | Parse + persist the delimited list |
| `Contable.Core/ProyeccionDeImportes.cs` | Create | Pure §5/§6 scalar derivation (D3) |
| `Facturacion.Core/{CorreccionFactura,ValidacionDeCorreccion,FacturaPersistida,IUnidadDeTrabajo}.cs` | Modify | Trailing `BaseImponible`/`Igv`/`Glosa`; D2 guards; `IgvOrig`/`Glosa`/`CamposNoExtraidos` on the record; two new ports |
| `Facturacion.Core/ServicioDeFacturas.cs` | Modify | `AplicarCorreccion` trio ladder + D4/D6/D7 in `PatchAsync` |
| `Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modify | Factura SELECT/UPDATE lists; `ExisteIdentidadPreviaAsync`; scalar projection write; narrowed `SinTipoCambio` predicate |
| `api/SmartNet.Api/FacturaEndpoints.cs` | Modify | `CorreccionFacturaRequest` + `FacturaRespuesta` (trailing, additive) |
| `SmartNetWeb/src/app/detalle/{models/factura.model.ts,ui/factura-form/*,feature/detalle-page/detalle-page.ts}` | Modify | Per-field `campoResaltado(campo)`, base/IGV/glosa inputs gated on `PENDIENTE_VALIDACION`, D5 refetch |

## Interfaces / Contracts

```csharp
public static ResultadoComando? Validar(FacturaPersistida original, CorreccionFactura cambios);

Task<bool> ExisteIdentidadPreviaAsync(long facturaId, string? ruc, string tipo, string? numero, CancellationToken ct);
Task<ResultadoEscritura> ActualizarProyeccionEscalarAsync(long asientoId, decimal basePen, decimal igvPen, decimal netoPen, CancellationToken ct);
```

```ts
readonly camposNoExtraidos: readonly string[];   // FacturaRespuesta, additive
readonly glosa: string | null;
campoResaltado(campo: string): boolean;          // replaces the invoice-wide computed()
```

## Testing Strategy

Strict TDD: every row is a RED test before implementation.

| Layer | What to Test | Approach |
|---|---|---|
| Unit (pure) | D2 guards (state gate, boleta/no-gravada `igv≠0` → 422, `base≥0`, `igv≤total`, pair-without-partner, pair+`totalOrig`); `ProyeccionDeImportes` against REGLAS §10.1/10.2/10.3 numbers; purity scan | xUnit, no I/O |
| Unit (SPA) | `campoResaltado(campo)` per field; inputs disabled unless `PENDIENTE_VALIDACION`; draft emits the pair and strips `totalOrig` | Vitest |
| Integration | Trio ladder + audit rows (D7); recompute iff triple changed (D6); scalar write + asiento `Version` bump (D4); `07` internal-ref not blocked by `SinTipoCambio`; PEN unaffected | `TestDatabaseFixture` |
| Contract | `021` applies idempotently; `ChecksumManifestTests`; `RollbackAdvisoryTests`; no new GRANT (`PermissionMatrixTests`) | Existing db-runner suites |
| E2E | Correct `numero` → duplicate banner clears without reload | Deferred to apply |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. All new inputs are parameterized SQL over existing typed columns.

## Migration / Rollout

Additive nullable columns; no backfill. Invoices promoted before `021` have
`CamposNoExtraidos IS NULL` → the SPA falls back to the existing `tieneCamposNoExtraidos` boolean,
i.e. today's coarse highlight. Rollback: `021_down.sql` + restore `checksums.txt`; `git revert` is
safe because no existing field changes name, type or meaning.

## Open Questions

- [ ] `AsientoContable.NetoPEN` semantics unverified. Design assumes `NetoPEN = BasePEN + IgvPEN`
      (`= TotalPEN`, §6 by construction). Confirm before apply — a wrong assumption silently
      corrupts a persisted column.
- [ ] D4 makes a §7 invariant live for edited invoices (see "Consequence"). Owner must accept that
      previously-validatable BORRADOR invoices may start failing `validar`.
- [ ] The `07` internal-reference carve-out rides §6's TC-inheritance rule, which §12 leaves
      **unratified**, and `FacturaReferenciaId` is unreachable until #10/#11, so the branch is
      latent today. Ratify or accept as dormant.
- [ ] Collateral finding — vacuous §7 line-sum invariants — is OUT of scope; open the new BACKLOG
      item "wire `ComposicionDeAsiento.Componer` into the confirm pipeline" before apply.
