# Delta for spa-shell-nav

This delta is INTENTIONAL. Memory `shell-nav-canvas-replica` warns reviewers not
to "restore" earlier sidebar rules (two routed destinations only). BACKLOG #22
deliberately activates two previously-inert entries and adds one new entry. Do
not revert the sidebar to a smaller list on review.

Note: the canvas has no Tipo de cambio entry; adding it is an owner decision —
do not restore 7 entries.

## MODIFIED Requirements

### Requirement: Sidebar mirrors the handoff navigation

The sidebar MUST render the navigation in two groups. Primary group, in order:
`Bandeja principal`, `Registro de compra`, `Proveedores`, `Plan contable`,
`Tipo de cambio`. Utility group, in order: `Errores y notificaciones`,
`Sincronización`, `Configuración` — eight destinations total.

The following five destinations resolve to a route and MUST render as `<a>`
links: `Bandeja principal` (`/bandeja`), `Proveedores` (`/catalogos/proveedores`),
`Plan contable` (`/catalogos/plan-contable`), `Tipo de cambio`
(`/catalogos/tipo-cambio`), and `Configuración` (`/configuracion`).

The remaining three destinations — `Registro de compra`, `Errores y
notificaciones`, `Sincronización` — MUST render as inert entries: not a link,
`aria-disabled="true"`, `title` "Disponible próximamente" — so the navigation
matches the canvas without offering a dead click.

(Previously: seven destinations; only `Bandeja principal` and `Configuración`
were links and the other five, including `Proveedores` and `Plan contable`, were
inert. `Tipo de cambio` did not exist.)

#### Scenario: Handoff destinations appear in order

- GIVEN the SPA is loaded with a valid session
- WHEN `ShellLayout` renders the sidebar
- THEN the eight destinations appear in the order above, with a divider between
  the primary and utility groups

#### Scenario: Routed destinations are links

- GIVEN the sidebar renders
- WHEN its entries are inspected
- THEN `Bandeja principal`, `Proveedores`, `Plan contable`, `Tipo de cambio`, and
  `Configuración` are `<a>` links to their routes, and `Registro de compra`,
  `Errores y notificaciones`, and `Sincronización` are `aria-disabled` non-link entries

#### Scenario: Active destination is indicated

- GIVEN the current URL matches a routed destination (e.g. `/catalogos/proveedores`)
- WHEN the sidebar renders
- THEN that entry carries an active/selected visual state (solid accent fill) and
  the others do not

#### Scenario: sidebar.spec.ts asserts the new exact list

- GIVEN `sidebar.spec.ts`
- WHEN it runs
- THEN it asserts the exact ordered eight-entry list above, that exactly the five
  routed destinations are `<a>` links, and that eight hand-built glyphs render

### Requirement: Shell CSS stays layout-only and within budget

The `ShellLayout` and `Sidebar` stylesheets MUST contain layout/composition rules
only, no color/typography/hue literals (every color via `var(--...)`), and MUST
stay within the `angular.json` `anyComponentStyle` hard cap (8kB error). The
per-file warning threshold is 6kB. Adding the eighth hand-built nav glyph
(`Tipo de cambio`) MUST reuse the existing glyph/token pattern and MUST keep
`Sidebar` under the 6kB warning; if the eighth glyph would breach 6kB, the glyph
CSS MUST be refactored to shared rules rather than raising the budget again.

(Previously: text referred to "seven hand-built nav glyphs" and `Sidebar` ~5.3kB.)

#### Scenario: Shell stylesheet has no literals and fits the budget

- GIVEN the `ShellLayout` and `Sidebar` stylesheets after the eighth glyph is added
- WHEN their declarations are inspected
- THEN every color references a token and the production build reports no
  `anyComponentStyle` budget error, and `Sidebar` stays under the 6kB warning
