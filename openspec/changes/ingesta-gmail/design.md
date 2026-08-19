# Design: Ingesta Gmail (BACKLOG #5)

## Technical Approach

Extend `SmartNet/worker/` with five modules that keep item #4's invariant intact — **a module either
decides or does IO, never both**. `gmail.py` is the pure half (query building, message parsing,
candidacy, hashing, path computation), `gmail_client.py` and `almacenamiento.py` are IO-only with
zero decisions, `documento_repo.py` receives a `cursor` exactly like `tipo_cambio_repo.py`, and
`cli_gmail.py` orchestrates one run and exits. `estado_integracion.py` is generalized from the
hardcoded `Nombre='SBS'` to a required parameter. No DDL for `Email`/`DocumentoRecibido`: their real
columns (003) and `fact_worker` grants (008) already exist. One new migration seeds
`INGESTA.ETIQUETA_PROCESADO`.

Note on money: nothing monetary is read here. `Monto`/`Moneda` live in `fact.DatosExtraidos`, item
#6. The `Decimal`-never-`float` rule has no application surface in this item; the only numeric column
written is `TamanoBytes BIGINT` (`len(bytes)`).

## Architecture Decisions

### Decision 1 — a thin IO-only Gmail client class, not IO inlined in the CLI

| Option | Tradeoff | Decision |
|---|---|---|
| All Gmail IO inside `cli_gmail.py` (literal #4 parity) | #4 had one `requests.get`; this has five call shapes (labels.list, messages.list+pagination, messages.get, attachments.get, messages.modify) — the CLI stops being readable and the orchestration can only be tested against a real API | Rejected |
| `ClienteGmail` in `gmail_client.py`: one method per API call, no branching, no parsing | One more module; but the CLI becomes testable with a fake client exactly as the repos are testable with a fake cursor | **Chosen** |

Rationale: what #4 actually protects is not "one IO module" but "no module mixes decision with IO".
`ClienteGmail` is the `cursor` of Gmail — a seam the unit suite substitutes.

### Decision 2 — `google-api-python-client` + `google-auth`, one env var carrying the whole authorized-user JSON

| Concern | Chosen | Rejected, and why |
|---|---|---|
| Client | `google-api-python-client>=2.140` (`build("gmail","v1")`) | Hand-rolled `requests` against the REST endpoints — reimplements pagination, refresh and error decoding for no gain |
| Auth | `google-auth>=2.34`, `Credentials.from_authorized_user_info(json.loads(env), scopes=[gmail.modify])` | `google-auth-oauthlib` — only needed for the *interactive* consent flow, which ADR 0015 assigns to `POST /api/integraciones/google/reconectar` (.NET), out of scope. Not adding it keeps the worker unable to start a consent flow, which is correct |
| Secret shape | **one** env var `SMARTNET_WORKER_GMAIL_CREDENTIALS` with the full `authorized_user` JSON (`client_id`, `client_secret`, `refresh_token`, `token_uri`) | Three separate env vars — a rotation can leave them mutually inconsistent; the four values are one atomic secret |

Refresh mechanics: `google-auth` exchanges the refresh token for an access token on the first call
(`creds.refresh(Request())`). That access token lives **in memory only** and dies with the process —
a single-run CLI performs exactly one refresh per run and never writes a token back (the env var is
read-only by construction, which is why the JSON-in-env shape is safe here and would not be for a
long-lived daemon). A revoked refresh token surfaces as `google.auth.exceptions.RefreshError` → run
failure in `EstadoIntegracion`. ADR 0010 classifies that `PERMANENTE` and ADR 0015 gives it an exit;
both are item #6/#11 surface, not this one. No default in code: missing or malformed JSON raises
`ConfiguracionError` before any network call, same rule as `SMARTNET_WORKER_ODBC_CONNECTION`.

### Decision 3 — both label names are resolved to Gmail label IDs **before** the first search

`users.labels.list` once per run, name → id. If `ETIQUETA_ORIGEN` or `ETIQUETA_PROCESADO` does not
exist in the mailbox, the run **fails loudly and creates nothing**.

Rationale — this is a safety gate, not a nicety: Gmail silently matches *everything* for
`-label:<name>` when `<name>` does not exist, so an unconfigured deployment with a typo'd
`ETIQUETA_PROCESADO` would ingest the entire mailbox on its first run. Rejected: auto-creating the
label (`labels.create` is permitted by `gmail.modify`) — it converts a misconfiguration into a
silently-working wrong configuration, the same reasoning as `estado_integracion.py`'s
`rowcount != 1` guard refusing to fall back to `INSERT`. `modify` needs the id; the search needs the
name, quoted when it contains spaces (`label:"Facturas 2026"`).

### Decision 4 — the `Email` INSERT is the idempotency gate, and it runs *before* any attachment download

`003_ingesta_y_procesamiento.sql` has **`UQ_Email_GmailMessageId`** and **no** unique constraint on
`DocumentoRecibido` (verified: PK on the IDENTITY column, one FK, two CHECKs, nothing else). So
message-level identity is engine-enforced and attachment-level identity is not. The design makes the
message the unit of idempotency:

    BEGIN → insertar_email (UNIQUE gate) → download+write files → insertar_documento(s) → COMMIT → apply label

`insertar_email` catches `pyodbc.IntegrityError` and returns `None` — literally the shape of
`insertar_sbs` (#4 Decision 3: the engine rejects the duplicate, the adapter only translates; no
`SELECT` first, no TOCTOU window). `None` means "already ingested": skip the download entirely and
just (re-)apply the label, which is a Gmail no-op if already present. This self-heals the only real
partial-failure window — commit succeeded, label write-back failed — without a `DELETE` and without
re-downloading. Rejected: an application-level `SELECT ... WHERE GmailMessageId=?` guard (TOCTOU, and
duplicates a constraint the schema already enforces).

**Resolved (was Open Question 3): `UNIQUE (EmailId, HashContenido)` is added on `DocumentoRecibido`
in this item**, not deferred to #6, per explicit user decision — even though it revises the
proposal's "no new DDL for `Email`/`DocumentoRecibido`" statement. Rationale for adding it now rather
than waiting: ADR 0010 already names this index as part of idempotent reprocessing, and #6 depending
on a constraint #5 was supposed to have shipped is exactly the kind of gap that gets rediscovered as
a bug instead of designed. `insertar_documento` also catches `pyodbc.IntegrityError` on this
constraint and treats a duplicate `(EmailId, HashContenido)` the same way as an already-ingested
message: a no-op, not an error — two attachments in the same message with identical content are the
same document.

Accepted cost: the transaction stays open across the attachment downloads (seconds, on one row of a
single-writer table). Rejected alternative — download first, insert after — burns Gmail quota on
every already-ingested message, which ADR 0010's `DIFERIBLE` class exists precisely to avoid.

### Decision 5 — the on-disk name is derived, deterministic and content-suffixed; `NombreArchivo` keeps the original

| Column | Value |
|---|---|
| `NombreArchivo` NVARCHAR(255) | the attachment's **original** name from Gmail, truncated to 255 — evidence, never used to open a file |
| `RutaRelativa` NVARCHAR(400) | `<yyyy>/<MM>/<GmailMessageId>/<stem-sanitizado>_<hash[:8]>.<ext>` |

`yyyy/MM` come from `FechaRecepcion` (the message's `internalDate`), never from the clock. The
`<hash[:8]>` suffix is what removes the collision case without a counter or an ordering assumption:
two attachments with the same name in the same message differ in content ⇒ different suffix ⇒
different files; identical content ⇒ same bytes ⇒ overwriting is a no-op. It also makes a re-run
after a failed commit rewrite the **same path with the same bytes** instead of accumulating
`factura_1.pdf`, `factura_2.pdf`. Rejected: `<attachment-index>_<name>` (deterministic only while the
MIME-tree walk order is; a nested-multipart fix would silently repath history); rejected: a UUID
(non-deterministic, so a retry orphans the previous write).

Length bound is computed, not hoped for: `8 + 33 + 9 + 11 = 61` fixed characters, stem truncated to
100 ⇒ path ≤ 172 < 400, and each component ≤ 120 < 255 (ADR 0013's NTFS/Drive limit, cited in the
DDL comment).

`sanitizar_nombre_archivo` is pure: NFC-normalize → keep `[A-Za-z0-9._-]`, everything else (path
separators, `:`, spaces, control chars, accents) → `_` → collapse `_` runs → strip leading/trailing
`.`/`_` → empty result becomes `adjunto` → Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`,
`COM1-9`, `LPT1-9`) get an `_` prefix → truncate to 100. Traversal is impossible **twice over**: a
stem of only dots collapses to `adjunto`, and the mandatory `_<hash8>` suffix means no component can
ever equal `.` or `..`. `almacenamiento.escribir` re-asserts containment (`resolved.is_relative_to(raiz)`)
before writing — defense in depth, because the guarantee above is a property of code that can be
edited.

### Decision 6 — candidacy reads the **final** extension only, lowercased, against a comma-separated allow-list

`EXTENSIONES_PERMITIDAS='pdf,xml'` → `frozenset({"pdf","xml"})` (split, strip, lowercase, drop a
leading dot, drop empties). `factura.pdf` ✓, `factura.PDF` ✓, `nota.docx` ✗, **`factura.pdf.exe` ✗**
(the last suffix is `exe`), `sinextension` ✗, inline images with an empty `filename` ✗ naturally.
Never a substring test. Subject and sender are not read by any code path in `gmail.py` other than to
populate `Remitente`/`Asunto` columns — ADR 0017's "el asunto y el remitente no intervienen".

`MimeType` comes from the MIME part; when absent, the declared convention is
`application/octet-stream` (the column is `NOT NULL`), written down here rather than invented at the
call site — same style as `sbs.py`'s midnight `fecha_consulta`. `Remitente` is `NOT NULL`: a message
with no `From` header cannot produce a row, so it is counted as a failed message (see Decision 7) and
is left unlabeled for a human to look at.

### Decision 7 — per-message failure isolation; the run's verdict is one `EstadoIntegracion` write

A failing message rolls back **its own** transaction and is left unlabeled, so the next run's bounded
query offers it again (spec: "A message whose persistence failed is not labeled"). The run continues
with the remaining messages. At the end: `registrar_exito` if every message succeeded, otherwise
`registrar_fallo` with a summary (message count + first error, truncated to 2000 by the existing
helper) in its own transaction after rollback — the exact shape of `cli_tipo_cambio.py`. Exit code 0
or 1. `EstadoIntegracion` is telemetry outside the business write, so its failure never rolls back a
committed document.

`estado_integracion.py` gains a required `nombre: str` parameter (`WHERE Nombre = ?`, parameterized —
never interpolated), and the `rowcount != 1` guard now also catches a name outside
`CK_EstadoIntegracion_Nombre`'s seven values, since the `UPDATE` would touch 0 rows. Breaking change
to item #4's two call sites plus its unit test; a defaulted `nombre='SBS'` was rejected because it
hides at the call site which integration is being stamped.

## Data Flow

    fact.Configuracion (SELECT, usr_worker)          SMARTNET_WORKER_GMAIL_CREDENTIALS (env)
      ETIQUETA_ORIGEN / _PROCESADO / FECHA_INICIO      │ Credentials.from_authorized_user_info
      EXTENSIONES_PERMITIDAS                           ▼
              │                                    ClienteGmail  (IO puro de red)
              ▼                                        │ labels.list → ids (Decision 3)
      construir_consulta(...)  puro ───────────────────┤ messages.list (paginado)
      "label:X -label:Y after:aaaa/mm/dd"               │ messages.get(format=full)
                                                        ▼
                              parsear_mensaje(json)  puro → MensajeGmail{ adjuntos[] }
                                                        │ es_candidato(nombre, extensiones)  puro
                                            ┌───────────┴───────────┐
                                     sin candidatos            con candidatos
                                     (sin fila, sin etiqueta)        │
                                                                     ▼
                                        BEGIN ─ insertar_email ──► UQ_Email_GmailMessageId
                                                  │ None = ya ingestado (salta descarga)
                                                  ▼
                                        attachments.get → bytes ─► calcular_hash / ruta_relativa (puros)
                                                  │                        │
                                                  │                        ▼
                                                  │           almacenamiento.escribir(raiz, ruta, bytes)
                                                  ▼                   <raiz>/aaaa/MM/<msgId>/<nombre>_<h8>.<ext>
                                        insertar_documento(...)  Estado='DESCARGADO'
                                                  ▼
                                              COMMIT ──► messages.modify (+ETIQUETA_PROCESADO, nunca delete)
                                                  │
                                                  ▼
                                    fact.EstadoIntegracion (Nombre='GMAIL', UPDATE, rowcount=1)

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/worker/src/smartnet_worker/gmail.py` | Create | **Puro**: `construir_consulta`, `parsear_mensaje`, `extensiones_permitidas`, `es_candidato`, `calcular_hash`, `sanitizar_nombre_archivo`, `ruta_relativa`, `ParseoGmailError` |
| `.../gmail_client.py` | Create | **IO**: `ClienteGmail` — `resolver_etiquetas`, `buscar_mensajes` (paginado), `obtener_mensaje`, `obtener_adjunto`, `aplicar_etiqueta`. Sin decisiones |
| `.../almacenamiento.py` | Create | **IO**: `escribir(raiz, ruta_relativa, datos)` + guarda de contención (`is_relative_to`) |
| `.../documento_repo.py` | Create | `insertar_email(cursor, ...) -> int \| None`, `insertar_documento(cursor, email_id, ...)`; SQL parametrizado, `fact.` calificado |
| `.../cli_gmail.py` | Create | Único orquestador: config → cliente → por mensaje una transacción → etiqueta → `EstadoIntegracion` |
| `.../config.py` | Modify | `GMAIL_CREDENTIALS_ENV_VAR`, `STORAGE_ROOT_ENV_VAR`, `GMAIL_SCOPES`, dos getters que lanzan `ConfiguracionError` |
| `.../estado_integracion.py` | Modify | `nombre: str` obligatorio; `WHERE Nombre = ?` |
| `.../cli_tipo_cambio.py` | Modify | Pasa `'SBS'` explícito (consecuencia de la firma nueva) |
| `SmartNet/worker/pyproject.toml` | Modify | `google-api-python-client>=2.140`, `google-auth>=2.34`; script `smartnet-gmail` |
| `SmartNet/worker/tests/unit/test_gmail.py` | Create | Suite pura (parseo, candidatura, hash, saneado, rutas) |
| `.../tests/unit/test_documento_repo.py` | Create | Cursor falso: SQL y parámetros exactos, `IntegrityError → None` |
| `.../tests/unit/test_cli_gmail.py` | Create | `ClienteGmail` falso + cursor falso: orden etiqueta-tras-commit, aislamiento de fallos |
| `.../tests/fixtures/gmail_mensaje*.json` | Create | Respuestas reales de `messages.get` **redactadas** (direcciones y ids reales son PII) |
| `.../tests/unit/test_estado_integracion.py` | Modify | Firma nueva + caso `Nombre='GMAIL'` |
| `.../tests/unit/test_no_dbo_structural.py` | Modify | Docstring + dos escaneos nuevos (sin `.delete(`/`.trash(`, sin tablas de .NET) |
| `.../tests/integration/test_pyodbc_integracion.py` | Modify | `usr_worker` real: Email duplicado, DocumentoRecibido, `EstadoIntegracion` GMAIL, `Configuracion` de solo lectura |
| `SmartNet/db/schema/013_configuracion_etiqueta_procesado.sql` | Create | Una clave `NOT EXISTS`-guardada **+** `UNIQUE (EmailId, HashContenido)` en `DocumentoRecibido`, `IF NOT EXISTS`-guardado (contenido abajo) |
| `SmartNet/db/schema/rollback/013_down.sql` | Create | `DELETE` de esa clave + `DROP CONSTRAINT` del UNIQUE, advisory, con la nota "CANNOT UNDO" de 009 |
| `SmartNet/db/runner/.../BaseDataTests.cs` | Modify | `+[InlineData("INGESTA", "ETIQUETA_PROCESADO")]` |
| `SmartNet/worker/README.md` | Modify | Dos variables de entorno nuevas y el comando `smartnet-gmail` |
| `.github/workflows/ci.yml` | **No change** | `pytest` ya descubre `tests/`; DbUp ya aplica todo `schema/*.sql` en orden léxico |

## Interfaces / Contracts

```python
# gmail.py — puro: ni red, ni disco, ni DB, ni reloj.
@dataclass(frozen=True)
class AdjuntoGmail:
    nombre: str; extension: str; mime_type: str; attachment_id: str; tamano_bytes: int

@dataclass(frozen=True)
class MensajeGmail:
    gmail_message_id: str; remitente: str; asunto: str | None
    fecha_recepcion: datetime            # internalDate (epoch ms UTC), nunca el reloj
    adjuntos: tuple[AdjuntoGmail, ...]   # recorrido recursivo de payload.parts

def construir_consulta(origen: str, procesado: str, desde: date) -> str   # after:aaaa/mm/dd
def parsear_mensaje(payload: dict) -> MensajeGmail                        # ParseoGmailError
def extensiones_permitidas(texto: str) -> frozenset[str]                  # "pdf,xml"
def es_candidato(nombre: str, permitidas: frozenset[str]) -> bool         # última extensión
def calcular_hash(datos: bytes) -> str                                    # sha256 hex, 64
def sanitizar_nombre_archivo(nombre: str) -> str
def ruta_relativa(m: MensajeGmail, a: AdjuntoGmail, hash_hex: str) -> str

# documento_repo.py — recibe cursor, igual que tipo_cambio_repo.py.
def insertar_email(cursor, m: MensajeGmail, fecha_deteccion: datetime) -> int | None
def insertar_documento(cursor, email_id: int, m: MensajeGmail, a: AdjuntoGmail,
                       hash_hex: str, ruta_relativa: str) -> None

# estado_integracion.py — generalizado.
def registrar_exito(cursor, nombre: str, instante: datetime) -> None
def registrar_fallo(cursor, nombre: str, instante: datetime, error: str) -> None
```

| Seam (port) | Shape | Substituted in tests by | Consumer |
|---|---|---|---|
| Gmail | `ClienteGmail` — 5 métodos, sin ramas | `ClienteGmailFalso` que registra llamadas | `cli_gmail.py` |
| SQL Server | `cursor` de pyodbc | cursor falso que registra sentencia y parámetros (patrón #4) | `documento_repo`, `estado_integracion` |
| Volumen compartido | `escribir(raiz, ruta, datos)` | `tmp_path` de pytest | `cli_gmail.py` |
| Reloj | `instante` / `fecha_deteccion` como parámetro | valor fijo | todo el paquete |

`Estado` literals written by this item: `Email.Estado='CANDIDATO'` (ingested, awaiting #6, which
moves it to `PROCESADO`) and `DocumentoRecibido.Estado='DESCARGADO'`. `TipoDocumento` stays `NULL`
— identifying XML vs PDF is ADR 0017's *procesamiento* stage, item #6.

### Migration content (`013_configuracion_etiqueta_procesado.sql`)

```sql
-- 013_configuracion_etiqueta_procesado.sql
-- Una sola clave nueva en fact.Configuracion (BACKLOG #5). 009_datos_base.sql sembro las otras
-- cuatro claves INGESTA a partir de TECH-DESIGN.md linea 307, que nombra "carpeta o etiqueta
-- monitoreada, extensiones permitidas, frecuencia de sondeo y fecha de inicio" -- pero no la
-- etiqueta propia que ADR 0017 ("Escritura en Gmail") manda aplicar al correo ya ingestado y que
-- el tercer termino de su consulta acotada excluye. Es un hueco real del esquema, no una decision
-- de este item. NOT EXISTS-guardado, igual que 009: reaplicar es un no-op.
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'ETIQUETA_PROCESADO')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('INGESTA', 'ETIQUETA_PROCESADO', 'TEXTO', NULL, NULL,
            N'Etiqueta propia que el worker aplica al correo ya ingestado y que la consulta acotada excluye (ADR 0017). Ningun documento fija su nombre; debe existir en Gmail antes del primer sondeo.');

-- Indice unico de identidad que ADR 0010 da por existente para la reproceso idempotente de
-- adjuntos (por decision explicita del usuario, agregado aqui en vez de diferirse al item #6, para
-- no dejarle una dependencia de esquema no declarada). No aplica a Email: ese ya tiene
-- UQ_Email_GmailMessageId desde 003_ingesta_y_procesamiento.sql.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_DocumentoRecibido_Email_Hash' AND object_id = OBJECT_ID('fact.DocumentoRecibido')
)
    ALTER TABLE fact.DocumentoRecibido
        ADD CONSTRAINT UQ_DocumentoRecibido_Email_Hash UNIQUE (EmailId, HashContenido);
```

`Valor`/`ValorPorDefecto` both `NULL` on purpose — 009's rule: a key no document decides is seeded
pending so an unconfigured system fails visibly instead of running on an invented label name.
`Descripcion` is `NVARCHAR(200) NOT NULL`; the text above is 183 characters.

`UQ_DocumentoRecibido_Email_Hash` makes `insertar_documento` symmetrical with `insertar_email`: both
catch `pyodbc.IntegrityError` and treat the duplicate as a no-op rather than an error, per Decision 4
above.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit — `gmail.py` (puro) | `construir_consulta` con etiqueta con espacios (`label:"Facturas 2026"`) y `after:aaaa/mm/dd`; `parsear_mensaje` sobre respuesta real redactada, incluido `multipart` anidado; `internalDate` → UTC; `From` ausente → `ParseoGmailError`; `Asunto` >500 truncado | Fixtures JSON, sin red ni DB |
| Unit — candidatura | `pdf` ✓, `.PDF` ✓, `docx` ✗, `factura.pdf.exe` ✗, sin extensión ✗, `filename` vacío ✗, lista con espacios/puntos/vacíos | Tabla de casos |
| Unit — saneado y rutas (**adversarial**) | `../../etc/passwd`, `..`, `.`, `....`, `C:\x`, `a:b`, `CON.pdf`, `NUL`, nombre de 300 chars, nombre solo-emoji, dos adjuntos mismo nombre distinto contenido → rutas distintas; longitud de ruta ≤ 400 y de componente ≤ 255 | Propiedades explícitas, RED antes del código |
| Unit — `calcular_hash` | Vector conocido (`sha256(b"")`), 64 chars, minúsculas | Directo |
| Unit — `documento_repo` (cursor falso) | SQL y parámetros exactos; `'CANDIDATO'`/`'DESCARGADO'` literales; `IntegrityError → None`; `SCOPE_IDENTITY()` leído del mismo cursor | Patrón `test_tipo_cambio_repo.py` |
| Unit — `cli_gmail` (cliente y cursor falsos) | Etiqueta **solo** tras commit; mensaje fallido no etiquetado y no aborta el run; mensaje sin candidatos → 0 filas, 0 escrituras; `insertar_email → None` → ninguna descarga y etiqueta reaplicada; etiqueta inexistente → falla antes de `messages.list` | Fakes, sin red |
| Unit — `estado_integracion` | `Nombre='GMAIL'`; `rowcount != 1` lanza; nombre fuera del CHECK lanza | Cursor falso |
| Estructural | Ningún módulo menciona `dbo.` (test existente, se amplía el docstring); ninguno menciona `.delete(`/`.trash(` (ADR 0017 nunca borra); ninguno menciona `fact.Factura`/`fact.AdjuntoManual`/`fact.Procesamiento`/`fact.DatosExtraidos` | Escaneo literal del `src/`, patrón `test_no_dbo_structural.py` |
| Integration (`integracion`) | `usr_worker` real: `insertar_email` inserta y el duplicado devuelve `None` por `UQ_Email_GmailMessageId`; `insertar_documento` con FK real; `UPDATE` de `EstadoIntegracion` GMAIL afecta 1 fila; **negativa**: `UPDATE fact.Configuracion` falla (008 da SELECT solo) | Job `pruebas-de-worker-python` existente |
| Integration (.NET) | `BaseDataTests` — `INGESTA.ETIQUETA_PROCESADO` existe con `Valor`/`ValorPorDefecto` `NULL` | `+1 InlineData` |
| **Excluido de CI** (`externa`) | Round-trip real contra una cuenta Gmail de prueba | Marker existente. Un CI rojo por una cuota de Google no dice nada sobre nuestro cambio |

## Threat Matrix

| Boundary | Applicability | Design response | Planned RED tests |
|---|---|---|---|
| Documentation-like paths / **classification of an untrusted filename into a filesystem path** | **Applicable** — the attachment name is attacker-controlled (anyone can email the inbox) and it decides both candidacy and a write path | Extension = final suffix only, lowercased, against the allow-list (never a substring match); `sanitizar_nombre_archivo` whitelists `[A-Za-z0-9._-]`; the mandatory `_<hash8>` suffix makes `.`/`..` components unreachable; `escribir` re-asserts containment under the configured root; the written file is never executed, opened or classified by content | The adversarial saneado/rutas row above: traversal, reserved device names, double extension, overlength, empty-after-saneado |
| Git repository selection | N/A | No component invokes `git` | — |
| Commit state | N/A | No VCS automation | — |
| Push state | N/A | No VCS automation | — |
| PR commands | N/A | No PR automation | — |

Beyond the matrix, the two real new boundaries: **credentials** (no default in code; a single env
var; the access token never touches disk; a `RefreshError` fails the run visibly instead of retrying
blind) and **an outbound write to a third-party system** (`messages.modify` adding one label — the
only mutating Gmail call in the package, structurally asserted).

## Migration / Rollout

Additive. One new versioned SQL script picked up automatically by DbUp in lexical order (011, 012
exist ⇒ 013), `NOT EXISTS`-guarded so reapplication is a no-op; `rollback/013_down.sql` is advisory
and deletes exactly that one key. No table, column, grant or index changes. Deployment prerequisites,
all operational: create the two Gmail labels, set `SMARTNET_WORKER_GMAIL_CREDENTIALS` and
`SMARTNET_WORKER_STORAGE_ROOT`, fill the five `INGESTA` keys, and schedule `smartnet-gmail`
(`FRECUENCIA_SONDEO_MINUTOS` is read by the operator configuring cron, not by this code). Rollback:
revert the commit and promote `013_down.sql`.

## Open Questions — resueltas

- **Formato de `after:`**: `spec.md` corregido a `after:2026/01/01` (formato real de Gmail); la
  design ya emitía la forma correcta, era el ejemplo del spec el que estaba mal — corregido en vez
  de silenciado (regla del proyecto 1).
- **Mensajes con etiqueta de origen pero cero adjuntos permitidos**: no se etiquetan como
  procesados. Ya era el comportamiento diseñado (Decision 3/7, data flow "sin candidatos → sin
  fila, sin etiqueta") — confirmado explícitamente por el usuario.
- **Índice único de identidad de ADR 0010**: se agrega **ahora**, no se difiere a #6.
  `UNIQUE (EmailId, HashContenido)` en `DocumentoRecibido`, vía la misma migración 013 (ver
  Decision 4 y "Migration content" arriba) — revisa la afirmación de `proposal.md` de "sin DDL
  nueva para `Email`/`DocumentoRecibido`", que queda desactualizada por esta decisión explícita.
- **Sin tope de tamaño de adjunto**: riesgo acotado aceptado tal como estaba propuesto (el límite
  de 25MB de Gmail es el tope de facto); no se inventa una clave de `Configuracion` que ADR 0013
  no autorizó.
