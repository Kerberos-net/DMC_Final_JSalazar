# Spec: Ingesta Gmail (BACKLOG #5)

New capability: single-run Python worker extension that polls a labeled Gmail inbox, downloads
candidate attachments, and persists them as `fact.Email` / `fact.DocumentoRecibido` rows. Schema,
grants, and `Configuracion` INGESTA keys (`ETIQUETA_ORIGEN`, `EXTENSIONES_PERMITIDAS`,
`FRECUENCIA_SONDEO_MINUTOS`, `FECHA_INICIO`) already shipped in item #1; this item adds the missing
`ETIQUETA_PROCESADO` key and the code that reads a message for the first time. See `proposal.md`
Out of Scope.

## Non-Goals (explicit scope boundaries)

- **No `fact.Procesamiento` / `fact.DatosExtraidos` rows.** This item stops at
  `DocumentoRecibido.Estado='DESCARGADO'`; extraction/processing is item #6's responsibility
  (ADR 0003's private-tables split between ingestion and processing).
- **No PDF↔XML association logic.** ADR 0017's identity-tributaria matching, `AfectacionMixta`
  detection, and the XML-as-priority-source rule all operate on already-downloaded content and
  belong to #6, not to candidacy/download.
- **No in-process scheduler, daemon, or retry loop.** `cli_gmail.py` is single-run, mirroring
  `cli_tipo_cambio.py` from item #4; recurring execution is a deployment concern (cron/Task
  Scheduler), not application code.
- **No `ADR 0010` error-classification/retry machinery.** This item logs run outcome to
  `fact.EstadoIntegracion` (success/failure), but does not implement `TRANSITORIO`/`DIFERIBLE`/
  `PERMANENTE`/`OBSOLETO` classification, `ProcesamientoIntentos` rows, or the
  `POST /api/incidencias/{id}/reprocesar` recovery path — those apply to `fact.Procesamiento`,
  which this item never writes.
- **No Vault-based secrets manager (ADR 0015).** OAuth credentials and the shared volume root are
  read from env vars only, with no default in code — explicitly out of backlog scope per the
  proposal's resolved decision.
- **No deletion of Gmail messages.** The worker only ever adds its own "processed" label; per
  ADR 0017's rejected alternative, deleting emails after labeling was explicitly discarded because
  a labeling bug would silently trash unprocessed invoices.
- **No .NET-side wiring of the shared volume root for reading/serving documents (ADR 0013).** This
  item only requires the worker to write into the volume; the API's read path is out of scope here.

---

## Capability: `sondeo-acotado-gmail`

### Requirement: The worker builds and executes a Gmail search bounded by three configurable terms

Per ADR 0017, the polling query MUST be `label:<etiqueta-origen> -label:<etiqueta-procesado>
after:<fecha-inicio-configurada>`, with all three terms read from `fact.Configuracion`
(`ETIQUETA_ORIGEN`, `ETIQUETA_PROCESADO`, `FECHA_INICIO`) — never hardcoded. The
`-label:<etiqueta-procesado>` term is what keeps each cycle from rereading the entire label history
once messages accumulate; `after:<fecha-inicio>` is what keeps the first run from pulling years of
mail.

#### Scenario: A run queries only unlabeled-as-processed messages after the configured start date
- **Given** `fact.Configuracion` has `ETIQUETA_ORIGEN='Facturas'`, `ETIQUETA_PROCESADO='fact-procesado'`,
  and `FECHA_INICIO='2026-01-01'`
- **When** `cli_gmail.py` runs one poll cycle
- **Then** the Gmail search executed is exactly
  `label:Facturas -label:fact-procesado after:2026/01/01` (Gmail's search operator requires
  `YYYY/MM/DD`, not the ISO `YYYY-MM-DD` stored in `Configuracion.FECHA_INICIO` — the worker
  reformats it when building the query)

#### Scenario: A message already carrying the processed label is never returned by the poll
- **Given** a message matches `ETIQUETA_ORIGEN` but already carries `ETIQUETA_PROCESADO`
- **When** the poll query runs
- **Then** that message is excluded from the result set and is not considered for download

#### Scenario: Missing required Configuracion value fails the run before any Gmail call
- **Given** `ETIQUETA_ORIGEN`, `ETIQUETA_PROCESADO`, or `FECHA_INICIO` has `Valor IS NULL`
- **When** `cli_gmail.py` starts
- **Then** the run fails fast with no Gmail API call issued, and the failure is logged to
  `fact.EstadoIntegracion` (`Nombre='GMAIL'`)

---

## Capability: `candidatura-etiqueta-extension`

### Requirement: An attachment is a download candidate only when its email matches the poll query AND its extension is in the configured allow-list

Per ADR 0017, candidacy is exactly two conditions — label membership (already enforced by the
bounded query) and an allowed attachment extension. Subject and sender MUST NOT be evaluated at
any point. `EXTENSIONES_PERMITIDAS` is a comma-separated list (per the proposal's resolved
delimiter decision, e.g. `pdf,xml`).

#### Scenario: An attachment with an allowed extension is a candidate
- **Given** `EXTENSIONES_PERMITIDAS='pdf,xml'` and a matched message has a `factura.pdf` attachment
- **When** candidacy is evaluated for that attachment
- **Then** it is selected for download

#### Scenario: An attachment with a disallowed extension is skipped
- **Given** `EXTENSIONES_PERMITIDAS='pdf,xml'` and a matched message has a `nota.docx` attachment
- **When** candidacy is evaluated for that attachment
- **Then** it is not downloaded and no `fact.DocumentoRecibido` row is created for it

#### Scenario: A matched message with no allowed-extension attachment produces no download
- **Given** a message matches the poll query but its only attachment is `imagen.jpg`, which is not
  in `EXTENSIONES_PERMITIDAS`
- **When** the message is evaluated
- **Then** no attachment is downloaded for that message

#### Scenario: Subject and sender never influence candidacy
- **Given** a matched message with subject `"newsletter"` and sender `noreply@example.com`, carrying
  a `factura.pdf` attachment
- **When** candidacy is evaluated
- **Then** the attachment is selected for download exactly as it would be for any other subject/sender

---

## Capability: `descarga-hash-persistencia`

### Requirement: A candidate attachment is downloaded to the shared volume, hashed with SHA-256, and persisted as `Email` + `DocumentoRecibido` with `Estado='DESCARGADO'`

Per ADR 0017's "identidad del adjunto," each downloaded attachment MUST record `GmailMessageId`,
`NombreArchivo`, `Extension`, `MimeType`, and `HashContenido` (SHA-256 hex). Per ADR 0013, the file
is written under the shared volume's configurable root and `RutaRelativa` stores the path relative
to that root — never an absolute path baked to one environment. Per ADR 0003, all writes use
`fact_worker` credentials and target only `fact.Email` / `fact.DocumentoRecibido`.

#### Scenario: A successful download persists both rows with the expected identity fields
- **Given** a candidate `factura.pdf` attachment on a matched message
- **When** the worker downloads it
- **Then** a `fact.Email` row exists for that `GmailMessageId`, and a `fact.DocumentoRecibido` row
  exists with `NombreArchivo='factura.pdf'`, `Extension='pdf'`, `MimeType` set from the attachment,
  a 64-character `HashContenido`, and `Estado='DESCARGADO'`

#### Scenario: The stored path is relative to the configured shared volume root
- **Given** the worker's shared volume root env var is set to a path
- **When** an attachment is downloaded
- **Then** `RutaRelativa` stores the path relative to that root, not an absolute filesystem path

#### Scenario: Persistence writes only to the worker's private tables
- **Given** a completed download, successful or failed
- **When** inspecting the SQL statements the worker executes for that attachment
- **Then** none targets a `dbo.*` table or any `.NET`-owned table (`fact.Factura`,
  `fact.AdjuntoManual`, etc.) — only `fact.Email`, `fact.DocumentoRecibido`, and
  `fact.EstadoIntegracion`

#### Scenario: Re-polling the same message before it is labeled processed does not duplicate the Email row
- **Given** a message was already persisted as `fact.Email` in a prior run whose label write-back
  failed, so the message still matches the bounded query
- **When** the next run polls and encounters that same `GmailMessageId` again
- **Then** no second `fact.Email` row is created for it — the existing `UQ_Email_GmailMessageId`
  constraint is the enforcement mechanism, consistent with ADR 0010's expectation that a retried
  operation not create duplicates

#### Scenario: No fact.Procesamiento or fact.DatosExtraidos row is created
- **Given** any successful download in this item's scope
- **When** the run completes
- **Then** no row exists in `fact.Procesamiento` or `fact.DatosExtraidos` as a result of that
  download — those tables remain #6's responsibility

---

## Capability: `write-back-etiqueta-procesado`

### Requirement: After a message's eligible attachments are successfully persisted, the worker applies its own "processed" label to that message and never deletes it

Per ADR 0017, the label write-back uses Gmail scope `gmail.modify`, is reversible, does not disturb
the user's read/unread state, and the original message survives as evidence of last resort. Label
application MUST be per-message, applied only once that message's persistence has succeeded, so a
message whose download/persistence failed remains unlabeled and is re-offered by the next run's
bounded query.

#### Scenario: A message with a successfully persisted attachment gets the processed label
- **Given** a matched message's `factura.pdf` attachment was downloaded and persisted successfully
- **When** the run finishes processing that message
- **Then** the message carries `ETIQUETA_PROCESADO` in Gmail afterward

#### Scenario: A message whose persistence failed is not labeled
- **Given** a matched message's attachment download or `fact.DocumentoRecibido` insert failed
- **When** the run finishes processing that message
- **Then** the message does not carry `ETIQUETA_PROCESADO`, so it is returned again by the next
  run's bounded query

#### Scenario: The worker never issues a delete or trash call against Gmail
- **Given** any run, successful or failed
- **When** inspecting the Gmail API calls issued
- **Then** none is a delete or trash operation on any message

---

## Capability: `registro-estado-integracion-gmail`

### Requirement: Every run — success or failure — logs an outcome to `fact.EstadoIntegracion` (`Nombre='GMAIL'`)

Per ADR 0003, `EstadoIntegracion` is written outside the business transaction as telemetry, and its
`GMAIL` row feeds the "Conectado / Con error" indicator. This generalizes the existing
`estado_integracion.py` helper (`UPDATE ... WHERE Nombre=X`, raise if `rowcount != 1`), established
in item #4 for `Nombre='SBS'`, to also accept `Nombre='GMAIL'`.

#### Scenario: A successful run updates UltimoIntento and UltimoExito and resets FallosSeguidos
- **Given** a run downloads and persists at least the messages it found eligible without error
- **When** the run finishes
- **Then** `fact.EstadoIntegracion` for `Nombre='GMAIL'` has `UltimoIntento` and `UltimoExito` set to
  that run's time, and `FallosSeguidos = 0`

#### Scenario: A failed run updates UltimoIntento, UltimoError, and increments FallosSeguidos
- **Given** the run fails (Gmail unreachable, missing Configuracion value, credential error, etc.)
- **When** the run finishes without completing its cycle
- **Then** `fact.EstadoIntegracion` for `Nombre='GMAIL'` has `UltimoIntento` set to that run's time,
  `UltimoError` populated, and `FallosSeguidos` incremented by one

#### Scenario: EstadoIntegracion logging failure does not roll back an otherwise-successful download
- **Given** an attachment was already downloaded and its `fact.DocumentoRecibido` row committed
- **When** the subsequent `fact.EstadoIntegracion` write fails
- **Then** the `fact.DocumentoRecibido` row remains committed — logging is telemetry outside the
  business write, per ADR 0003
