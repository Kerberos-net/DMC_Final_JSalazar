# Delta for SPA Visual Bandeja

## MODIFIED Requirements

### Requirement: inbox-list table with derived Estado chip column

The system MUST render `inbox-list` as the handoff §2 compras table: one row per
`BandejaItem` with columns for proveedor display name, comprobante tipo
(client-side display name mapped from the API code: 01 -> "Factura",
03 -> "Boleta", 07 -> "Nota de crédito", other codes -> the raw code), número,
monto with moneda (rendered with a component-scoped tabular-figures treatment,
NOT the global `.tabular-nums` primitive), fecha de emisión (component-scoped
tabular-figures, left-aligned), and an uppercase small-caps header row. Rows with
`origen === 'INCIDENCIA'` MUST render "—" for every factura-only cell (proveedor,
tipo, número, monto, moneda, fecha de emisión).

The system MUST keep one ADDITIVE derived "Estado" chip column computed per row
by `chipEstadoDe` in `inbox-list.ts`, FIRST MATCH WINS, in this precedence:

1. `estadoConsumo === 'DESCARTADO'` → `.chip--descartada` "Descartada"
   (unconditional — wins even when the row still carries error history)
2. else `errores.length > 0` → `.chip--error` "Error"
3. else `indicadores !== null && (esProveedorGenerico || posibleDuplicado)` →
   `.chip--alerta` "Alerta" (null-safe: `origen === 'INCIDENCIA'` rows have
   `indicadores: null`)
4. else `estadoConsumo === 'PROMOVIDO'` → `.chip--validada` "Validada"
5. else `estadoConsumo === 'PENDIENTE'` → `.chip--pendiente` "Pendiente"

`DESCARTADO` ranks FIRST, not last. The derived Estado chip is presentation-only
and is NOT a change to #13's "indicators → chips" logic. The existing
per-indicator `chipsDe()` list column MUST remain unchanged.

(Previously: the row used ONLY data already on `BandejaItem` and rendered a
minimal set of cells plus the derived Estado chip; rich compras columns were
deferred to #21. #21 now brings proveedor/tipo/número/monto/moneda/fecha into
the row and adds the client-side comprobante display-name map, while the
`chipEstadoDe` precedence and the frozen `chipsDe()` column are unchanged.)

#### Scenario: FACTURA row renders the compras columns

- GIVEN a `BandejaItem` with `origen === 'FACTURA'` and enriched fields present
- WHEN the row renders
- THEN it shows proveedor name, mapped comprobante tipo, número, monto+moneda in
  tabular figures, fecha de emisión in tabular figures, and the derived Estado chip

#### Scenario: INCIDENCIA row shows em dashes for factura-only cells

- GIVEN a `BandejaItem` with `origen === 'INCIDENCIA'` (enriched fields null)
- WHEN the row renders
- THEN proveedor, tipo, número, monto, moneda and fecha de emisión cells each
  render "—", and the derived Estado chip still renders

#### Scenario: Comprobante code is mapped client-side

- GIVEN a row whose API `tipoComprobante` is "01"
- WHEN the tipo cell renders
- THEN it displays "Factura" (the API response still carries the code "01")

#### Scenario: Discarded row with error history shows Descartada

- GIVEN a `BandejaItem` with `estadoConsumo === 'DESCARTADO'` and `errores.length > 0`
- WHEN the row renders
- THEN the Estado column shows a `.chip--descartada` labeled "Descartada"

#### Scenario: Non-discarded row with errors shows Error chip

- GIVEN a `BandejaItem` with `estadoConsumo !== 'DESCARTADO'` and `errores.length > 0`
- WHEN the row renders
- THEN the Estado column shows a `.chip--error` labeled "Error" and the
  `chipsDe()` indicator column is unchanged

#### Scenario: Promoted row without errors or alert indicators

- GIVEN a `BandejaItem` with `estadoConsumo === 'PROMOVIDO'`, no errores, and no
  `esProveedorGenerico`/`posibleDuplicado`
- WHEN the row renders
- THEN the Estado column shows a `.chip--validada` labeled "Validada"

#### Scenario: Date cell uses component-scoped tabular figures

- GIVEN any inbox-list row
- WHEN the fecha de emisión cell renders
- THEN it carries a component-scoped tabular-figures class, not the global
  `.tabular-nums` primitive

## ADDED Requirements

### Requirement: inbox-page global summary cards

The system MUST render four summary cards in `inbox-page` — "Pendientes",
"Validadas", "Con error", "Alertas" — fed from the bandeja estado aggregate
(`resumen`). The cards MUST show GLOBAL totals: independent of the active filter
signals and of the current page. The cards MUST be display-only — they MUST NOT
act as filter shortcuts and MUST NOT mutate any filter signal on interaction.
Card values map to aggregate buckets: Pendientes←`pendientes`,
Validadas←`validadas`, Con error←`conError`, Alertas←`alertas`. Card CSS MUST be
token-driven, layout-only, and within the `anyComponentStyle` budget.

#### Scenario: Cards show global totals regardless of filters

- GIVEN the aggregate reports pendientes=12, validadas=40, conError=3, alertas=5
- WHEN the user applies an `estado=PENDIENTE` filter and moves to page 2
- THEN the four cards still show 12 / 40 / 3 / 5

#### Scenario: Cards are not filter shortcuts

- GIVEN the summary cards are rendered
- WHEN the user activates the "Con error" card
- THEN no filter signal changes and the list query is not re-issued

#### Scenario: Validadas card is non-zero when promoted rows exist

- GIVEN promoted facturas exist that the default list view excludes
- WHEN `inbox-page` renders
- THEN the "Validadas" card shows the promoted count, not 0

## Notes for archive (project rule 1 — deliberate, documented)

When this delta is archived into `openspec/specs/spa-visual-bandeja/spec.md`,
apply these two edits to the non-requirement prose as well:

1. **Purpose** — change the frozen-behavior clause. The #13 bandeja query and
   `inbox.service.ts` are DELIBERATELY UNFROZEN by #21 for the enriched
   comprobante fields and the estado aggregate (`resumen`). Still frozen:
   filter semantics, pagination, the `chipsDe()` per-indicator list column, and
   the reprocesar 5-minute window.
2. **Out of Scope** — REMOVE the two bullets "Summary/counter cards
   (Pendientes/Validadas/Con error/Alertas) — BACKLOG #21" and "Rich data
   columns (proveedor display name, monto, moneda, número, tipo, fecha de
   emisión, ...) — BACKLOG #21" for the fields delivered here (proveedor name,
   monto, moneda, número, tipo, fecha de emisión). KEEP glosa, tipo de cambio,
   base imponible, and IGV out of scope (#19 / future). Narrow the last bullet
   so it no longer forbids changes to "the #13 bandeja query" or
   "`inbox.service.ts`", while still forbidding changes to filter semantics,
   pagination, `chipsDe()` per-indicator logic, and the reprocesar 5-minute window.
