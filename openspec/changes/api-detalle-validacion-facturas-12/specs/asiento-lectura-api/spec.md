# Spec: asiento-lectura-api (BACKLOG #12)

New capability — read access to an asiento by id, and resolution from a
factura to its vigente asiento. Orchestration over existing repositories; no
new domain logic.

## Capability: `asiento-lectura-api`

### Requirement: `GET /api/asientos/{id}` returns the asiento

The endpoint MUST return the asiento's current state, líneas, `ETag` (for
subsequent `If-Match` edits), and `TipoCambioVenta` when applicable.

#### Scenario: Fetching an existing asiento
- **Given** an asiento identified by `id` exists
- **When** `GET /api/asientos/{id}` is called
- **Then** the response is `200 OK` with the asiento body, its líneas, and
  an `ETag` matching its current `Version`

#### Scenario: Unknown asiento id returns 404
- **Given** no asiento exists with the given `id`
- **When** `GET /api/asientos/{id}` is called
- **Then** the response is `404 Not Found`

### Requirement: Factura resolves to its vigente asiento over HTTP

The API MUST expose a way to resolve a `FacturaId` to its current
(non-`ANULADO`) `AsientoContableId`, using the existing
`IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync` lookup. No new resolution
rule is introduced.

#### Scenario: Factura with a vigente asiento
- **Given** a factura with a non-`ANULADO` asiento
- **When** the factura→asiento resolution is requested for that factura
- **Then** the response identifies that asiento's id

#### Scenario: Factura with no asiento yet
- **Given** a factura that has not been opened (`abrir` not yet called)
- **When** the factura→asiento resolution is requested
- **Then** the response indicates no vigente asiento exists, distinctly from
  a 404 on an unknown factura
