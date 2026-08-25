# Design: Diseño visual para SPA — login y detalle-validación

## Technical Approach

Three CSS layers in `SmartNet/spa/src/styles.css` (`@layer tokens, base, primitives`) plus per-component
`styleUrl` files for layout only. Angular emulated encapsulation already isolates component CSS, so no
BEM/utility methodology is needed; component styles sit outside `@layer` and therefore win over global
primitives without `!important`. One `TemaService` (signals, ADR 0009 pattern) writes
`document.documentElement.dataset.tema`. Zero new dependencies.

A second, thin backend slice feeds two of the visual pieces with real data: a read-only audit
repository (historial) and an additive projection of four indicator columns `fact.Factura` already
persists. ADR 0003 holds — `fact_api` already has `GRANT SELECT` on `fact.AuditoriaCorreccion`
(`008_usuarios_y_permisos.sql`) and `fact_worker` is already `DENY`-ed; no permission change. ADR 0016
holds — the only schema change is one additive `CREATE INDEX` in plain versioned SQL. ADR 0019 holds —
no accounting rule is added; both additions are projection/read.

## Architecture Decisions

### D1 — Theme switching: explicit `data-tema`, resolved in TS

| Option | Tradeoff | Decision |
|---|---|---|
| `prefers-color-scheme` media query only | No manual override | Rejected |
| Media query **+** `[data-tema]` override | Dark token block duplicated in two selectors (plain CSS has no mixin; `angular.json` has no SCSS) | Rejected |
| `data-tema` always explicit; `sistema` resolved in TS via `matchMedia` | One dark block; needs JS (already required) | **Chosen** |

`aplicarTemaInicial()` runs in `main.ts` before `bootstrapApplication` → no flash. `localStorage['fact.tema']`
is validated against `'claro' | 'oscuro' | 'sistema'` before touching the DOM; any other value → `'sistema'`.
Tokens also set `color-scheme: light|dark` so native `<select>` (visor-documento) and `<input type="date">`
(detalle-page) follow the theme.

### D2 — One amber ink for both alert levels; difference is treatment, not hue

Two ambers would break "un color = un significado" (brief). `--alerta-ink` is shared; **bloqueante** =
tinted fill + 4px left rule; **informativo** = 1px border + icon, no fill. Verified: the tint alone is only
1.06:1 against the page, so the 4px rule at 4.73:1 is what actually carries the signal — it is mandatory,
not decorative.

### D3 — Hue budget (one hue = one meaning)

| Meaning | Hue | Non-color redundancy |
|---|---|---|
| Pendiente de validación | neutral (resting state, not an alarm) | filled chip, solid border |
| Validada | verde | filled chip, solid border |
| Descartada | gris | outline-only chip, dashed border |
| Alerta (bloqueante / informativa) | ámbar | fill + 4px rule / 1px border |
| Conflicto 412 | violeta | banner + "recargar" action |
| Error validación 422 | rojo | inline under the field |
| Focus / links | azul | outline only, never a fill |

Amber (alerta) and red (422) are the hardest pair under protanopia/deuteranopia and both appear on
detalle-page. Mitigated by three redundant channels: distinct **placement** (banner vs. inline), a distinct
inline **SVG shape** (`fill="currentColor"`, 16px, `aria-hidden="true"`), and an always-visible **text
label**. Color is never the sole carrier (WCAG 1.4.1).

### D4 — Historial: native `<details>`/`<summary>`

Zero component state, built-in keyboard + AT semantics, closed by default, caret drawn in CSS
(`summary::before`, rotated on `[open]`) — no icon asset. Rejected: Angular signal + hand-rolled
`aria-expanded`/`aria-controls` (more state, more CSS, more tests, no gain). No animation: instant toggle
suits a dense tool, and `::details-content` is not portable yet.

### D5 — Typography: system stack, no webfont

`--fuente-ui: ui-sans-serif, "Segoe UI Variable Text", "Segoe UI", system-ui, -apple-system, Arial, sans-serif`.
Numbers use `font-variant-numeric: tabular-nums` + `text-align: right` on the UI font (keeps density; avoids
the "code" look of a mono switch). `--fuente-mono` is reserved for identifiers where character
disambiguation matters: `cuentaCodigo`, `numero`, `rucProveedor`. Dense scale, 14px base — 12 / 13 / **14** /
16 / 20 / 24px; weights 400/500/600; `--lh-densa: 1.35`, `--lh-normal: 1.5`. Only 24px qualifies as WCAG
"large text"; every text token below is held to 4.5:1, so the palette never leans on the 3:1 exemption for text.

### D6 — CSS split against the 8kB `anyComponentStyle` budget

`styles.css` is global (no per-component budget) and owns tokens, base/reset and the shared primitives
`.btn`, `.campo`, `.chip`, `.tabla`, `.alerta`, `.panel`, `.banner`. Component files own **layout and
composition only** — grid, sticky, sizing, ordering. Target ≤2kB per component, enforced by
`ng build --configuration production` (8kB = hard error).

### D7 — Audit read lives in a dedicated `IAuditoriaRepository`, not on `IUnidadDeTrabajo`

| Option | Tradeoff | Decision |
|---|---|---|
| New member on `IUnidadDeTrabajo` | It is a *transaction session* (`IAsyncDisposable` owning a `SqlTransaction`, rollback unless `CommitAsync`) — a pure SELECT would open and roll back a transaction; its header also states the signature was fixed in design.md and is not extended casually | Rejected |
| Dedicated `IAuditoriaRepository` (Core) + `SqlAuditoriaRepository` (Infrastructure), injected into the endpoint | One more type | **Chosen** |

This is the codebase's own read-side convention: `IBandejaRepository`, `IEstadoIntegracionRepository`,
`ITipoCambioRepository`, `ISugerenciaCuentaRepository` all follow it. (`CargarDocumentosFacturaAsync`
is read-only *and* on the UoW, but only because its endpoint already opens one for the factura's 404
and ETag — the historial endpoint needs neither.)

One SQL resolves the whole trail without a UoW, because the user perceives one document, not three
entities:

```sql
WHERE (a.EntidadTipo='FACTURA' AND a.EntidadId=@facturaId)
   OR (a.EntidadTipo='ASIENTO'  AND a.EntidadId IN (SELECT AsientoContableId FROM fact.AsientoContable WHERE FacturaId=@facturaId))
   OR (a.EntidadTipo='ADJUNTO'  AND a.EntidadId IN (SELECT AdjuntoManualId  FROM fact.AdjuntoManual  WHERE FacturaId=@facturaId))
ORDER BY a.OcurridoEn DESC, a.AuditoriaCorreccionId DESC;
```

ANULADO asientos are deliberately included — an anulación is exactly what an audit trail must show.
`GET /api/facturas/{id}/historial` returns `200 []` for an unknown id rather than 404: the endpoint
would need an extra existence query to tell "unknown" from "no corrections", and the SPA only calls it
for an id whose `GET /api/facturas/{id}` already succeeded. `UsuarioId` is returned but not rendered
(single-user product); no join to `fact.Usuario`.

### D8 — One additive index (ADR 0016)

`fact.AuditoriaCorreccion` has only `PK (AuditoriaCorreccionId)`; the query above filters on
`(EntidadTipo, EntidadId)` and would table-scan. New file
`SmartNet/db/schema/017_indice_auditoria_por_entidad.sql`:
`CREATE INDEX IX_AuditoriaCorreccion_Entidad ON fact.AuditoriaCorreccion (EntidadTipo, EntidadId) INCLUDE (Accion, Campo, ValorOriginal, ValorNuevo, Motivo, UsuarioId, OcurridoEn);`
Additive, no data change, rollback = `DROP INDEX` in `rollback/017_down.sql`. Not EF Core, not Alembic.

### D9 — Indicators: widen the existing projection, no new mapping

`fact.Factura` already stores `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`,
`AfectacionMixta` (`005_negocio.sql`), and `SqlBandejaRepository.ListarAsync` already reads them.
`SqlUnidadDeTrabajo.CargarFacturaAsync` simply does not select them. Fix at the source: add the four
columns to that `SELECT` and four **trailing** parameters to `FacturaPersistida` (defaults `false`/`null`),
which keeps every existing positional call site source-compatible. `FacturaRespuesta.De` then projects
them. No new DTO, no new mapper, no second query.

Safe by construction: `GuardarFacturaAsync`'s `UPDATE` sets only Estado / ProveedorCodigo / RucProveedor /
TotalOrig / Moneda / FechaEmision / Motivo / Afectacion — it never writes an indicator column, so these
four are read-only projections and cannot be clobbered by a `PATCH` round-trip.

Visual binding (already-designed treatments, now with real data): `PosibleDuplicado` and
`EsProveedorGenerico` → `.alerta--bloqueante`; `TieneCamposNoExtraidos` and `AfectacionMixta === null` →
`.alerta--informativa`. Note `Afectacion` (VARCHAR, GRAVADA/EXONERADA/INAFECTA) and `AfectacionMixta`
(three-state BIT) are different columns: "no verificada" is `AfectacionMixta IS NULL`, not `Afectacion IS NULL`.

### D10 — Confirmación de afectación: write the column, do NOT arm the gate in this change

`CasoConflicto.AfectacionNoVerificada` already exists and `ServicioDeFacturas.EvaluarHechosDeConflicto`
already blocks on it — but `SqlUnidadDeTrabajo` hardcodes that fact to `false` (its own header documents
this, line 24), so the gate is dormant. Two separable pieces:

| Piece | Nature | In this change |
|---|---|---|
| Project `AfectacionMixta` → indicator (D9) | pure read | **Yes** |
| `POST /api/facturas/{id}/confirmar-afectacion` — CAS on `Version` via `IfMatch.Requerido`, `UPDATE AfectacionMixta`, `RegistrarAuditoriaAsync(CONFIRMACION_AFECTACION)`, `CommitAsync` (same shape as `abrir`/`validar`/`descartar`) | additive write | **Yes** |
| Wire `HechosDeConflicto.AfectacionNoVerificada` to read the column | **behaviour change** | **No — needs a go/no-go** |

Rationale: flipping that one boolean makes every existing pending factura with `AfectacionMixta IS NULL`
non-validable overnight. That is a product/accounting decision with a backfill implication, not a
projection, and `CLAUDE.md` ranks correctitud contable above shipping. Keeping it out leaves this change
strictly additive and reversible; arming it later is a one-line follow-up. See Open Questions.

## Palette and verified contrast

Relative luminance per WCAG 2.x; every ratio below is computed, not assumed. Floors: **4.5:1** text,
**3:1** non-text UI (borders, focus, chip outlines).

**Tema claro** — `--fondo-app #F7F8FA`, `--fondo-superficie #FFFFFF`

| Token | Hex | Against | Ratio | Floor |
|---|---|---|---|---|
| `--texto-principal` | `#16191D` | fondo-app | **16.60** | 4.5 |
| `--texto-secundario` | `#5A6270` | fondo-app | **5.79** | 4.5 |
| `--borde-control` | `#767E8C` | fondo-app | **3.85** | 3 |
| `--borde-sutil` | `#D5D9E0` | — | decorative divider | n/a |
| `--alerta-ink` | `#B45309` | fondo-app | **4.73** | 4.5 |
| `--alerta-texto` | `#7C3D00` | `--alerta-fondo #FDF0E1` | **7.42** | 4.5 |
| `--conflicto-ink` | `#6D28D9` | fondo-app / `--conflicto-fondo #F1EBFD` | **6.69** / **6.10** | 4.5 |
| `--error-ink` | `#B91C1C` | fondo-app / `--error-fondo #FDECEC` | **6.09** / **5.67** | 4.5 |
| `--estado-pendiente-texto` | `#363C46` | `--estado-pendiente-fondo #EAECF0` | **9.39** | 4.5 |
| `--estado-pendiente-borde` | `#767E8C` | fondo-app | **3.85** | 3 |
| `--estado-validada-texto` | `#14532D` | `--estado-validada-fondo #DCF3E3` | **7.80** | 4.5 |
| `--estado-validada-borde` | `#1E7A44` | fondo-app | **5.04** | 3 |
| `--accion-texto` | `#FFFFFF` | `--accion-fondo #1F2937` | **14.68** | 4.5 |
| `--focus-ring` | `#1D4ED8` | fondo-app | **6.31** | 3 |

**Tema oscuro** — `--fondo-app #12151A`, `--fondo-superficie #1A1F26`

| Token | Hex | Against | Ratio | Floor |
|---|---|---|---|---|
| `--texto-principal` | `#E8EBF0` | fondo-app / superficie | **15.31** / **13.86** | 4.5 |
| `--texto-secundario` | `#A2ABBA` | fondo-app / superficie | **7.90** / **7.15** | 4.5 |
| `--borde-control` | `#6E7787` | fondo-app / superficie | **4.05** / **3.67** | 3 |
| `--alerta-ink` | `#E8A33D` | superficie / `--alerta-fondo #3A2A14` | **7.68** / **6.40** | 4.5 |
| `--conflicto-ink` | `#B79BF5` | superficie / `--conflicto-fondo #241C3D` | **7.11** / **6.89** | 4.5 |
| `--error-ink` | `#F58787` | superficie / `--error-fondo #3A1E1E` | **6.86** / **6.28** | 4.5 |
| `--estado-pendiente-borde` | `#8A93A3` | fondo-app | **5.91** | 3 |
| `--estado-validada-borde` | `#58C97F` | fondo-app | **8.78** | 3 |
| `--accion-texto` | `#12151A` | `--accion-fondo #E8EBF0` | **15.31** | 4.5 |
| `--focus-ring` | `#7FA9FF` | fondo-app | **7.86** | 3 |

Chip/alert text in dark mode uses `--texto-principal` over its tinted fill (11.5–13.4:1). Shadows do not
read on dark: `--sombra-*` degrade to `--borde-sutil` elevation under `[data-tema="oscuro"]`.

## Data Flow

    main.ts ──aplicarTemaInicial()──→ <html data-tema>
                                          │
    TemaService (signal) ─────────────────┘   (localStorage 'fact.tema')
                                          │
    styles.css @layer tokens ─── resolves --* per [data-tema]
                                          │
    global primitives ──── component styleUrl (layout only) ──→ rendered UI

Backend slice (D7–D10). `DetallePage` is the only fetcher; `HistorialCorreccion` and `FacturaForm`
stay presentational, per the container/presentational split their own headers document:

    fact.Factura ──CargarFacturaAsync (+4 cols)──→ FacturaPersistida ──→ FacturaRespuesta
                                                                              │
    fact.AuditoriaCorreccion ──SqlAuditoriaRepository──→ EntradaAuditoriaRespuesta
                                                                              │
                                    DetallePage (container, signals) ←────────┘
                                          │                    │
                          [historial] ────┘                    └──── [indicadores]
                                          ▼                                ▼
                            <app-historial-correccion>          <app-factura-form>
                              <details> closed by default        .alerta--bloqueante / --informativa

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/spa/src/styles.css` | Modify | `@layer tokens, base, primitives`; both themes; `color-scheme` |
| `SmartNet/spa/src/main.ts` | Modify | Call `aplicarTemaInicial()` before bootstrap |
| `SmartNet/spa/src/index.html` | Modify | `lang="es"`, real `<title>` |
| `SmartNet/spa/src/app/shared/tema.service.ts` | Create | Signal service + pure `resolverTema()` |
| `SmartNet/spa/src/app/shared/tema.service.spec.ts` | Create | RED first (D1) |
| `SmartNet/spa/src/app/shared/contraste.ts` (+ `.spec.ts`) | Create | Pure WCAG ratio fn; spec asserts every pair above |
| `SmartNet/spa/src/app/app.html` / `app.css` | Modify | Minimal shell header + native `<select>` theme control |
| `login-page.{ts,html,css}` | Modify | `styleUrl`; centered card; 401 message uses `.alerta` |
| `detalle-page.{ts,html,css}` | Modify | `styleUrl`; 2-col grid, sticky visor, collapses <1100px |
| `factura-form.{ts,html,css}` | Modify | `styleUrl`; estado chip; P00000 → bloqueante; null field → informativo |
| `asiento-lineas.{ts,html,css}` | Modify | `styleUrl`; tabular table (unchanged otherwise) |
| `visor-documento.{ts,html,css}` | Modify | `styleUrl`; iframe fill, selector |
| `conflicto-banner.{ts,html,css}` | Modify | `styleUrl`; 412 violeta + shape vs 422 rojo + shape |

Backend and its SPA consumers (D7–D10):

| File | Action | Description |
|---|---|---|
| `SmartNet/db/schema/017_indice_auditoria_por_entidad.sql` (+ `rollback/017_down.sql`) | Create | `IX_AuditoriaCorreccion_Entidad`, additive (D8) |
| `SmartNet.Facturacion.Core/IAuditoriaRepository.cs` | Create | `ListarPorFacturaAsync` |
| `SmartNet.Facturacion.Infrastructure/SqlAuditoriaRepository.cs` | Create | The D7 SELECT; own `SqlConnection`, no transaction |
| `SmartNet.Facturacion.Core/FacturaPersistida.cs` | Modify | 4 **trailing** params, defaults — source-compatible (D9) |
| `SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modify | 4 columns into `CargarFacturaAsync`'s SELECT; `UPDATE` untouched |
| `SmartNet.Api/AuditoriaEndpoints.cs` | Create | `GET /api/facturas/{id}/historial` |
| `SmartNet.Api/FacturaEndpoints.cs` | Modify | `FacturaRespuesta` +4 fields; `POST /api/facturas/{id}/confirmar-afectacion` (D10) |
| `SmartNet.Api/Program.cs` (DI wiring) | Modify | Register `IAuditoriaRepository` |
| `spa/.../detalle/models/{factura,historial}.model.ts` | Modify/Create | Mirror the widened DTO + `EntradaAuditoriaRespuesta` |
| `spa/.../detalle/data-access/historial.service.ts` (+spec) | Create | Signals service, ADR 0009 pattern |
| `spa/.../detalle/ui/historial-correccion/*` | Create | Presentational `<details>` panel (D4) |
| `spa/.../detalle/feature/detalle-page/*` | Modify | Fetch historial; pass `[historial]`/indicators down |

## Interfaces / Contracts

```ts
export type PreferenciaTema = 'claro' | 'oscuro' | 'sistema';
export type TemaEfectivo = 'claro' | 'oscuro';
export function resolverTema(p: PreferenciaTema, prefiereOscuro: boolean): TemaEfectivo;
export function aplicarTemaInicial(): void; // main.ts, pre-bootstrap
export function contraste(hexA: string, hexB: string): number; // WCAG 2.x
```

```csharp
public interface IAuditoriaRepository
{
    Task<IReadOnlyList<EntradaAuditoria>> ListarPorFacturaAsync(long facturaId, CancellationToken ct);
}

// GET /api/facturas/{id}/historial -> 200 EntradaAuditoriaRespuesta[] (newest first; [] if none/unknown)
internal sealed record EntradaAuditoriaRespuesta(
    string EntidadTipo, long EntidadId, string Accion, string? Campo,
    string? ValorOriginal, string? ValorNuevo, string? Motivo, long UsuarioId, DateTimeOffset OcurridoEn);

// FacturaRespuesta gains exactly four fields, all additive, none existing field changes shape:
//   bool EsProveedorGenerico, bool PosibleDuplicado, bool TieneCamposNoExtraidos, bool? AfectacionMixta

// POST /api/facturas/{id}/confirmar-afectacion -> same If-Match/CAS shape as abrir/validar/descartar
internal sealed record ConfirmarAfectacionRequest(bool EsMixta);
```

Alert-level contract: `.alerta--bloqueante` (fill + 4px rule) for `PosibleDuplicado` /
`EsProveedorGenerico`; `.alerta--informativa` (1px border + icon) for `TieneCamposNoExtraidos` /
`AfectacionMixta === null`.

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Unit (pure) | `resolverTema`, invalid `localStorage` → `'sistema'` | Vitest, no DOM |
| Unit (pure) | Every documented token pair meets its floor | `contraste.spec.ts` table — regression guard against later "prettified" hexes |
| Component | `.alerta--bloqueante` iff `posibleDuplicado \|\| esProveedorGenerico`; `.alerta--informativa` iff `tieneCamposNoExtraidos \|\| afectacionMixta === null`; `.banner--conflicto` on 412 vs `.banner--error` on 422; `<details>` closed by default | `ng test` (Vitest + jsdom) |
| Build | No component CSS > 8kB | `ng build --configuration production` |
| Unit (.NET) | `GET /historial` shape/order; unknown id → `200 []`; `FacturaRespuesta` carries the 4 indicators | `dotnet test` — `SmartNet.Api.Tests` (`FacturaEndpointsTests` pattern) |
| Contrato-de-frontera | `SqlAuditoriaRepository` returns FACTURA + ASIENTO (incl. ANULADO) + ADJUNTO entries for a factura; `CargarFacturaAsync` round-trips the 4 columns; a `PATCH` does NOT clobber them | `SqlUnidadDeTrabajoTests` fixture (ADR 0019 level 2) |
| Schema shape | `IX_AuditoriaCorreccion_Entidad` exists; `fact_api` still `SELECT`-only-plus, `fact_worker` still denied | `SchemaShapeTests` / `PermissionMatrixTests` (both already exist) |

Gotchas: jsdom does not implement `matchMedia` — `TemaService` specs must stub it. `FakeUnidadDeTrabajo`
(`SmartNet.Facturacion.Core.Tests`) constructs `FacturaPersistida` positionally; the four new params must
be trailing with defaults or that fake breaks.

## Threat Matrix

No shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary.
Two routing rows are now applicable and carry design requirements:

| Row | Status | Expected behaviour / RED test |
|---|---|---|
| New HTTP routes (`GET /api/facturas/{id}/historial`, `POST /api/facturas/{id}/confirmar-afectacion`) | Applicable | Both `.RequireAuthorization()` like every sibling route; `{id:long}` route constraint. RED: unauthenticated request → 401, not 200/500 |
| SQL injection surface (new SELECT/UPDATE) | Applicable | Parameterized `SqlParameter` only, matching `SqlUnidadDeTrabajo`/`SqlBandejaRepository`; no string interpolation into SQL. RED: covered by the frontera fixture, which passes ids as parameters |
| Client input trust | Applicable | `localStorage` theme value allowlisted to `'claro'\|'oscuro'\|'sistema'` before reaching `dataset.tema` (D1). RED: tampered value → falls back to `'sistema'` |
| Privilege boundary | Applicable | No permission change; `fact_api` already has `SELECT` on `fact.AuditoriaCorreccion`, `fact_worker` already `DENY`. RED: existing `PermissionMatrixTests` must still pass unmodified |

## Migration / Rollout

No data migration. Absent/invalid `localStorage` → `'sistema'`. The only schema change is one additive
`CREATE INDEX` (D8), rolled back by `DROP INDEX`. `FacturaRespuesta` grows additively — no existing field
changes shape, so `factura-form` keeps working unchanged during rollout. No backfill: `AfectacionMixta`
stays `NULL` where it is `NULL`, and D10 deliberately leaves the validation gate dormant so no factura
that was validable yesterday becomes blocked. Revert = `git revert` of the style + API commits.

## Open Questions

- [ ] **Decision needed — arm the `AfectacionNoVerificada` gate?** (D10) The `CasoConflicto` and its
      block already exist in `ServicioDeFacturas`; only the Infrastructure fact is hardcoded `false`.
      Wiring it makes every pending factura with `AfectacionMixta IS NULL` non-validable until the
      assistant confirms — which is what `DESIGN_BRIEF.md` §3 describes, but it is a behaviour change
      with a backfill question, so this design does **not** include it. Recommendation: ship the
      indicator + confirmation endpoint now, arm the gate in a follow-up once the assistant has a way
      to clear the existing `NULL` backlog. Does not block implementation of this change.
- [ ] Confirm the app-shell header (theme control) is acceptable on the login page too.
