# Delta for pantalla-detalle-validacion

Change: `cablear-composicion-asiento` (BACKLOG #24). The detalle screen can now assume an
asiento is always present (seeded at promotion), renders the asiento líneas, offers a
"recomponer asiento" action on a `BORRADOR`, and shows a cabecera↔detalle descuadre marker
when the líneas no longer match the header projection. Manual line editing (#12) and #19's
editable base/IGV/glosa are unchanged.

## ADDED Requirements

### Requirement: The asiento is assumed present and its base/IGV drive the form

Because promotion seeds the `BORRADOR` asiento, `factura-form` MUST populate `base imponible`
and `IGV` from the asiento's `BasePEN`/`IgvPEN` projection on load, and the asiento líneas
section MUST render. When (exceptionally) no asiento exists — a foreign-currency factura
promoted with no tipo de cambio — the screen MUST show a "generar asiento" affordance that
calls `POST /api/facturas/{id}/abrir` once a rate is available, instead of blank amounts with
no recourse.

#### Scenario: Detalle load shows base/IGV from the seeded asiento — [new]
- GIVEN a factura promoted with a seeded asiento
- WHEN the detalle screen loads
- THEN `base imponible` and `IGV` are populated from the asiento projection and the líneas
  section lists the PRINCIPAL + DESTINO líneas
- (test: SPA unit)

#### Scenario: Factura with no asiento shows "generar asiento" — [new]
- GIVEN a foreign-currency factura promoted without an asiento
- WHEN the detalle screen loads
- THEN a "generar asiento" action is shown; invoking it (once a tipo de cambio exists) calls
  `abrir` and the screen refetches
- (test: SPA unit)

### Requirement: "Recomponer asiento" action on a BORRADOR asiento

The screen MUST offer a "recomponer asiento" action, visible only while the asiento is
`BORRADOR`, that calls `POST /api/facturas/{id}/recomponer`, then refetches the factura and
asiento. A confirmation dialog MUST warn that manual line edits will be replaced before the
request is sent. The action MUST NOT be shown for a `CONFIRMADO` asiento.

#### Scenario: Recomponer regenerates the líneas from the screen — [new]
- GIVEN a `BORRADOR` asiento with manually split líneas
- WHEN the user triggers "recomponer asiento" and confirms the warning
- THEN `recomponer` is called, and after the refetch the líneas show the fresh engine seed
- (test: SPA unit)

#### Scenario: Recomponer hidden on a CONFIRMADO asiento — [new]
- GIVEN a factura whose asiento is `CONFIRMADO`
- WHEN the detalle screen renders
- THEN the "recomponer asiento" action is not shown
- (test: SPA unit)

### Requirement: Cabecera↔detalle descuadre marker

The screen MUST show a read-only descuadre marker (reusing the read-only marker introduced by
BACKLOG #23) whenever the sum of the PRINCIPAL cargo líneas does not equal the header
`BasePEN` (gravada) / `NetoPEN` (otherwise). The marker MUST explain that `validar` is blocked
until the líneas are re-aligned or the asiento is recompuesto. It MUST clear once the líneas
match the header again.

#### Scenario: Marker appears after a base edit unbalances the líneas — [new]
- GIVEN a seeded asiento, and the user edits `base imponible` so the cargo líneas no longer
  sum to the new `BasePEN`, then selects "Guardar avance"
- WHEN the screen re-renders after the refetch
- THEN the cabecera↔detalle descuadre marker is shown and "Validar" surfaces the §7 `422`
  distinctly (per the existing outcome-distinction requirement)
- (test: SPA unit)

#### Scenario: Marker clears after recomponer — [new]
- GIVEN the descuadre marker is shown
- WHEN the user runs "recomponer asiento"
- THEN after the refetch the líneas match the header and the marker is gone
- (test: SPA unit)

#### Scenario: Manual line editing and #19 editable base/IGV/glosa still work (regression) — [new]
- GIVEN a `PENDIENTE_VALIDACION` factura with a `BORRADOR` asiento
- WHEN the user edits an asiento línea inline (#12) or edits `base imponible` / `IGV` /
  `glosa` (#19) and selects "Guardar avance"
- THEN the edits persist exactly as before this change; only the descuadre marker and
  "recomponer" affordance are added on top
- (test: SPA unit)
