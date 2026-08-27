# Delta for api-facturas

Revises BACKLOG #11 spec. Small additive delta so the SPA can edit `tipoComprobante` and `numero`.

## ADDED Requirements

### Requirement: `CorreccionFacturaRequest` accepts `tipoComprobante` and `numero`

`CorreccionFacturaRequest` (and the `CorreccionFactura` core type and `ServicioDeFacturas.PatchAsync`) MUST additively accept optional `tipoComprobante` and `numero` fields. When present on a `PATCH /api/facturas/{id}` body, the endpoint MUST apply them to `fact.Factura` under the same `If-Match` optimistic-concurrency and `AuditoriaCorreccion` rules as existing editable fields. Domain validation MUST reject an empty/blank `numero` and a `tipoComprobante` outside the accepted comprobante-type set. No existing request field changes name, type, or meaning.

This delta does NOT add `base imponible`, `IGV`, `TC compra`, `glosa`, `mes`, or `día contable` to the write contract — those remain out of scope and, where applicable, gated behind a REGLAS.md review and versioned SQL.

#### Scenario: PATCH updates tipoComprobante and numero

- GIVEN a factura with a valid current `ETag`
- WHEN `PATCH /api/facturas/{id}` is sent with `If-Match` and a body carrying `tipoComprobante` and `numero`
- THEN both columns update on `fact.Factura`, `Version` advances, and the new `ETag` is returned

#### Scenario: Correction on a validated factura writes audit

- GIVEN a `PATCH` changes `numero` on a factura whose asiento is `CONFIRMADO`
- WHEN the edit succeeds
- THEN a `fact.AuditoriaCorreccion` row is written with `EntidadTipo=FACTURA`, `Accion=CORRECCION`

#### Scenario: Blank numero is rejected

- GIVEN a `PATCH` body with `numero` empty or whitespace
- WHEN the request is processed
- THEN it is rejected with `422 Unprocessable Content` (`application/problem+json`) and zero rows update

#### Scenario: Unknown tipoComprobante is rejected

- GIVEN a `PATCH` body with `tipoComprobante` not in the accepted comprobante-type set
- WHEN the request is processed
- THEN it is rejected with `422 Unprocessable Content` and zero rows update

#### Scenario: Omitting the new fields is a no-op

- GIVEN a `PATCH` body without `tipoComprobante` or `numero`
- WHEN the request is processed
- THEN those columns are left unchanged and existing behavior is identical to before this delta
