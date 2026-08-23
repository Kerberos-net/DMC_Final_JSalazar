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

### Requirement: Content is served from the .NET-owned projection, never from Python-owned storage

Per ADR 0003 §Privadas (invariant 3, symmetric with the Python-side rule),
`fact.DocumentoRecibido` is Python-owned and `fact_api` has **no SELECT
grant** on it (`008_usuarios_y_permisos.sql` DENY, unchanged). Reading it —
not just writing it — is the violation. For a document that originated from
Python's ingesta pipeline, this endpoint MUST resolve its metadata
(`NombreArchivo`, `MimeType`, `RutaRelativa`) from `fact.DocumentoFactura`
(the .NET-owned projection populated asynchronously at `InboxEvent`
promoción — schema 016), never from `fact.DocumentoRecibido`. This endpoint
MUST NOT issue any SELECT, INSERT, UPDATE, or DELETE against
`fact.DocumentoRecibido` or any other Python-owned table.

#### Scenario: Serving a projected ingesta-origin file
- **Given** the requested document is an ingesta-origin document already
  promoted into `fact.DocumentoFactura`
- **When** its content is served
- **Then** only a read query executes against `fact.DocumentoFactura`; no
  statement of any kind is issued against `fact.DocumentoRecibido`
