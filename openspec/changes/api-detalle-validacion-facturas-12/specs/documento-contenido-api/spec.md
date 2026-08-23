# Spec: documento-contenido-api (BACKLOG #12)

New capability — serves raw document bytes for same-origin viewing. Read-only
orchestration; no new domain logic.

## Capability: `documento-contenido-api`

### Requirement: `GET /api/documentos/{id}/contenido` serves raw bytes with correct MIME type

The endpoint MUST stream the document's stored bytes with a `Content-Type`
matching the document's real MIME type, suitable for same-origin `<iframe>`
embedding. It MUST require the same authentication as other `/api/*`
endpoints (per item #2).

#### Scenario: Fetching an existing document's bytes
- **Given** a document identified by `id` exists and belongs to a factura the
  authenticated user can access
- **When** `GET /api/documentos/{id}/contenido` is called
- **Then** the response is `200 OK` with the document's raw bytes and its
  correct `Content-Type`

#### Scenario: Unknown document id returns 404
- **Given** no document exists with the given `id`
- **When** `GET /api/documentos/{id}/contenido` is called
- **Then** the response is `404 Not Found`

#### Scenario: Unauthenticated request is rejected
- **Given** no valid session
- **When** `GET /api/documentos/{id}/contenido` is called
- **Then** the response is `401 Unauthorized` and no bytes are returned

### Requirement: Reads never write to Python-owned storage

Per ADR 0003, this endpoint MUST read `DocumentoRecibido` content strictly
read-only when the document originates from Python's ingesta pipeline; it
MUST NOT write, update, or delete any Python-owned table or row.

#### Scenario: Serving a DocumentoRecibido-origin file
- **Given** the requested document is a `DocumentoRecibido` (ingesta-owned)
- **When** its content is served
- **Then** only a read query executes against that table; no write statement
  is issued
