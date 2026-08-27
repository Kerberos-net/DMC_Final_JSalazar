# Exploration: item-18-ajuste-visual-spa (BACKLOG #18 — Ajuste visual del diseño SPA)

## Summary
Item #18 = visual conformance pass: align the already-built, already-styled SPA (login + detalle-validacion + tokens/theme) with the design handoff (`handoff/DESIGN_BRIEF.md` + `handoff/Gestor de Facturas.dc.html`). Functional SPA (#12) and its FIRST visual pass (archived change `2026-08-24-diseno-visual-spa-item-12`) already exist. That first pass was done "con criterio" WITHOUT a formal mockup (a risk it flagged). The `.dc.html` canvas is that missing mockup and it diverges from the first pass's choices. #18 reconciles them.

## SPA location
`SmartNet/SmartNetWeb/` (Angular standalone, signals, OnPush, zero UI libs — ADR 0009). sdd-init guessed `SmartNet/web/`; #12-visual proposal said `SmartNet/spa/`. ACTUAL: `SmartNet/SmartNetWeb/`. Feature-sliced: `src/app/{login,detalle,inbox,configuracion,shared}/{feature,ui,data-access,models}`.

## Screens/components today
- IN SCOPE: `login/feature/login-page`; `detalle/feature/detalle-page` (container); `detalle/ui/{factura-form, asiento-lineas, visor-documento, conflicto-banner, historial-correccion}`; `shared/{tema.service, contraste.ts}`; app shell `app.html`/`app.css` (header + `<select>` theme switch). All already have thin token-only CSS.
- OUT OF SCOPE: `inbox/*` (bandeja #13: inbox-page, inbox-list, inbox-filter, panel-errores, confirmar-reproceso) and `configuracion/*` (#17) — these currently have NO component CSS (global primitives only); `registro-de-compra` screen does not exist. Token ripple will touch them but they stay excluded.

## Token/theme architecture (implemented, src/styles.css)
`@layer tokens, base, primitives;` global; component styleUrl files sit OUTSIDE layers, layout/composition only, no color/font literals. `angular.json` anyComponentStyle budget 4kB warn / 8kB error. Theme: `html[data-tema=claro|oscuro]`; `aplicarTemaInicial()` pre-bootstrap in main.ts; `TemaService` signal+computed; 'sistema' resolved via matchMedia; `localStorage['fact.tema']` allowlisted. `contraste.ts` + `contraste.spec.ts` = pure WCAG guard, every token pair re-asserted.
Current light tokens: `--fondo-app #f7f8fa`, `--fondo-superficie #fff`, `--borde-control #767e8c` / `--borde-sutil #d5d9e0`, `--alerta-*` amber `#b45309`, `--conflicto-*` violet `#6d28d9`, `--error-*` red `#b91c1c`, `--estado-{pendiente,validada,descartada}-*`, `--accion-fondo #1f2937` (near-black primary btn), `--focus-ring #1d4ed8`, `--sombra-panel 0 1px 2px` (flat). Radii 6px/8px. Type 12/13/14/16/20/24 Segoe-first. Primitives: `.btn`/`.btn--secundario`/`.btn--peligro`, `.campo`/`.campo--resaltado` (2px amber), `.chip--{pendiente,validada,descartada}`, `.tabla`, `.tabular-nums`, `.alerta--{bloqueante,informativa}`, `.panel`, `.banner--{conflicto,error}`.

## First visual pass choices now CONTRADICTED by handoff (archive 2026-08-24-diseno-visual-spa-item-12/design.md)
- D3 hue budget: "pendiente" = NEUTRAL GRAY ("resting state, not alarm"); blue = focus/links only, never a fill; NO accent color; primary btn near-black.
- D2: duplicado AND proveedor P00000 both -> `.alerta--bloqueante` (amber, blocks Validar).
- Near-flat elevation, 6px radius.

## Handoff (.dc.html — live x-dc prototype; palette in `colors()` ~L1333, `estadoInfo()` ~L1291)
Gives USABLE concrete hex/px/radii/shadows, but as inline styles not a token file.
Palette light | dark:
- bg/surface/surface-2/sidebar: `#f5f5f7`/`#ffffff`/`#fbfbfc`/`#eef0f3` | `#1c1c1e`/`#2c2c2e`/`#242426`/`#232326`
- text/secondary/tertiary: `#1d1d1f`/`#6e6e73`/`#86868b` | `#f5f5f7`/`#98989d`/`#6e6e73`
- border hairline translucent: `rgba(0,0,0,0.08)` | `rgba(255,255,255,0.09)`
- ACCENT (primary btn, links, logo, active nav): `#0071e3` | `#0a84ff`; accent-soft `rgba(0,113,227,0.08)` | `rgba(10,132,255,0.16)`
- estado pendiente: BLUE `#0071e3` on `rgba(0,113,227,0.1)` | `#409cff`
- estado validada: `#1f8a3d` on `rgba(52,199,89,0.13)` | `#30d158`
- estado error: `#d70015` | `#ff453a`
- estado alerta: `#c93400` burnt orange on `rgba(255,149,0,0.13)` | `#ff9f0a`
Radii: inputs/buttons 8px, inner cards 10-12px, modals/login 14-16px, pills 20px. Shadows PROMINENT: login `0 20px 60px rgba(0,0,0,0.18)`, modals `0 24px 70px rgba(0,0,0,0.35)`. Font: Apple system stack. Denser half-px scale: 10.5/11/11.5/12/12.5/13/13.5/19. Icons: hand-drawn geometric CSS (brief-compliant). Dark toggle: sun/moon icon "Apariencia" in sidebar.
Login: centered card 360px pad 36/32 radius 16 big shadow 1px border. "GF" logo badge 56x56 accent bg white radius13 -> title "Gestor de Facturas de Compra" 17/600 -> subtitle -> PLACEHOLDER-ONLY inputs -> inline red `#d70015` error -> FULL-WIDTH accent "Ingresar" -> footer "Credenciales verificadas contra SQL Server". CURRENT login: h1 "Iniciar sesion", visible `<label>`, no logo/subtitle/footer, right-aligned near-black button, `.banner--error` for 401.
Detalle: header row `<- Volver`, title `{compro} - {numero} - {proveedor}` 19/700, estado pill, then `Guardar avance`+`Validar` TOP-RIGHT. Three full-width indicator banners ABOVE the split (1px border + 10% tint, radius10): duplicado->AMBER `#c93400` + inline "Revise el duplicado" checkbox; proveedor P0000->ACCENT BLUE informational; tipo-de-cambio faltante->RED `#d70015` "Se muestra 0.00". Validar NOT disabled with duplicate; relies on ack checkbox not hard gate. Layout: visor 42% left fixed, form flex:1 right, gap20, visor NOT sticky. CURRENT: 50/50 grid gap24 STICKY visor collapses <1100px. Form: 2-col grid 1fr 1fr gap14, label = 11.5px secondary text ABOVE input. Fields: tipo, numero, proveedor+picker btn, codigo proveedor (read-only), glosa, base imponible, IGV(18%), monto, moneda, fecha emision, TC compra + "Fuente: SBS - fecha X" note, mes contable, dia contable. Asiento block: Cuenta/Debe/Haber grid, uppercase 10.5 header, per-row account picker, right tabular Debe/Haber, Total row, "+ Agregar linea" accent link, CUADRE PILL badge. Historial: "v Historial de correccion" caret toggle collapsed by default.
CURRENT factura-form ONLY renders: estado chip (hardcoded chip--pendiente), a few read-only `<p>`, `.alerta--bloqueante`/`--informativa` blocks, ONLY 2 editable inputs (Proveedor, RUC proveedor) + afectacion-mixta confirm buttons. NO tipo/numero/monto/base/IGV/moneda/fecha/TC/glosa/mes/dia fields, no field-highlight applied, no proveedor picker, no dedicated TC-missing indicator.
Handoff `fmtMonto` uses 3 decimals — contradicts CONVENTIONS.md `DECIMAL(18,2)`. Prototype quirk; 2 decimals wins.

## Concrete gaps (current -> handoff)
1. No accent color token — need `--accent`/`--accent-soft` + `.btn` restyle.
2. "Pendiente" hue: handoff blue tint vs current neutral gray (D3 deliberate).
3. P00000 tone: handoff informational blue vs current blocking amber (D2).
4. No dedicated "tipo de cambio faltante" indicator in factura-form (handoff red banner).
5. Field-highlight for OCR-missing fields NOT applied — `.campo--resaltado` exists but unbound.
6. Tabular alignment: primitives exist; `asiento-lineas` has no tabular table styling yet.
7. Radii too tight (6/8 vs 8/12/16). Shadows too flat.
8. Surface hierarchy shallow — 2 levels vs handoff 4.
9. Borders heavy solid vs handoff translucent hairlines.
10. Dark palette differs (current cool vs handoff warm `#1c1c1e`).
11. Login: missing logo/subtitle/footer; labels visible vs placeholder-only; button not full-width.
12. Detalle: no page header/title/back button; actions bottom not top; indicator banners inside factura-form not above split; visor sticky vs static; 50/50 vs 42/58.
13. Theme toggle `<select>` vs sun/moon icon.
14. Font Segoe-first vs Apple-first; denser half-px scale.

## Constraints
- ADR 0009: signals, NO state library.
- CONVENTIONS.md: TS PascalCase types / camelCase methods+props+locals; accounting domain Spanish, technical English; no accents/ñ in identifiers; money never float, never 3-decimal display.
- Brief semantic-color rule: "un estado = un color consistente; no reusar el mismo color semantico como acento decorativo". Handoff APPEARS TO BREAK ITS OWN RULE: blue = primary action AND estado pendiente AND P00000 info banner. Central tension of #18.
- Brief: avoid generic AI iconography; reserve real alert color for genuine alerts.
- `angular.json` budgets: shared rules in styles.css (global unbudgeted), component css layout-only.
- `contraste.spec.ts` must update in lockstep with any hex change; every pair AA in BOTH themes. Apple `#0071e3` on `#f5f5f7` ≈ 4.0:1 -> FAILS AA for normal text; need darker accent-text token for links/text even if `#0071e3` stays for button fills (white-on-`#0071e3` ≈ 4.7:1 passes).
- #12/#13/#17 functional logic FROZEN — #18 is CSS + template-structure only.

## Approaches
1. **Tokens-only re-skin (Low effort):** rewrite styles.css token values + contraste.spec.ts; component css only where layout diverges. Pros: smallest diff, stays "visual only", low review, easy revert. Cons: no handoff fidelity — no page header, actions stay bottom, banners stay in factura-form, missing fields/TC-missing absent.
2. **Tokens + template restructure of the two screens, NO new data (Medium):** #1 plus rebuild `login-page.html`, `detalle-page.html` (page header + top actions; move indicator banners above split), `factura-form` (2-col grid, per-field `.campo--resaltado` for `tieneCamposNoExtraidos`, dedicated TC-missing indicator from data already on `FacturaRespuesta`), `asiento-lineas` (tabular table + cuadre pill — `cuadre` already computed in `detalle-page.ts`). No new API/domain logic.
3. **Full adoption incl missing editable fields + wiring TC/afectacion states (High):** this is #12 FUNCTIONAL work not visual; violates why #18 was split from #12. NOT recommended.

## Recommendation: Approach 2, tightly scoped
Deliver: (1) styles.css token refresh (accent family + separate accessible accent-text token, warm dark palette, 8/12/16 radii, elevated shadows, 4-level surfaces, denser type scale) + contraste.spec.ts green both themes; (2) login-page + detalle-page + detalle/ui children template/CSS restructure to handoff for ONLY those screens; (3) per-field highlight + dedicated TC indicator using data already present on `FacturaRespuesta`/`AsientoRespuesta` — NO new endpoint/domain logic; (4) explicit resolution of semantic-color tension in proposal.
Exclude: bandeja/inbox, configuracion, panel de errores, registro de compra, sidebar nav, any new editable field or gate wiring. Token ripple into excluded screens acceptable — call out, don't chase.

## Open Questions for sdd-propose
1. Semantic-color tension: (a) follow handoff literally; (b) keep pendiente neutral (D3), blue only for actions + P00000 info; (c) blue only for actions, distinct hues for pendiente and P00000. USER DECISION.
2. P00000 / duplicate severity: handoff informational + ack checkbox (non-blocking) vs brief-3/current hard gate on Validar.
3. Confirm #18 does NOT add missing editable factura-form fields (base/IGV/monto/tipo/moneda/fecha/glosa/mes/dia) — stays #12 follow-up.
4. Adopt handoff top header (back, title, top-right Guardar/Validar)? Displaces existing "Fecha de corte contable" input which has no handoff equivalent — where does it go?
5. Theme control: keep `<select>` or handoff sun/moon toggle?
6. Font: Apple-first per handoff or keep Segoe-first (Windows desktop)? Half-px sizes — adopt or round?
7. Accent hex vs AA: confirm `#0071e3` for button fills, separate darker token for links/accent text; pick dark-theme accent-text value.
8. Money display: confirm 2-decimal everywhere.

## Affected Areas
- `SmartNet/SmartNetWeb/src/styles.css` — token refresh
- `SmartNet/SmartNetWeb/src/app/shared/contraste.spec.ts` — update every token-pair assertion
- `SmartNet/SmartNetWeb/src/app/shared/tema.service.ts` — only if toggle control changes
- `SmartNet/SmartNetWeb/src/app/app.html` + `app.css` — header, theme control, marca/logo
- `SmartNet/SmartNetWeb/src/app/login/feature/login-page/*` — template + css restructure
- `SmartNet/SmartNetWeb/src/app/detalle/feature/detalle-page/*` — page header, action placement, split ratio
- `SmartNet/SmartNetWeb/src/app/detalle/ui/factura-form/*` — 2-col grid, per-field highlight, TC indicator
- `SmartNet/SmartNetWeb/src/app/detalle/ui/asiento-lineas/*` — tabular table, cuadre pill
- `SmartNet/SmartNetWeb/src/app/detalle/ui/{visor-documento,conflicto-banner,historial-correccion}/*` — token/radii/shadow follow-through
- `*.spec.ts` for login-page/detalle-page/factura-form/asiento-lineas — assertions for new structure
- Out of scope (ripple only): `inbox/*`, `configuracion/*`

## Prior SDD artifacts
- `openspec/changes/archive/2026-08-23-api-detalle-validacion-facturas-12/` — #12 functional (archived)
- `openspec/changes/archive/2026-08-24-diseno-visual-spa-item-12/` — first visual pass; specs spa-design-tokens, spa-theme-toggle, spa-visual-login, spa-visual-detalle-validacion, pantalla-detalle-validacion. #18 revises these.
- `openspec/changes/archive/2026-08-24-item-13-bandeja-incidencias/` — bandeja/inbox screens (out of #18 scope)

## Ready for Proposal: YES
Q1, Q2, Q4 are genuine design forks that change the spec; the rest are confirmations.
