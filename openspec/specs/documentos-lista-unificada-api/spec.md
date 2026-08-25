# Spec: documentos-lista-unificada-api (BACKLOG #12)

New capability — merged read view of a factura's documents across both
storage owners. Read-only orchestration over .NET-owned data only; no new
domain logic.

## Capability: `documentos-lista-unificada-api`

### Requirement: Unified list merges `fact.DocumentoFactura` and `AdjuntoManual` per factura

The endpoint MUST return, for a given factura, a single ordered list
combining `fact.DocumentoFactura` (.NET-owned projection of documents
ingested by Python, populated asynchronously from the `InboxEvent` payload
at promoción — schema 016) and `AdjuntoManual` (.NET-owned) entries, each
identifying its origin and enough metadata (id, nombre, tipo/MIME, fecha)
for the viewer to fetch its content. The endpoint MUST NOT read
`fact.DocumentoRecibido` or any other Python-owned table; per ADR 0003 the
partition is symmetric and `fact_api` has no SELECT grant on that table.

#### Scenario: Factura with only a projected ingested document
- **Given** a factura with one `fact.DocumentoFactura` row (already
  promoted) and no manual adjuntos
- **When** the unified documents list is requested
- **Then** the response contains exactly that one entry, marked as its
  origin type

#### Scenario: Factura with both projected ingested and manual documents
- **Given** a factura with a `fact.DocumentoFactura` row and one or more
  `AdjuntoManual` entries
- **When** the unified documents list is requested
- **Then** the response contains all of them, each correctly tagged by
  origin, with no duplicates

#### Scenario: Factura with no documents
- **Given** a factura with neither a `fact.DocumentoFactura` row nor any
  `AdjuntoManual`
- **When** the unified documents list is requested
- **Then** the response is an empty list, not an error

### Requirement: The list reflects eventual-consistency gaps in the projection, not a live cross-partition read

Because `fact.DocumentoFactura` is populated asynchronously at `InboxEvent`
promoción rather than read live from Python-owned storage, a document that
was just ingested MAY be temporarily absent from the unified list until its
promoción completes. The endpoint MUST NOT error or block waiting for the
projection to catch up; it MUST return the current state of .NET-owned data
only.

#### Scenario: Document ingested but not yet promoted
- **Given** an `InboxEvent` for a factura's document has arrived but has not
  yet been promoted into `fact.DocumentoFactura`
- **When** the unified documents list is requested
- **Then** the response does not include that document
- **AND** no error is returned

#### Scenario: Document appears after promoción completes
- **Given** a document whose `InboxEvent` has since been promoted into
  `fact.DocumentoFactura`
- **When** the unified documents list is requested again
- **Then** the response now includes that document, tagged by its origin

### Requirement: Documents ingested before schema 016 cannot be retroprojected

`fact.DocumentoFactura` is populated only from `InboxEvent` payloads
processed after schema 016 introduced the additional metadata fields.
Documents ingested before that point have no corresponding row and MUST NOT
be backfilled by reading `fact.DocumentoRecibido` (forbidden by ADR 0003).
The unified list for such a factura degrades to its `AdjuntoManual` entries
only; this is not treated as an error or a missing-data fault.

#### Scenario: Factura with pre-schema-016 ingested documents only
- **Given** a factura whose `DocumentoRecibido` rows predate schema 016 and
  have no matching `fact.DocumentoFactura` projection
- **When** the unified documents list is requested
- **Then** the response contains only its `AdjuntoManual` entries, if any
- **AND** the response is not an error, even though the ingested documents
  are known to exist upstream

### Requirement: The merge is strictly read-only over .NET-owned tables

Per ADR 0003, building this list MUST NOT write, update, or delete
`fact.DocumentoFactura` or `AdjuntoManual`, and MUST NOT attempt any read
against `fact.DocumentoRecibido` or other Python-owned/`dbo.*` tables.

#### Scenario: Listing does not mutate .NET-owned projection data
- **Given** a factura with a `fact.DocumentoFactura` row
- **When** the unified documents list is requested repeatedly
- **Then** no write statement executes against `fact.DocumentoFactura` or
  `AdjuntoManual`

#### Scenario: Listing never queries Python-owned storage
- **Given** any factura, regardless of document state
- **When** the unified documents list is requested
- **Then** no SELECT is issued against `fact.DocumentoRecibido` or any other
  Python-owned table
