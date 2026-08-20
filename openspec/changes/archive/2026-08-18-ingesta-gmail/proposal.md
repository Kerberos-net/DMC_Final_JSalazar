# Proposal: Ingesta Gmail (BACKLOG #5)

## Intent

Facturas arrive as email attachments in a shared Gmail inbox. Today nothing polls that inbox:
`fact.Email` and `fact.DocumentoRecibido` exist since item #1 (schema-only), and
`fact.Configuracion` already carries the INGESTA keys (`ETIQUETA_ORIGEN`, `EXTENSIONES_PERMITIDAS`,
`FRECUENCIA_SONDEO_MINUTOS`, `FECHA_INICIO`) seeded with `Valor=NULL`, but no code reads a message,
downloads an attachment, or writes a row. This item builds the ingestion capability — candidacy by
label + extension, attachment download, hash computation, Gmail label write-back, and the bounded
polling query defined in ADR 0017 — the prerequisite #6 (extraction/processing) depends on.

**No new tables, columns, or grants for `Email`/`DocumentoRecibido`.** They already shipped in
`SmartNet/db/schema/003_ingesta_y_procesamiento.sql` / `008_usuarios_y_permisos.sql`. One small new
migration is needed: the missing `Configuracion.ETIQUETA_PROCESADO` key, **and** — per an explicit
design-stage decision (see design.md, revised Open Questions) — a `UNIQUE (EmailId, HashContenido)`
constraint on `DocumentoRecibido`, added now rather than deferred to item #6, since ADR 0010 already
assumes this index exists for idempotent reprocessing. This revises the item's original "no new DDL"
framing: the migration adds one seeded row and one constraint, not a table/column change.

## Decisions already resolved (not open questions)

- **Gmail OAuth credentials**: env var only (token/refresh-token), no default in code — same
  pattern as `config.py` from item #4 (`ODBC_CONNECTION_ENV_VAR`). Reason: ADR 0015's prescribed
  Vault-based secrets manager is explicitly out of scope for the backlog; env-var-only is the
  minimal surface that doesn't hardcode a secret while staying consistent with the worker's
  existing convention.
- **Missing `ETIQUETA_PROCESADO` in `fact.Configuracion`**: added via one small new SQL migration,
  consistent with how item #1 seeded the other INGESTA keys. Reason: ADR 0017's polling query needs
  a third configurable term (`label:<etiqueta-origen> -label:<etiqueta-procesado>
  after:<fecha-inicio>`) that item #1 never seeded — this is a genuine schema gap, not a design
  choice to work around.
- **Scheduler shape**: single-run CLI (`cli_gmail.py`), mirrors `cli_tipo_cambio.py` from item #4 —
  one full poll cycle (query → download → hash → write → label) per invocation, recurring
  scheduling deferred to deployment (cron/Task Scheduler). Reason: matches the proven pattern from
  #4, avoids building in-process daemon supervision/shutdown that the backlog never asked for, and
  keeps scheduling policy a deployment concern rather than application code.
- **Shared volume root**: env var (e.g. `SMARTNET_WORKER_STORAGE_ROOT`), no default in code, same
  pattern as the worker's other configs. Reason: ADR 0013 requires a configurable root delivered to
  both runtimes (.NET API for read/download, Python worker for write); hardcoding a path would
  break that contract across environments.

## Scope

### In Scope
- `SmartNet/worker/src/smartnet_worker/` — Gmail API client wrapper (read + label write-back,
  scope `gmail.modify` per ADR 0015), attachment download, SHA-256 hash computation, repository for
  `Email`/`DocumentoRecibido` writes (mirrors `tipo_cambio_repo.py`), `EstadoIntegracion` logging
  generalized beyond the current SBS-only module (`Nombre='GMAIL'` row).
- `SmartNet/worker/src/smartnet_worker/config.py` — extended with Gmail OAuth credential loading and
  shared volume root, both via env var, no defaults.
- `SmartNet/worker/pyproject.toml` — new dependency for Gmail API access
  (`google-api-python-client` + auth libs).
- `cli_gmail.py` — single-run entry point: one bounded polling query
  (`label:<etiqueta-origen> -label:<etiqueta-procesado> after:<fecha-inicio>`), download eligible
  attachments (candidacy = label + allowed extension only, per ADR 0017 — subject/sender never
  used), write `fact.Email` + `fact.DocumentoRecibido` (`Estado='DESCARGADO'`), apply the
  "processed" Gmail label, log the run to `fact.EstadoIntegracion` (`Nombre='GMAIL'`).
- One small new SQL migration seeding the `ETIQUETA_PROCESADO` key in `fact.Configuracion`.

### Out of Scope
- `fact.Procesamiento` / `fact.DatosExtraidos` rows — extraction/processing is item #6's
  responsibility; this item stops at `DocumentoRecibido.Estado='DESCARGADO'`.
- Any in-process daemon/polling loop or cross-integration scheduler shared with the SBS scraper —
  deferred to deployment configuration.
- Vault-based secrets management (ADR 0015) — explicitly out of backlog scope; env vars are the
  interim mechanism.
- Deleting or modifying Gmail messages beyond applying the "processed" label — the worker never
  deletes.
- Retry/backoff policy for transient Gmail API failures beyond what a single run naturally gets
  from being re-invoked (no in-process retry loop this item).

## Capabilities

### New Capabilities
- `ingesta-gmail`: bounded polling of a labeled Gmail inbox, label+extension candidacy, attachment
  download to shared volume, SHA-256 hashing, `Email`/`DocumentoRecibido` persistence
  (`Estado='DESCARGADO'`), Gmail label write-back. Covers `SmartNet/worker/` extensions and
  `cli_gmail.py`.

### Modified Capabilities
None — `Email`/`DocumentoRecibido` schema and grants already exist from item #1; only their
`Configuracion` sibling key is new.

## Approach

Extend the existing Python worker rather than create a third stack (confirmed by exploration:
item #4's `cli_tipo_cambio.py` docstring explicitly defers scheduling/polling to #5, and #4's own
proposal names #5 as the reuse point). Follow the same internal split #4 established: a pure
parsing/decision module (candidacy check, hash computation) separate from the single IO entry point
(`cli_gmail.py`), plus a repository mirroring `tipo_cambio_repo.py`. `EstadoIntegracion` logging
generalizes the existing `estado_integracion.py` helper (`UPDATE ... WHERE Nombre=X`, raise if
`rowcount != 1`) to accept `Nombre='GMAIL'` alongside `'SBS'`. Gmail candidacy, identity, and
write-back rules come directly from ADR 0017 — label + extension only, `GmailMessageId` + filename
+ extension + MIME + `HashContenido` for idempotent reprocessing (ADR 0010), own "processed" label
applied, never deleted.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/worker/src/smartnet_worker/` | Modified | Gmail client wrapper, attachment download, hashing, `Email`/`DocumentoRecibido` repo |
| `SmartNet/worker/src/smartnet_worker/config.py` | Modified | Gmail OAuth env vars + shared volume root env var |
| `SmartNet/worker/pyproject.toml` | Modified | New Gmail API dependency |
| `cli_gmail.py` | New | Single-run poll → download → hash → write → label entry point |
| `fact.Configuracion` | New (data) | One small migration seeding `ETIQUETA_PROCESADO` |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| OAuth token expiry/refresh handling underspecified beyond "env var, no default" | Med | Scoped to `gmail.modify`; refresh mechanics are an implementation detail of this item, not a new decision — flagged if it surfaces a genuine blocker during spec/design |
| `EXTENSIONES_PERMITIDAS` delimiter format unresolved (see question round below) | Med | Explicit proposal question — not assumed silently |
| Shared volume root not yet wired to the .NET API side (read/download path) | Low | This item only requires the worker to write; API-side read wiring is out of scope here and doesn't block it |
| Large/slow Gmail API responses during a single poll cycle | Low | Bounded query (label + `-label:processed` + `after:fecha-inicio`) keeps result sets small by construction |

## Rollback Plan

Revert `cli_gmail.py` and the `smartnet_worker` extensions; revert the one small `Configuracion`
migration (single `DELETE`/rollback script, no other schema changes to undo since `Email`/
`DocumentoRecibido` predate this item).

## Dependencies

- Item #1 (schema/permissions for `Email`, `DocumentoRecibido`, `Configuracion`,
  `EstadoIntegracion`) — already closed.
- Item #4 (worker tooling convention: `config.py`, `estado_integracion.py`, CLI single-run pattern)
  — already closed, reused directly.

## Success Criteria

- [ ] `cli_gmail.py` executes one full poll cycle per invocation and exits (no in-process loop).
- [ ] Only messages matching `label:<etiqueta-origen> -label:<etiqueta-procesado>
      after:<fecha-inicio>` are considered.
- [ ] An attachment is downloaded only if it matches the configured label AND an allowed extension
      (subject/sender never evaluated).
- [ ] Each downloaded attachment gets a SHA-256 hash and a `fact.DocumentoRecibido` row with
      `Estado='DESCARGADO'`.
- [ ] No `fact.Procesamiento`/`fact.DatosExtraidos` rows are created by this item.
- [ ] After a successful run, the Gmail message carries the worker's own "processed" label; the
      message is never deleted.
- [ ] Gmail OAuth credentials and the shared volume root are read only from env vars, with no
      default value in code.
- [ ] `fact.Configuracion.ETIQUETA_PROCESADO` exists after the new migration runs.
- [ ] Every run (success or failure) logs an outcome to `fact.EstadoIntegracion` (`Nombre='GMAIL'`).

## Decisions already resolved — round 2

- **`EXTENSIONES_PERMITIDAS` delimiter format**: comma-separated (e.g. `pdf,xml`) — simple to seed
  and edit by hand in SQL, no JSON parsing needed for what is a trivial list.

## Proposal question round

None remaining. All five decisions (the four from exploration plus the delimiter format above) are
resolved.
