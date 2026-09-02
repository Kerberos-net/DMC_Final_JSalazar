# Exploration: cablear-composicion-asiento (BACKLOG #24)

Wire `ComposicionDeAsiento.Componer` into the productive asiento lifecycle so
`AsientoContable.BasePEN/IgvPEN/NetoPEN` and the PRINCIPAL/DESTINO lines are engine-produced, not
hand-built, and REGLAS.md §7 invariants stop passing vacuously.

## Current State

### Engine exists, golden-tested, zero production callers

- `contable/SmartNet.Contable.Core/ComposicionDeAsiento.cs` — `static AsientoContable
  Componer(EntradaAsiento entrada)`. Fully implemented: PRINCIPAL (REGLAS §5 all 4 cases), DESTINO
  (reflejo/puente per cargo con CtaReflejaCodigo), conversion (§6 via
  ConversionDeMoneda.Convertir), percepción, NC herencia (afectación/cargos/motivo/TC congelados).
  Total function — never throws/rejects for accounting reasons (ADR 0006 BORRADOR phase).
- Golden-tested 1:1 vs REGLAS §10.1–§10.7 in `ComponerGoldenTests.cs` +
  `InvariantesDeConfirmacionTests.cs`.
- codegraph blast radius: `Componer` / `EntradaAsiento` referenced ONLY by `*.Core.Tests`.
  BACKLOG #24 premise confirmed verbatim.

### EntradaAsiento — net-new to build for a real factura (`EntradaAsiento.cs`, sealed record)

| Field | Source |
|---|---|
| ProveedorCodigo | fact.Factura.ProveedorCodigo (on FacturaPersistida) |
| EsRelacionada | ProveedorAtributo.EsRelacionada — NOT loaded today by CargarFacturaAsync/CargarAsientoAsync |
| Moneda (MonedaAsiento) | fact.Factura.Moneda mapped |
| FechaContable | asiento today uses factura.FechaEmision (CrearAsientoBorradorAsync) |
| MotivoDescripcion | from fact.Factura.Motivo (int) via dbo.Motivo — NOT loaded today |
| Comprobante | fact.Factura.TipoComprobante (CodigoComprobante.Convertir) |
| Afectacion | fact.Factura.Afectacion (MapearAfectacion — already in CargarAsientoAsync) |
| BaseOrig/IgvOrig | TotalOrig-IgvOrig / IgvOrig (both on FacturaPersistida since #19) |
| PercepcionOrig | NO column on fact.Factura today — net-new schema if percepción required now (REGLAS §10.4) |
| TipoCambio (TipoCambioCongelado?) | fact.TipoCambio venta for FechaEmision via ITipoCambioRepository.ObtenerVigenteAsync (already used for SinTipoCambio gate) |
| Cargos (IReadOnlyList<CargoSolicitado>) | CRUX. CargoSolicitado=(CuentaContable Cuenta, decimal ImportePEN). Needs resolved CuentaContable (frozen CtaRefleja/CtaPuente) from ICuentaContableRepository.ObtenerAsync + absolute PEN amount per account (deliberately NOT a proportion). Normal single-account: ImportePEN==basePEN; split: assistant decides amounts. |
| Herencia (HerenciaNotaCredito?) | only NC 07 internal ref — dormant path |

### Lifecycle today

1. POST /api/facturas/{id}/abrir -> ServicioDeFacturas.AbrirAsync (ServicioDeFacturas.cs:336): load
   factura, idempotency via ObtenerAsientoVigenteIdAsync, foreign-currency SinTipoCambio gate
   (409), then uow.CrearAsientoBorradorAsync(facturaId, proveedorCodigo, FechaEmision) — HEADER
   ONLY. SqlUnidadDeTrabajo.cs:554 INSERT fact.AsientoContable (FacturaId, OrigenLibro,
   ProveedorCodigo, FechaContable, Estado='BORRADOR'). BasePEN/IgvPEN/NetoPEN/MotivoDescripcion/
   TipoCambioVenta all NULL (schema/005_negocio.sql:126-131 nullable). No auditoría.
2. POST/PATCH/DELETE /api/asientos/{id}/lineas -> ServicioDeAsientos. Gate: BORRADOR. Each write ->
   one AuditoriaCorreccion(Accion=REPARTO_MANUAL, Campo="Cargos", Motivo=null) (design D6 row 7).
   NO invariant runs here. User hand-builds every line.
3. PATCH /api/facturas/{id} -> ServicioDeFacturas.PatchAsync (#19 design D4): if
   TotalOrig/IgvOrig/Moneda changed, re-derive 3 scalars via ProyeccionDeImportes.Derivar(...) and
   write to BORRADOR header via ActualizarProyeccionEscalarAsync. SCALAR ONLY — never touches
   lines. Foreign currency w/o frozen TipoCambioVenta -> write skipped.
4. POST /api/facturas/{id}/validar -> ValidarPorFacturaAsync -> ValidarInternoAsync
   (ServicioDeFacturas.cs:68): resolve vigente asiento, BORRADOR check, EvaluarHechosDeConflicto,
   InvariantesDeConfirmacion.Evaluar(persistido.Asiento, fechaCorteContable), correlativo, Estado
   -> CONFIRMADO + NumeroAsiento, MarcarFacturaValidadaAsync, FACTURA_VALIDADA outbox, commit.

### Asiento reconstruction

SqlUnidadDeTrabajo.CargarAsientoAsync (:54) builds AsientoContable from fact.AsientoContable header
cols + CargarLineasAsync (fact.AsientoContableDetalle). BasePEN/IgvPEN/NetoPEN come straight from
header cols, coalesced ?? 0m. NOT recomputed from lines; engine never produced them.

### Why §7 passes vacuously (and what #19 half-broke)

InvariantesDeConfirmacion.Evaluar:
- Global 1 SUM(Debe)==SUM(Haber): header + 0 lines -> 0==0 ok (vacuous).
- Global 2 (no line w/o account), Global 5 (Tipo/Debe/Haber): 0 lines -> ok (vacuous).
- PRINCIPAL: sumaCargos(0) vs esperadoCargos = esGravada ? asiento.BasePEN : asiento.NetoPEN.
  - Before #19: BasePEN==0 -> 0==0 ok (vacuous — the #24 complaint).
  - AFTER #19: user edits base/IGV -> BasePEN>0 while sumaCargos==0 -> invariant FAILS; 401111
    sub-check also fails for gravada. #19 half-opened this: editing scalars w/o lines now blocks
    validar. #24 must reconcile scalar projection + lines into one source.
- DESTINO: iterates PRINCIPAL lines w/ CtaReflejaCodigo != null; 0 -> ok (vacuous).
- Globals 3/4 (fecha corte, P00000): NOT vacuous — read header, already work.

Once composition runs, PRINCIPAL/DESTINO/Global-1/2/5 become real. Nothing breaks incorrectly;
empty asientos correctly start requiring a balanced engine-shaped asiento.

### Catálogo / plan de cuentas

- ICuentaContableRepository (catalogos/SmartNet.Catalogos.Core): only ListarPlanCompletoAsync +
  ObtenerAsync(cuenta) — NO prefix query. Prefix->candidate resolution (REGLAS §3) lives in
  sugerencia/SmartNet.Sugerencia.Core/ServicioDeSugerencia.cs.
- sugerencia module NOT wired into Program.cs (no DI, no endpoints) — per SmartNetApi/CLAUDE.md.
- CuentaContable carries CtaReflejaCodigo/CtaPuenteCodigo -> once account code picked, ObtenerAsync
  yields all CargoSolicitado needs; those get frozen onto the line (ADR 0006).
- ADR 0003: dbo.CuentaContable/dbo.Motivo external, usr_api has SELECT (008). Composition pure
  Core (ADR 0019) stays clean; only the EntradaAsiento builder (Infrastructure) reads catálogo. No
  dbo.* writes.

### Nota de crédito (#10) status

HerenciaNotaCredito type + Componer NC branches exist, golden-tested (§10.5–§10.7). BUT
CargarAsientoAsync comments NC-07-internal branch as "rama DORMIDA hoy: FacturaReferenciaId no se
puebla hasta el flujo de asociación de NC (#10/#11)". No productive NC association flow.
RECOMMENDATION: #24 wires factura/boleta path only, NC composition explicitly OUT OF SCOPE (needs
#10/#11 first); Componer already supports it so later wiring is additive.

### SPA

- detalle-page never calls /abrir. factura-form shows basePEN/igvPEN from asiento()?.basePEN.
  asiento-lineas edits lines. If no asiento exists, base/IGV blank and NO affordance to create one
  (#2 symptom).
- Any option: SPA needs an asiento to exist when detalle opens (auto /abrir at promotion OR
  "generar asiento" button) + decision on whether asiento-lineas stays editable and whether #19
  editable base/IGV/glosa remain.

### #25/#26 interaction

#25/#26 touch PromocionBackgroundService / SqlPromocionRepository (promotion + CamposNoExtraidos
CSV). #24 touches AbrirAsync / CrearAsientoBorradorAsync / ValidarInternoAsync /
ComposicionDeAsiento — different paths. Only overlap: if #24 fires /abrir automatically at
promotion, it calls into the promotion seam #25/#26 modified. Otherwise independent.

## Design decision to frame for owner

Binding constraints: REGLAS §5 "División del cargo"; ADR 0006 rejected "componer en el cliente y
crear al validar" (server must not trust client lines; Guardar avance must persist the asiento
with real lines); ADR 0006 "se genera en BORRADOR al abrir la factura", "ambos editables".

### Option 1 — Compose at abrir, manual editing still allowed

CrearAsientoBorradorAsync calls Componer with a single default CargoSolicitado (suggested account,
ImportePEN=basePEN), persists header+PRINCIPAL+DESTINO. User then adjusts lines (#12) and base/IGV
(#19); each edit re-runs composition or is layered.
- §7: real immediately; freshly-opened asiento already balanced/confirmable.
- #19: base/IGV edit must RECOMPOSE or lines/scalars diverge. Manual splits before a base/IGV edit
  lost on recompose — needs rule.
- Migration: dev/demo only. SPA: /abrir auto at promotion or button; asiento-lineas stays.
- ADR 0006 fit: strong. Test surface: medium-high. Size: Medium-Large.

### Option 2 — Compose at validar, lines stay manual during BORRADOR

validar re-composes from engine, DISCARDS manual lines, then §7 against composed asiento.
- §7: only on engine output — split invariant becomes tautological — exactly what CargoSolicitado
  "no proportion" + ADR 0019 warn against.
- #19: base/IGV just update scalars; lines recomputed at validar.
- ADR 0006 fit: WEAK — explicitly discarded alternative; makes #12 line editor pointless.
- SPA: asiento-lineas misleading (edits silently dropped). Test surface: lower. Size: Medium.

### Option 3 — Compose at abrir as seed; manual edits layered on top; §7 gates at validar (hybrid) — RECOMMENDED

Engine seeds header+lines at abrir. User adjustments persisted as-is (audited REPARTO_MANUAL).
validar runs §7 against whatever is persisted (seed + user deltas), NOT a re-composition. base/IGV
edit (#19) re-runs only scalar projection into header; assistant realigns lines; §7 blocks validar
if they don't (the "correct failure" #24 wants).
- §7: fully real and meaningful — split invariant actually checks user split vs BasePEN. Vacuous
  passes gone (lines always exist after abrir).
- #19: minimal change — keep D4 scalar projection exactly as is; the divergence it can create
  becomes a feature (validar blocks until reconciled). Optional SPA "recomponer" action.
- Migration: none (dev/demo). SPA: /abrir at promotion or button; asiento-lineas + #19 fields all
  stay; add "regenerar asiento" + inline "cabecera != líneas" warning.
- ADR 0006 fit: strongest. Test surface: medium. Size: Medium.

### SPA "generar asiento" affordance

Yes under any option. Cleanest: call /abrir automatically at promotion (factura enters
PENDIENTE_VALIDACION with asiento already seeded) so detalle-page always has an asiento + base/IGV.
Manual "regenerar/recomponer" button still useful for Option 1/3. Overlaps #25/#26 promotion seam
only if done at promotion.

## Recommendation: Option 3 (hybrid)

- Best ADR 0006 fit; keeps CargoSolicitado "absolute amounts, no proportion" intent — §7 split
  invariant becomes a genuine check not a tautology (Option 2 defeats it).
- Smallest disturbance to #19: D4 scalar projection stays byte-for-byte; its header/lines mismatch
  stops being a latent bug, becomes intended "validar blocks until reconciled".
- Manual line editing (#12) + #19 editable fields survive — no dead UI.
- NC composition stays out of scope (dormant FacturaReferenciaId), additive later.
- Trigger /abrir at promotion so SPA always has an asiento; add "recomponer" button.

## Open sub-decisions for propose phase

1. Percepción — support now (new fact.Factura column) or defer (REGLAS §10.4 unreachable until
   then)?
2. EsRelacionada + motivo-description loading — extend CargarFacturaAsync or new ports?
3. Recompose semantics when a base/IGV edit invalidates a manual split — warn, block, or
   auto-reprorate (like NC parcial)?
4. Does abrir recompose an existing BORRADOR asiento, or only seed on first create (today
   idempotent no-op)?
5. Account suggestion at seed time — wire sugerencia into DI, or seed with a single "sin cuenta"
   placeholder cargo (Global-2 then blocks validar)?

## Risks

- §7 ratification (REGLAS §12): #24 makes §5–§7 executable in production for the first time.
  Points 1 and 5 (TC venta, NC hereda TC) unratified, affect every foreign-currency asiento — but
  #24 does not change rules, only wires them; NC path stays out.
- #19 divergence: after #19, editing base/IGV on an asiento with no lines already fails validar.
  Any option must reconcile scalar projection + lines or existing dev asientos become
  unconfirmable.
- sugerencia not wired: seeding a real cargo account needs DI wiring of sugerencia OR a deliberate
  placeholder-line design.
- Percepción has no schema home — REGLAS §10.4 cannot be honored without a new column.
- Manual-split loss on recompose — needs explicit rule.
- SPA scope creep — "generar asiento" + mismatch warning + recompose button is real frontend
  work; keep minimal (auto-/abrir at promotion + one button).
- Test churn — InvariantesDeConfirmacionTests + ServicioDeFacturasPhase2Tests rewritten from
  "vacuous pass" to "real asiento" fixtures.

## Ready for Proposal: Yes

Recommend sdd-propose for cablear-composicion-asiento with Option 3 as proposed approach and the 5
open sub-decisions surfaced to owner. Mandatory context: REGLAS.md §5–§10, ADR 0006, ADR 0018, ADR
0019, plan de cuentas (Cuentas.xlsx). Scope: factura/boleta path only; NC composition explicitly
deferred to #10/#11 association flow.
