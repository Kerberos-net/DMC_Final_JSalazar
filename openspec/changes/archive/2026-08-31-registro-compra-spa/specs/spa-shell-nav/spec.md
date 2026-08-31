# Delta for spa-shell-nav

This delta is INTENTIONAL. Memory `shell-nav-canvas-replica` warns reviewers not
to "restore" earlier sidebar rules (fewer routed destinations). BACKLOG #23
deliberately activates one previously-inert entry (`Registro de compra`),
following the same inert→routed pattern BACKLOG #22 used for `Proveedores` and
`Plan contable`. Do not revert the sidebar to a smaller routed list on review.
The eight destinations and their order are unchanged; only `Registro de compra`
moves from inert to routed. Its hand-built glyph already exists.

## MODIFIED Requirements

### Requirement: Sidebar mirrors the handoff navigation

The sidebar MUST render the navigation in two groups. Primary group, in order:
`Bandeja principal`, `Registro de compra`, `Proveedores`, `Plan contable`,
`Tipo de cambio`. Utility group, in order: `Errores y notificaciones`,
`Sincronización`, `Configuración` — eight destinations total.

The following six destinations resolve to a route and MUST render as `<a>`
links: `Bandeja principal` (`/bandeja`), `Registro de compra`
(`/registro-compra`), `Proveedores` (`/catalogos/proveedores`), `Plan contable`
(`/catalogos/plan-contable`), `Tipo de cambio` (`/catalogos/tipo-cambio`), and
`Configuración` (`/configuracion`).

The remaining two destinations — `Errores y notificaciones` and `Sincronización`
— MUST render as inert entries: not a link, `aria-disabled="true"`, `title`
"Disponible próximamente" — so the navigation matches the canvas without
offering a dead click.

(Previously: only five destinations were links — `Registro de compra` was one of
three inert entries; BACKLOG #23 routes it to `/registro-compra`, leaving two
inert entries.)

#### Scenario: Handoff destinations appear in order

- GIVEN the SPA is loaded with a valid session
- WHEN `ShellLayout` renders the sidebar
- THEN the eight destinations appear in the order above, with a divider between
  the primary and utility groups

#### Scenario: Routed destinations are links

- GIVEN the sidebar renders
- WHEN its entries are inspected
- THEN `Bandeja principal`, `Registro de compra`, `Proveedores`, `Plan contable`,
  `Tipo de cambio`, and `Configuración` are `<a>` links to their routes, and
  `Errores y notificaciones` and `Sincronización` are `aria-disabled` non-link
  entries

#### Scenario: Active destination is indicated

- GIVEN the current URL matches a routed destination (e.g. `/registro-compra`)
- WHEN the sidebar renders
- THEN that entry carries an active/selected visual state (solid accent fill) and
  the others do not

#### Scenario: sidebar.spec.ts asserts the new exact list

- GIVEN `sidebar.spec.ts`
- WHEN it runs
- THEN it asserts the exact ordered eight-entry list above, that exactly the six
  routed destinations (`Bandeja principal`, `Registro de compra`, `Proveedores`,
  `Plan contable`, `Tipo de cambio`, `Configuración`) are `<a>` links, that
  `Errores y notificaciones` and `Sincronización` are the only inert entries,
  and that eight hand-built glyphs render
