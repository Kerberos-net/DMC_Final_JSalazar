# Delta for api-facturas (BACKLOG #19)

Makes `base imponible`, `IGV` and `glosa` editable while the factura is
`PENDIENTE_VALIDACION`, recomputes `PosibleDuplicado` on identity-triple change,
projects the per-field OCR list, populates the scalar `BasePEN`/`IgvPEN`/`NetoPEN`
projection, and narrows the existing missing-tipo-de-cambio conflict so it stops
blocking an NC `07` con referencia interna. Faithful to REGLAS §5–§10. No asiento
línea is generated or modified (see BACKLOG #24 for wiring `ComposicionDeAsiento.Componer`).

> **State model note.** `fact.Factura` has NO `BORRADOR` state —
> `CK_Factura_Estado` allows only `PENDIENTE_VALIDACION | VALIDADA | DESCARTADA`.
> `BORRADOR` / `CONFIRMADO` are `AsientoContable` states. This delta introduces no
> new state and no CHECK-constraint change. "Editable before validation" means
> `Factura.Estado == PENDIENTE_VALIDACION` (`FacturaPersistida.PendienteValidacion`).

## MODIFIED Requirements

### Requirement: `CorreccionFacturaRequest` accepts `tipoComprobante`, `numero`, and the contable fields

(Previously: base imponible / IGV / glosa were explicitly excluded from the write contract.)

`CorreccionFacturaRequest` (and the `CorreccionFactura` core type and
`ServicioDeFacturas.PatchAsync`) MUST additively accept optional
`tipoComprobante`, `numero`, `baseImponible`, `igv`, and `glosa`. When present on
a `PATCH /api/facturas/{id}` body they MUST be applied to `fact.Factura` under the
same `If-Match` optimistic-concurrency and per-field `AuditoriaCorreccion` rules
as existing editable fields.

- `baseImponible` and `igv` MUST write only the ORIGINAL-currency values
  (`TotalOrig = baseImponible + igv`, `IgvOrig = igv`). They MUST NOT write the
  PEN projection: per REGLAS §6 `BasePEN` stays derived and no adjustment line is
  created.
- `glosa` is free text, nullable, maximum 250 characters.
- `baseImponible` / `igv` / `glosa` edits MUST be applied only when the factura's
  `Estado` is `PENDIENTE_VALIDACION` (`FacturaPersistida.PendienteValidacion`). A
  request carrying any of them for a `VALIDADA` factura MUST be rejected `422`
  with zero rows updated (REGLAS §9 — `reabrir` returns the factura to
  `PENDIENTE_VALIDACION` first). A `DESCARTADA` factura is not editable.
- Pure domain validation in `ValidacionDeCorreccion` MUST reject with `422`:
  `baseImponible` < 0; `IgvOrig` > `TotalOrig`; a non-zero `igv` for a boleta
  (`03`) or a non-NC operación NO gravada (`EXONERADA` / `INAFECTA`) — REGLAS §5,
  that IGV belongs in cost, not a `401111` line. This IGV guard MUST NOT apply to
  a nota de crédito `07` con **referencia interna**: such an NC mirrors the
  rectified document and follows its own REGLAS §6 inheritance rule, so it MAY
  carry a non-zero `igv` even when its own structure would otherwise be a
  two-line boleta mirror.
- Domain validation MUST still reject an empty/blank `numero` and a
  `tipoComprobante` outside the accepted comprobante-type set.
- `tipoComprobante` / `numero` keep their current post-validation audited
  `Correccion` behavior; only the new contable fields carry the stricter
  `PENDIENTE_VALIDACION`-only rule.

No existing request field changes name, type, or meaning.

#### Scenario: PATCH updates base, IGV and glosa on a pre-validation factura
- GIVEN a factura with `Estado = PENDIENTE_VALIDACION` and a valid `ETag`
- WHEN `PATCH` is sent with `If-Match` and `baseImponible`, `igv`, `glosa`
- THEN `TotalOrig`, `IgvOrig` and `Glosa` persist, `Version` advances, the new
  `ETag` is returned, and one `AuditoriaCorreccion` row is written per changed field

#### Scenario: Contable edit on a validated factura is rejected
- GIVEN a factura with `Estado = VALIDADA`
- WHEN `PATCH` carries `baseImponible`, `igv` or `glosa`
- THEN the response is `422 Unprocessable Content` (`application/problem+json`) and zero rows update

#### Scenario: Negative base or IGV over total is rejected
- GIVEN a `PATCH` body with `baseImponible` < 0, or `igv` making `IgvOrig` > `TotalOrig`
- WHEN the request is processed
- THEN it is rejected `422 Unprocessable Content` and zero rows update

#### Scenario: Non-zero IGV on a boleta or non-gravada factura is rejected
- GIVEN a factura of tipo `03`, or a non-NC factura with `Afectacion` `EXONERADA`/`INAFECTA`
- WHEN `PATCH` sends a non-zero `igv`
- THEN it is rejected `422` (REGLAS §5 — IGV goes to cost), zero rows update

#### Scenario: Non-zero IGV on an NC 07 con referencia interna is accepted
- GIVEN a nota de crédito `07` with `FacturaReferenciaId` set and `EsReferenciaExterna = false`,
  whose rectified document is a boleta or non-gravada operation
- WHEN `PATCH` sends a non-zero `igv` while `Estado = PENDIENTE_VALIDACION`
- THEN the IGV guard does NOT fire; `IgvOrig` is written and the edit is accepted
  (REGLAS §6 — the NC follows its own inheritance rule)

#### Scenario: PATCH updates tipoComprobante and numero
- GIVEN a factura with a valid current `ETag`
- WHEN `PATCH` is sent with `If-Match` and a body carrying `tipoComprobante` and `numero`
- THEN both columns update, `Version` advances, and the new `ETag` is returned

#### Scenario: Correction on a validated factura writes audit
- GIVEN a `PATCH` changes `numero` on a factura whose asiento is `CONFIRMADO`
- WHEN the edit succeeds
- THEN a `fact.AuditoriaCorreccion` row is written with `EntidadTipo=FACTURA`, `Accion=CORRECCION`

#### Scenario: Blank numero is rejected
- GIVEN a `PATCH` body with `numero` empty or whitespace
- WHEN the request is processed
- THEN it is rejected `422 Unprocessable Content` and zero rows update

#### Scenario: Unknown tipoComprobante is rejected
- GIVEN a `PATCH` body with `tipoComprobante` not in the accepted comprobante-type set
- WHEN the request is processed
- THEN it is rejected `422 Unprocessable Content` and zero rows update

#### Scenario: Omitting the new fields is a no-op
- GIVEN a `PATCH` body without `tipoComprobante`, `numero`, `baseImponible`, `igv`, or `glosa`
- WHEN the request is processed
- THEN those columns are left unchanged and behavior is identical to before this delta

### Requirement: `FacturaRespuesta` projects existing duplicate/afectación/OCR indicators

(Previously: only the invoice-wide `TieneCamposNoExtraidos` boolean and no `Glosa` were projected.)

`FacturaRespuesta` MUST additively expose `EsProveedorGenerico`,
`PosibleDuplicado`, `TieneCamposNoExtraidos`, `AfectacionMixta`, plus
`CamposNoExtraidos` (a `string[]` of the not-extracted field names, drawn from the
canonical set: `tipoComprobante`, `numero`, `ruc`, `nombreProveedor`, `total`,
`igv`, `moneda`, `fechaEmision`) and `Glosa` (nullable string). `CamposNoExtraidos`
MUST be consistent with `TieneCamposNoExtraidos` (non-empty iff the boolean is
true). No existing `FacturaRespuesta` field changes name, type, or meaning.

#### Scenario: Detail response includes the indicator fields
- GIVEN a factura persisted with `PosibleDuplicado=true` and `AfectacionMixta=null`
- WHEN its `FacturaRespuesta` is returned by the factura detail endpoint
- THEN the response includes `PosibleDuplicado: true` and `AfectacionMixta: null`
  alongside the existing fields

#### Scenario: Per-field not-extracted list is projected
- GIVEN a factura promoted with `numero` and `total` not extracted by the worker
- WHEN its `FacturaRespuesta` is returned
- THEN `CamposNoExtraidos` contains exactly `["numero","total"]` and
  `TieneCamposNoExtraidos` is `true`

#### Scenario: Fully extracted factura
- GIVEN a factura with every canonical field extracted
- WHEN its `FacturaRespuesta` is returned
- THEN `CamposNoExtraidos` is an empty array and `TieneCamposNoExtraidos` is `false`

#### Scenario: Glosa is projected
- GIVEN a factura with `Glosa` set to a non-null value
- WHEN its `FacturaRespuesta` is returned
- THEN the response includes that `Glosa` value; a factura with no glosa returns `null`

#### Scenario: Parity with the bandeja projection
- GIVEN a factura row with a given set of indicator column values
- WHEN read via `GET /api/bandeja` and via the factura detail endpoint
- THEN `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`, and
  `AfectacionMixta` resolve to the same values in both responses

#### Scenario: Existing consumers are unaffected
- GIVEN a client reading only the fields that existed before this change
- WHEN it parses the updated response
- THEN every previously existing field keeps its previous name, type, and semantics

## ADDED Requirements

### Requirement: `PosibleDuplicado` is recomputed when the identity triple changes

When a `PATCH /api/facturas/{id}` changes any of `ruc` (RucProveedor),
`tipoComprobante`, or `numero`, `ServicioDeFacturas.PatchAsync` MUST recompute
`fact.Factura.PosibleDuplicado` synchronously inside the same transaction, using
the REGLAS §8 identity rule `(RUC, tipo, número)` against `IX_Factura_Identidad`
(excluding the factura itself and any `DESCARTADA` factura). The recomputed value
MUST be persisted and reflected in the `FacturaRespuesta` returned by that same
`PATCH`.

#### Scenario: Correcting the número clears a stale duplicate flag
- GIVEN a factura with `PosibleDuplicado=true` caused by a wrong `numero`
- WHEN `PATCH` sets `numero` to a value with no other matching `(RUC, tipo, número)` row
- THEN `PosibleDuplicado` becomes `false` in the same transaction and the `PATCH`
  response carries `PosibleDuplicado: false`

#### Scenario: Editing into a collision sets the flag
- GIVEN a factura with `PosibleDuplicado=false`
- WHEN `PATCH` changes `numero`/`ruc`/`tipoComprobante` to match an existing non-discarded factura
- THEN `PosibleDuplicado` becomes `true` and is returned in the response

#### Scenario: Editing an unrelated field does not touch the flag
- GIVEN a factura with a known `PosibleDuplicado` value
- WHEN `PATCH` changes only `glosa`, `baseImponible`, or `igv`
- THEN `PosibleDuplicado` is left unchanged

### Requirement: Scalar `BasePEN`/`IgvPEN`/`NetoPEN` are recomputed on a contable edit

When a `PATCH` changes `baseImponible`, `igv`, or `moneda`,
`ServicioDeFacturas.PatchAsync` MUST recompute the scalar
`BasePEN`/`IgvPEN`/`NetoPEN` projection as a pure REGLAS §5/§6 derivation of that
invoice's own original values, with `conv(x) = round(x × TCventa, 2)` and
`TCventa = 1` for a PEN factura:

- **Gravada** (`01` con `Afectacion = GRAVADA`): `IgvPEN = conv(IgvOrig)`,
  `NetoPEN = conv(TotalOrig)`, `BasePEN = NetoPEN − IgvPEN` (derived — REGLAS §6,
  the base absorbs the rounding, never the IGV).
- **Boleta `03` / `EXONERADA` / `INAFECTA`**: `IgvPEN = 0`,
  `NetoPEN = conv(TotalOrig)`, `BasePEN = NetoPEN`.
- In every case `NetoPEN = BasePEN + IgvPEN` holds exactly (design-confirmed
  against the golden fixtures: `3789.50 + 682.11 = 4471.61`; non-gravada
  `118 + 0 = 118`).

It MUST NOT create, regenerate, or touch any `AsientoContable` línea, and MUST NOT
create an adjustment line (REGLAS §6 — no tolerance line).

#### Scenario: Editing base and IGV on a pre-validation gravada factura
- GIVEN a `PENDIENTE_VALIDACION` USD gravada factura with an available venta rate `TCventa`
- WHEN `PATCH` changes `baseImponible` and `igv`
- THEN `IgvPEN = conv(IgvOrig)`, `NetoPEN = conv(TotalOrig)`,
  `BasePEN = NetoPEN − IgvPEN`, and `NetoPEN = BasePEN + IgvPEN` holds exactly

#### Scenario: Recompute for a boleta folds nothing into IGV
- GIVEN a `PENDIENTE_VALIDACION` boleta `03` (PEN)
- WHEN `PATCH` changes `baseImponible`
- THEN `IgvPEN = 0`, `NetoPEN = conv(TotalOrig)`, `BasePEN = NetoPEN`

#### Scenario: Asiento líneas are never regenerated
- GIVEN an asiento with hand-built líneas
- WHEN a contable `PATCH` succeeds
- THEN no línea is added, removed, or modified (BACKLOG #24 owns wiring `Componer`)

#### Scenario: Populating BasePEN can newly fail a §7 invariant at validar
- GIVEN a factura whose `AsientoContable` líneas were hand-built against the old base,
  and a `PATCH` has edited `baseImponible` so `BasePEN` no longer equals the sum of
  the principal cargo líneas
- WHEN `POST /api/facturas/{id}/validar` is called
- THEN `validar` MAY now reject with `422` on the REGLAS §7 "cargos `6x`/`1x` igualan
  base imponible" invariant that was previously vacuous while `BasePEN` was unpopulated
- AND this behavior change is accepted by the product owner (re-align the líneas, then
  re-validate)

### Requirement: The missing-tipo-de-cambio conflict is narrowed to exclude NC `07` con referencia interna

The existing `HechosDeConflicto.SinTipoCambio` fact
(`SqlUnidadDeTrabajo.EvaluarHechosDeConflicto`) already blocks `abrir`
(`AbrirAsync`) and `validar` (= confirm; `ValidarInternoAsync` sets `CONFIRMADO`)
with `409 Conflict` when `moneda != MonedaLocal` and no vigente tipo de cambio
exists for the fecha de emisión, and already exempts a local-currency (PEN)
factura. This delta MUST narrow that predicate so it ALSO does not fire for a
nota de crédito `07` con **referencia interna** (`FacturaReferenciaId` populated
and `EsReferenciaExterna = false`): per REGLAS §6 such an NC inherits the tipo de
cambio frozen on the referenced factura's asiento and never performs its own
lookup. An NC `07` con **referencia externa** still applies the general rule.
No new guard, response shape, or endpoint behavior is introduced — only the
condition under which the existing `409` is raised.

> This branch is dormant today: `FacturaReferenciaId` is not populated until
> BACKLOG #10/#11. The narrowing is specified now so it is correct once NC
> referencing is wired.

#### Scenario: Foreign-currency factura with no rate still cannot be validated
- GIVEN a USD factura whose fecha de emisión has no vigente `fact.TipoCambio` row
- WHEN `POST /api/facturas/{id}/validar` (or `.../abrir`) is called
- THEN the response is `409 Conflict` naming the missing tipo de cambio (unchanged)

#### Scenario: PEN factura is unaffected
- GIVEN a PEN factura with no tipo de cambio row
- WHEN `validar` is called and all other invariants pass
- THEN `SinTipoCambio` does not fire (unchanged)

#### Scenario: NC 07 con referencia interna is no longer blocked
- GIVEN a nota de crédito `07` with `FacturaReferenciaId` set, `EsReferenciaExterna = false`,
  in foreign currency, whose fecha de emisión has no vigente tipo de cambio
- WHEN `validar` (or `abrir`) is called
- THEN `SinTipoCambio` does NOT fire; the NC inherits the referenced factura's frozen
  rate (REGLAS §6)

#### Scenario: NC 07 con referencia externa still applies the general rule
- GIVEN a nota de crédito `07` with `EsReferenciaExterna = true`, foreign currency,
  fecha de emisión with no vigente tipo de cambio
- WHEN `validar` is called
- THEN the response is `409 Conflict` naming the missing tipo de cambio

#### Scenario: The narrowing does not affect PATCH
- GIVEN any factura with no applicable tipo de cambio
- WHEN `PATCH /api/facturas/{id}` ("Guardar avance") is called with a valid body
- THEN the edit is applied normally; `SinTipoCambio` is not evaluated on `PATCH` (unchanged)

## Resolved decisions

- **Boleta / non-NC no-gravada IGV** — a non-zero `igv` is a HARD `422` reject
  (owner-confirmed). Encoded as a scenario above.
- **NC `07` con referencia interna** — NOT subject to the `422` IGV guard; it
  follows its own REGLAS §6 inheritance rule and MAY carry a non-zero `igv`
  (owner-confirmed). Encoded as a scenario above.

## Open Questions (affecting requirements)

1. **Scalar recompute trigger set** — confirmed as `baseImponible` / `igv` /
   `moneda` (proposal default). Design must confirm no other field triggers it.
2. **`AuditoriaCorreccion` granularity** — spec assumes one row per changed field
   for base/IGV/glosa (proposal default).
