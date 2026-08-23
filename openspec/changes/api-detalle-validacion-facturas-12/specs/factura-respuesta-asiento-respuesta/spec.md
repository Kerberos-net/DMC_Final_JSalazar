# Delta for factura-respuesta-asiento-respuesta (BACKLOG #12)

Adds the frozen sale exchange rate to the two existing response DTOs
(`FacturaRespuesta`, `AsientoRespuesta`, defined in `api-facturas` and
`api-asientos`, BACKLOG #11). Additive, non-breaking; no new domain rule.

## ADDED Requirements

### Requirement: `TipoCambioVenta` is exposed in factura and asiento responses

Per ADR 0018 pt.1 (foreign-currency liabilities convert at tipo de cambio
**venta**, not compra), `FacturaRespuesta` and `AsientoRespuesta` MUST
include a `TipoCambioVenta` field sourced from the persisted
`TipoCambioCongelado.Venta` frozen at asiento generation/confirmation time.
The field MUST NOT be sourced from or renamed to a "compra" rate.

#### Scenario: Foreign-currency factura exposes its frozen venta rate
- **Given** a factura in foreign currency with an asiento generated using a
  frozen `TipoCambioCongelado`
- **When** the factura or its asiento is fetched (`GET`, or returned by
  `PATCH`/`validar`)
- **Then** the response body includes `TipoCambioVenta` equal to the frozen
  `TipoCambioCongelado.Venta` value used for that asiento

#### Scenario: Local-currency (PEN) factura has no applicable rate
- **Given** a factura already in PEN, with no `TipoCambioCongelado` applied
- **When** the factura or its asiento is fetched
- **Then** `TipoCambioVenta` is `null`/absent, not a fabricated or default
  value

#### Scenario: Field addition does not break existing consumers
- **Given** an existing `/api/facturas/{id}` or `/api/asientos/{id}` response
  consumer built against BACKLOG #11's contract
- **When** `TipoCambioVenta` is added to the response body
- **Then** all previously existing fields remain unchanged in name, type,
  and meaning
