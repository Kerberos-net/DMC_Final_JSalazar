# Delta for factura-respuesta-asiento-respuesta (BACKLOG #19)

Adds the per-field OCR list and the free-text glosa to `FacturaRespuesta`.
Additive, non-breaking; no new domain rule. `AsientoRespuesta` is untouched by
this item.

## ADDED Requirements

### Requirement: `FacturaRespuesta` exposes `CamposNoExtraidos` and `Glosa`

`FacturaRespuesta` MUST additively include:

- `CamposNoExtraidos`: a `string[]` naming the invoice fields the ingesta worker
  could not extract, drawn from the canonical set `tipoComprobante`, `numero`,
  `ruc`, `nombreProveedor`, `total`, `igv`, `moneda`, `fechaEmision`. Sourced from
  the new `fact.Factura.CamposNoExtraidos` column promoted from
  `EventoInbox.CamposNoExtraidos` (no API-side derivation). It MUST be non-empty
  if and only if the existing `TieneCamposNoExtraidos` boolean is `true`.
- `Glosa`: the nullable free-text `fact.Factura.Glosa` value.

The existing `TieneCamposNoExtraidos` boolean MUST be retained. No existing
`FacturaRespuesta` field changes name, type, or meaning.

#### Scenario: Per-field list is returned
- **Given** a factura promoted with `numero` and `moneda` not extracted
- **When** its `FacturaRespuesta` is fetched
- **Then** `CamposNoExtraidos` is `["numero","moneda"]` and `TieneCamposNoExtraidos` is `true`

#### Scenario: Nothing missing
- **Given** a factura with every canonical field extracted
- **When** its `FacturaRespuesta` is fetched
- **Then** `CamposNoExtraidos` is `[]` and `TieneCamposNoExtraidos` is `false`

#### Scenario: Glosa round-trips
- **Given** a factura whose `Glosa` was set via `PATCH`
- **When** its `FacturaRespuesta` is fetched
- **Then** the response `Glosa` equals the persisted value; `null` when unset

#### Scenario: Field addition does not break existing consumers
- **Given** a consumer built against the prior `FacturaRespuesta` contract
- **When** `CamposNoExtraidos` and `Glosa` are added
- **Then** all previously existing fields remain unchanged in name, type, and meaning

## Open Questions (affecting requirements)

- **XML-sourced invoices** — spec assumes the worker's `CamposNoExtraidos` list is
  trusted as-is even when a UBL XML is present; no API-side reconciliation
  (proposal default). Design to confirm.
