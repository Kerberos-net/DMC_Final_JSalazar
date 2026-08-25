# Spec: api-incidencias-integraciones (BACKLOG #11)

New capability — commands that queue background work (ADR 0004) and the integrations status
projection, per ADR 0008. .NET MUST NOT call Python directly (ADR 0003 partition).

## Capability: `api-incidencias-integraciones`

### Requirement: reprocesar/sincronizar/reconectar enqueue CommandQueue rows, never call Python

`POST /api/incidencias/{id}/reprocesar`, `POST /api/integraciones/{nombre}/sincronizar`, and `POST
/api/integraciones/google/reconectar` MUST insert a `fact.CommandQueue` row in the same
transaction as the response and MUST NOT write `AuditoriaCorreccion` (none of the three is in the
`Accion` enum). For `reprocesar`, `{id}` MUST be interpreted as `ProcesamientoId` — the key that
`fact.ProcesamientoError` is indexed by — not `InboxEventId` or `FacturaId`.
(Previously: `{id}` semantics were unspecified; route and enqueue-only behavior were already shipped by #11.)

#### Scenario: Reprocesar an incidencia enqueues a command
- **Given** an unresolved incidencia (id known)
- **When** `POST /api/incidencias/{id}/reprocesar` is called
- **Then** a `fact.CommandQueue` row is inserted targeting that incidencia, no HTTP/RPC call to the
  Python worker is made, and no `AuditoriaCorreccion` row is written

#### Scenario: Reprocesar uses ProcesamientoId, not InboxEventId or FacturaId
- **Given** a document with a known `ProcesamientoId` distinct from its `InboxEventId`/`FacturaId`
- **When** `POST /api/incidencias/{id}/reprocesar` is called with `{id}` set to that `ProcesamientoId`
- **Then** the command queue row and the `fact.ProcesamientoError` lookup used to build the panel
  de errores resolve to the same document

#### Scenario: Sincronizar an integration enqueues a command
- **Given** a named integration (e.g. `gmail`)
- **When** `POST /api/integraciones/gmail/sincronizar` is called
- **Then** a `fact.CommandQueue` row is inserted for that integration and no
  `AuditoriaCorreccion` row is written

#### Scenario: Reconectar Google enqueues a command
- **Given** the Google integration needs re-authentication
- **When** `POST /api/integraciones/google/reconectar` is called
- **Then** a `fact.CommandQueue` row is inserted and no `AuditoriaCorreccion` row is written

#### Scenario: Unknown integration name is rejected
- **Given** an integration name not recognized by the system
- **When** `POST /api/integraciones/{nombre}/sincronizar` is called
- **Then** the response is `409 Conflict` or `400 Bad Request` and no `CommandQueue` row is
  inserted

### Requirement: `GET /api/integraciones/estado` derives the connection pill, never stores it

Per ADR 0008, the "Conectado / Con error" pill is derived from `fact.EstadoIntegracion` fields
(last run, last success, last error, consecutive failures) — it is not a stored column.

#### Scenario: Integration with recent success reports Conectado
- **Given** `fact.EstadoIntegracion` shows the last attempt for `SBS` succeeded
- **When** `GET /api/integraciones/estado` is called
- **Then** the `SBS` entry's derived status is `Conectado`, computed from the stored fields at
  request time

#### Scenario: Integration with consecutive failures reports Con error
- **Given** `fact.EstadoIntegracion` shows the last N attempts for `gmail` failed
- **When** `GET /api/integraciones/estado` is called
- **Then** the `gmail` entry's derived status is `Con error`
