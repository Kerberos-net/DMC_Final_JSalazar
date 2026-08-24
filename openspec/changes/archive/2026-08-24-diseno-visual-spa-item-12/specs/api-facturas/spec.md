# Delta for api-facturas

## ADDED Requirements

### Requirement: `FacturaRespuesta` projects existing duplicate/afectación/OCR indicators

`FacturaRespuesta` MUST additively expose `EsProveedorGenerico`,
`PosibleDuplicado`, `TieneCamposNoExtraidos`, and `AfectacionMixta` — the same
`fact.Factura` columns already read by `GET /api/bandeja`
(`SqlBandejaRepository.ListarAsync`). No existing `FacturaRespuesta` field
MUST change name, type, or meaning as part of this addition.

#### Scenario: Detail response includes the indicator fields

- GIVEN a factura persisted with `PosibleDuplicado=true` and
  `AfectacionMixta=null`
- WHEN its `FacturaRespuesta` is returned by the factura detail endpoint
- THEN the response includes `PosibleDuplicado: true` and
  `AfectacionMixta: null` alongside the existing fields

#### Scenario: Parity with the bandeja projection

- GIVEN a factura row with a given set of indicator column values
- WHEN the same factura is read via `GET /api/bandeja` and via the factura
  detail endpoint
- THEN `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`,
  and `AfectacionMixta` resolve to the same values in both responses

#### Scenario: Existing consumers are unaffected

- GIVEN a client that only reads the `FacturaRespuesta` fields that existed
  before this change
- WHEN it parses the updated response
- THEN every previously existing field is present with its previous name,
  type, and value semantics unchanged
</content>
