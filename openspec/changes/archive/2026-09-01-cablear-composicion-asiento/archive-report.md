# Archive Report: cablear-composicion-asiento (BACKLOG #24)

**Date**: 2026-09-01
**Status**: COMPLETE AND ARCHIVED
**Verdict**: pass-with-warnings (verify-report; 0 CRITICAL, 4 WARNING, 1 SUGGESTION; `sdd-verify-validate` valid: true)

## Change Summary

Wire `ComposicionDeAsiento.Componer` (golden-tested against REGLAS §10.1–§10.7, previously zero
production callers) into the productive asiento lifecycle so `AsientoContable.BasePEN/IgvPEN/NetoPEN`
and the PRINCIPAL/DESTINO lines are engine-produced, not hand-built, and REGLAS §7 invariants stop
passing vacuously. Owner decision: **Option 3 (hybrid)** — engine seeds at `abrir`/promotion,
manual (#12) and #19 edits layer on top audited `REPARTO_MANUAL`, `validar` runs §7 against the
persisted asiento (no re-composition).

**Scope**: factura/boleta path only. NC composition (#10/#11) and percepción (§10.4) explicitly
deferred (non-goals).

## Implementation (final state)

### Core (ADR 0019 pure)
- NEW `HechosDeComposicion` record + `SembradoDeAsiento.Construir/.Sembrar` statics.
  `Sembrar` drops `Debe==0 && Haber==0` lines (fixes a `CK_Linea_Tipo` 500 on a GRAVADA factura
  with `IgvOrig=0`) and renumbers `Orden` 1..n.
- UNCHANGED byte-for-byte: `ComposicionDeAsiento.Componer`, `InvariantesDeConfirmacion.Evaluar`,
  `ProyeccionDeImportes.Derivar`, `PatchAsync` D4, `CargarAsientoAsync`.

### Service + persistence
- `ServicioDeFacturas.AbrirAsync` composes + seeds header + PRINCIPAL/DESTINO on first BORRADOR
  create (idempotent no-op thereafter).
- `IUnidadDeTrabajo` gains `ResolverHechosDeComposicionAsync`,
  `CrearAsientoBorradorAsync(long, AsientoContable, ct)`, `ReemplazarLineasAsync`,
  `ObtenerCuentaContableAsync`. `SqlUnidadDeTrabajo` resolver reads `fact.ProveedorAtributo` +
  `dbo.Motivo` in-tx (ADR 0003 grants already in `008` — no schema script).
- `ServicioDeAsientos.RecomponerAsync` regenerates lines + header scalars, writes one
  `REPARTO_MANUAL` audit row (no new `Accion` enum value).

### API + promotion + DI
- `POST /api/asientos/{id}/recomponer` — `If-Match` required, optional `{cuentaCodigo}` body,
  409 CONFIRMADO / 412 stale / 422 unknown cuentaCodigo / 404.
- `validar` after a base/IGV edit that unbalances lines → 422 `InvarianteContable.Principal`
  "Los cargos 6x/1x suman {X}, se esperaba {N}" (owner decision 4). `recomponer` is the way out.
- `PromocionBackgroundService` fires `/abrir` at promotion via new `ISembradorDeAsiento` port +
  `SembradorDeAsientoAdapter` (swallows `SinTipoCambio`/`NoEncontrado`). Foreign-currency no-TC:
  promotion succeeds without an asiento; SPA offers "generar asiento" later (owner decision C3).
- `ServicioDeSugerencia` wired into `Program.cs` DI (compose-time consumer only, no HTTP endpoint).

### SPA
- `asiento.service.recomponer(asientoId, cuentaCodigo?)`, `factura.service.abrir(id)`.
- `detalle-page`: "recomponer asiento" button (BORRADOR-only, two-step confirm), "generar asiento"
  affordance when `asiento()` is null, descuadre marker bound to the existing `cuadre()` computed.
- `factura-form` base/IGV now populated from the seeded asiento; `asiento-lineas` unchanged.

## Test evidence (verified in isolation)

| Suite | Result |
|---|---|
| `dotnet build SmartNet.sln` | 0 warnings / 0 errors |
| `SmartNet.Facturacion.Core.Tests` | 186/186 |
| `SmartNet.Contable.Core.Tests` | 51/51 |
| `SmartNet.Facturacion.Infrastructure.Tests` | 72/72 |
| `SmartNet.Inbox.Infrastructure.Tests` | 75/75 (was 71 — #25/#26 regression guards, no new call into `ProcesarDocumentoAsociadoAsync`) |
| `SmartNet.Api.Tests` | 212/212 (incl. REGLAS §10.1/§10.2/§10.3 E2E goldens + 422-descuadre→recomponer→200) |
| SPA `npm test` | 491/491 (52 files) |
| SPA `npm run lint` | clean |

Spec compliance: 12/12 requirements, 32/34 scenarios covered; 2 deferred (§10.4 percepción — no
`fact.Factura.PercepcionOrig` column, owner decision).

## Delta specs merged into `openspec/specs/`

`nucleo-contable` (invariantes globales de-vacuumed + Componer seed requirement), `api-facturas`
(POST /abrir engine seed + recomponer + validar §7 real + base/IGV blocking), `factura-promotion`
(promotion seeds the BORRADOR asiento), `sugerencia-cuenta` (DI registration + suggested-account
seeding), `pantalla-detalle-validacion` (asiento assumed present + recomponer action + descuadre
marker).

## Verify warnings (non-blocking)

1. `SembradorDeAsientoAdapter` swallow logic partially tested (no direct throw test; promotion-side
   fake cannot throw).
2. §10.4 percepción — 2 scenarios deferred by owner decision.
3. Descuadre marker binds to `cuadre()` (all-líneas Debe vs Haber), not literally
   `sum(PRINCIPAL cargos) ≠ header BasePEN` — a balanced-but-misallocated manual edit could hide
   the client marker while server-side §7 still returns 422 (server gate proven by E2E).
4. §10.5 E2E goldens rely on the sugerencia Tier-3 first-candidate path (one seeded hoja per
   prefix); multi-candidate / usage-history branches are covered by the sugerencia module's own
   suite, not here.

SUGGESTION: `MapearAfectacion` is now duplicated 3× — hoist to a shared helper (follow-up).

## Follow-ups

- REGLAS §12 points 1 & 5 (TC venta, NC hereda TC): recorded in `DEUDA-TECNICA.md` row 5.2 — a
  note, not a ratification gate (owner decision 7). Point 1 already executes via #19; point 5
  unreachable this cycle.
- Existing dev/demo asientos (NULL scalars, zero lines) become unconfirmable — intended;
  `recomponer` regenerates them. No data migration (dev/demo scope).
- BACKLOG #24 to be added to `BACKLOG.md` as closed (owner-managed).
- Lineage: #19 → #24 (this) → #25, #26 (shipped this session) → #28 (draft — bandeja dedup, the
  third reported symptom).

## Commit hygiene

Working tree is on `main` and carries all of Batches 1–9 on top of the 11 commits from earlier
this session. The #24 commit stages only this cycle's files: `SmartNet/SmartNetApi/facturacion/**`,
`contable/**`, `inbox/**`, `api/**`, `SmartNet/SmartNetWeb/src/app/detalle/**`, `DEUDA-TECNICA.md`,
`openspec/**`.
