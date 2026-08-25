# Delta for factura-respuesta-asiento-respuesta (BACKLOG #12)

Adds the frozen sale exchange rate to the existing asiento response DTO
(`AsientoRespuesta`, defined in `api-asientos`, BACKLOG #11). Additive,
non-breaking; no new domain rule.

> **Scope correction (apply, PR3/PR5).** design.md Decision D4 restricts
> this field to `AsientoRespuesta` only — sourced directly from the
> already-persisted `AsientoContable.TipoCambioCongelado.Venta`
> (`SqlUnidadDeTrabajo.cs:53,107,128,136`, unchanged by this item).
> `FacturaRespuesta` does NOT gain a `TipoCambioVenta` field: `Factura` has
> no equivalent frozen-rate column, and surfacing a *different* column
> (`Factura.TipoCambioAplicado`) beside it would let two rates diverge on
> screen for the same document — exactly the ambiguity D4 exists to avoid.
> The requirement below reflects the corrected, as-implemented scope; the
> original delta text naming both DTOs was the literal (uncorrected) intent
> before design.md's own D4 correction was applied.

## ADDED Requirements

### Requirement: `TipoCambioVenta` is exposed in the asiento response

Per ADR 0018 pt.1 (foreign-currency liabilities convert at tipo de cambio
**venta**, not compra), `AsientoRespuesta` MUST include a `TipoCambioVenta`
field sourced from the persisted `TipoCambioCongelado.Venta` frozen at
asiento generation/confirmation time. The field MUST NOT be sourced from or
renamed to a "compra" rate, and MUST NOT be added to `FacturaRespuesta`
(design D4 — see scope correction above).

#### Scenario: Foreign-currency asiento exposes its frozen venta rate
- **Given** an asiento generated for a factura in foreign currency, using a
  frozen `TipoCambioCongelado`
- **When** the asiento is fetched (`GET /api/asientos/{id}`,
  `GET /api/facturas/{id}/asiento`, or returned by `PATCH`/`validar`)
- **Then** the response body includes `TipoCambioVenta` equal to the frozen
  `TipoCambioCongelado.Venta` value used for that asiento

#### Scenario: Local-currency (PEN) asiento has no applicable rate
- **Given** an asiento for a factura already in PEN, with no
  `TipoCambioCongelado` applied
- **When** the asiento is fetched
- **Then** `TipoCambioVenta` is `null`/absent, not a fabricated or default
  value

#### Scenario: Field addition does not break existing consumers
- **Given** an existing `/api/asientos/{id}` response consumer built against
  BACKLOG #11's contract
- **When** `TipoCambioVenta` is added to the response body
- **Then** all previously existing fields remain unchanged in name, type,
  and meaning
