# Design: Esquema y Permisos (BACKLOG #1)

> **Size note.** This artifact deliberately exceeds the 800-word SDD budget. The phase brief requires
> ~15 column-type decisions each carried with a visible rationale, plus four architecture decisions.
> Compressing them would reproduce the exact failure mode the brief names: a silent default that
> sixteen downstream items inherit.

## Technical Approach

Nine numbered plain-SQL scripts under `SmartNet/db/schema/`, applied in lexical order by a **DbUp**
runner hosted in a standalone .NET console project, before the API and before the worker (ADR 0012).
Structure, permissions and base data are separate scripts. Nothing outside schema `fact` is created;
the five `dbo.*` catalogs (`Proveedor`, `CuentaContable`, `Motivo`, `Origen`, `DocumentoIdentidad`)
are referenced by object-level `GRANT SELECT` only. `DocumentoIdentidad` was added after this
document's first draft — a real external catalog the user loaded, and the FK target of
`dbo.Proveedor.coddocide` — via Work Unit 3's coordinator-directed follow-up; not otherwise part of
this Decision's own reasoning, so it is noted here only for count accuracy, not re-argued.

The SQL is the authoritative type definition both runtimes derive from (ADR 0016). Therefore every
type left unstated by the documents is decided **here**, in writing, with its cost — not at authoring
time.

---

## Decision 1 — Schema-versioning tool: DbUp

| Option | What it adds to the deploy chain | Verdict |
|---|---|---|
| **DbUp** | A ~60-line .NET console project in a repo that already builds .NET. Publishes as a self-contained executable. No new runtime. | **Chosen** |
| Flyway (Community) | A JVM, or the ~200 MB bundled-JRE distribution, in a Windows/SQL Server shop with zero Java. Buys checksums and `undo` (paid tier only). | Rejected |
| SSDT / DACPAC (`sqlpackage`) | .NET-native, no new runtime — but it is **state-based**: the deployed change is a computed diff, not a reviewed script. ADR 0016's whole value is a change reviewable in a `diff`. It also defaults to whole-database ownership and needs extensive exclusion config to avoid touching `dbo`. | Rejected |
| Hand-applied scripts | Nothing. Rejected by ADR 0016 itself. | Rejected |

**On "runtime-neutral".** ADR 0016's neutrality argument is about who *defines* the schema, not what
language the *applier* is written in. The rejected option is "tables defined by C# classes no .NET
code uses". DbUp does not define anything: the scripts are plain SQL, ordered and reviewable, and
Python derives its types from the same bytes. To keep the appearance honest, the runner lives at
`SmartNet/db/runner/` (`SmartNet.Db.Runner`), **not** under `SmartNet/api/`, and references no domain
project.

### Costs of DbUp, stated

1. **No checksums.** DbUp's journal (`SchemaVersions`) records script name and applied date only. An
   edit to an already-applied script is **silently ignored** — no rerun, no error. Flyway would fail
   the deploy. Mitigation: `SmartNet/db/schema/checksums.txt` committed alongside, and a CI step that
   re-hashes every `*.sql` and fails the build on any change to a file already listed. Applied scripts
   are immutable by review rule; the CI step is what makes the rule bite.
2. **No down-scripts, at all.** DbUp has no `undo` concept. See Decision 4 — this is why rollback is
   forward-only here. It is the single largest cost of this choice, and it is partly moot: ADR 0016's
   backward-compatibility rule already made most rollbacks forward-only.
3. **A runner to build and version.** Flyway ships a CLI; DbUp does not. One more artifact in the
   pipeline, with its own publish step.
4. **The tool cannot scope itself to `fact`.** Neither can Flyway — schema scoping is not a property
   any migration tool has. Enforcement is pushed to two places that *can* express it: (a) the deploy
   principal holds `ALTER ON SCHEMA::fact` and no permission on `dbo` objects, so a stray `CREATE
   TABLE dbo.X` fails at the engine; (b) a CI lint rejects any `dbo.` occurrence in the schema scripts
   except in `GRANT SELECT ON OBJECT::dbo.<one of four>`.
5. **Journal location.** DbUp defaults to `dbo.SchemaVersions`. On a shared database that would plant
   a project object in the accounting system's schema. Configure explicitly:
   `.JournalToSqlTable("fact", "SchemaVersions")`.

**Configuration:** `WithTransactionPerScript()` — a failing script leaves nothing half-applied; earlier
scripts stay journalled. **Naming:** `NNN_snake_case.sql`, three-digit zero-padded, because DbUp orders
by ordinal string comparison and padding is what makes lexical order equal numeric order.

**GRANTs are ordinary numbered migrations**, not a bootstrap step. A bootstrap would live outside the
journal — which is precisely the "parallel document that desynchronizes on the first deploy" ADR 0016
rejects. Rule going forward: **a new table's grants ship in the same numbered file as its DDL**, so a
table can never exist without its permissions. For this initial change everything exists before `008`,
so the grants get their own file as ADR 0016 sketched.

**Invocation (ADR 0012 order):** step 1 runs `SmartNet.Db.Runner --connection <deploy-cred>`; a
non-zero exit halts the deployment before the API artifact is touched.

---

## Decision 2 — The unstated column types

### Six global rules (these collapse ~40 individual judgement calls)

| Rule | Rationale | Cost if wrong |
|---|---|---|
| Surrogate PKs are `BIGINT IDENTITY(1,1)`, uniformly | One rule beats per-table judgement; a mixed INT/BIGINT scheme creates exactly the C#/Python mapping divergence ADR 0002 declares as a cost | 4 bytes/row and wider indexes. Negligible at this volume |
| Technical timestamps are **`DATETIME2(3)`** (UTC) | Milliseconds are representable *exactly* in both C# `DateTime` and Python `datetime`. `DATETIME2(7)` is 100 ns — Python cannot express it and silently truncates, which is a type divergence ADR 0019 level 2 would have to catch | Two events in the same millisecond are not orderable by timestamp. Irrelevant: ordering uses `Secuencia`/identity |
| Enum-like columns are **`VARCHAR(20)`** unless the domain fixes the width | Uniform width means adding a state value never needs a widening migration; `VARCHAR` stores only actual bytes, so the headroom is free | None |
| Enum-like columns carry a **named `CHECK`** | Value sets are normative in the ADRs and both runtimes must agree. A disagreement fails at write in the offending runtime instead of surfacing as a mystery row | Adding a value becomes a two-step deploy — already budgeted by ADR 0016's backward-compatibility rule |
| Free text is **`NVARCHAR`**; codes with a guaranteed ASCII alphabet are `VARCHAR`/`CHAR` | We do **not** control the collation of a shared database. `NVARCHAR` removes the collation dependency for supplier names, subjects and error text | 2 bytes/char on text columns |
| **No explicit `COLLATE`** on any column that joins to `dbo` | Forcing a collation on our side raises "cannot resolve collation conflict" on every join to the external catalogs. Exception: `HashContenido`, which never joins | The comparison semantics of `Numero`/`Ruc` inherit the shared DB collation — see Risks |

### The named decisions

| # | Column(s) | Type | Reason | If it turns out too small |
|---|---|---|---|---|
| 1 | `RucProveedor` (`Factura`, `DatosExtraidos`) | `VARCHAR(11) NULL` + `CHECK (LEN BETWEEN 8 AND 11 AND NOT LIKE '%[^0-9]%')` | **Revised by accounting decision.** The original `CHAR(11)` assumed the emitter is always an 11-digit RUC. It is not: of the 6600 rows in `dbo.Proveedor`, 118 carry an 8-digit DNI and 6 a 9-or-10-digit carné de extranjería, and the user confirmed those emitters are legitimate. `VARCHAR` and never `CHAR`: a fixed-length type pads an 8-digit DNI to 11, which would never equal `dbo.Proveedor.rucpro` (itself `VARCHAR`) and would enter `IX_Factura_Identidad` padded, silently defeating duplicate detection. Still an **identifier** — leading zeros are data, no arithmetic. Nullable because "campos no extraídos" is an explicitly supported state. **Not an FK to `dbo.Proveedor`** — it is a frozen copy | A 124-supplier blind spot, closed |
| 2 | `Factura.Numero`, `AsientoContable.NumeroComprobante` | `VARCHAR(20)` | SUNAT serie is 4 chars + `-` + up to 8 digits = 13. `VARCHAR` because issuers do not always pad the correlativo. **Kept as one column**, not split, because `IX_Factura_Identidad` is defined over `Numero` and the value is compared as a printed string against the physical document. `Factura.Numero` is `NULL`-able — the un-extracted-number case is normative (TECH-DESIGN) | Widening a `VARCHAR` is metadata-only, but this one sits in `IX_Factura_Identidad` → index rebuild. Cheap at this volume |
| 3 | `RefExternaSerie` / `RefExternaNumero` | `VARCHAR(4)` / `VARCHAR(15)` | The documents split the *external* reference while keeping the internal one whole. That asymmetry is honoured, not "fixed" (see Open Questions) | Trivially widened; not indexed |
| 4 | `TipoComprobante`, `OrigenLibro` | `CHAR(2)` | SUNAT catálogo-01 codes and libro origins are exactly two digits with a significant leading zero (`01`, `03`, `07`, `02`) | Cannot happen |
| 5 | `Moneda` | `CHAR(3)` + `CHECK (Moneda LIKE '[A-Z][A-Z][A-Z]')` | ISO 4217 alpha-3, fixed. The CHECK constrains **format, not membership** — the accepted currency set is a business decision no document states, and hardcoding `('PEN','USD')` would invent one | Cannot happen |
| 6 | `CuentaCodigo`, `CtaReflejaCodigo`, `CtaPuenteCodigo` | `VARCHAR(10)` | The one type ADR 0011 actually states. `VARCHAR`, **not `CHAR`**, is load-bearing: `dbo.Motivo` stores prefixes of 2–6 digits and the leaves are 6, and `CHAR` would pad prefixes with trailing spaces and break `LIKE prefix + '%'`. **No FK to `dbo.CuentaContable`** — ADR 0006 freezes these values so they must survive the external account being renumbered or deleted; an FK would make freezing meaningless and could block the accounting system's own deletes | 10 is 66% headroom over the real 6. Widening is metadata-only except in `SugerenciaCuenta`'s PK → index rebuild |
| 7 | Five `Factura` indicators: `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`, `FechaEnDomingo`, `EsReferenciaExterna` | `BIT NOT NULL DEFAULT 0` | `BIT` packs 8 per byte and maps to C# `bool` / Python `bool` with no sentinel | n/a |
| 8 | `Factura.AfectacionMixta` | **`BIT NULL`, no `DEFAULT`** | Three-state per ADR 0017: `1` = XML declares >1 afectación code (reject at validate), `0` = verified, `NULL` = no XML, **unverified**. `NULL` is a first-class third state in a `BIT` column; a `CHAR(1)` `'S'/'N'/'X'` scheme was rejected for inventing a sentinel the engine already provides. **No `DEFAULT`**, so the column can only be set deliberately | n/a — but see the sixth indicator below |
| 8b | "afectación no verificada" (the sixth indicator) | **Not stored** | It is exactly `AfectacionMixta IS NULL`. Storing it would create a second source of truth that can disagree with the first. Note the useful asymmetry: a naive filter `WHERE AfectacionMixta = 0` **excludes** unverified rows rather than admitting them — the failure mode is a missing row, not a permissive read | n/a |
| 9 | `OutboxEvent.Payload`, `CommandQueue.Payload`, `InboxEvent.Payload` | `NVARCHAR(MAX)` | Not `VARBINARY` (unreadable in a query window, and ADR 0016's legibility argument applies to data too), not the SQL Server `json` type (2025+ only; the shared instance's version is not ours to assume). `NVARCHAR` because payloads carry supplier names and glosas with accents | `MAX` has no ceiling |
| 9b | JSON validation on those payloads | **Consumer-enforced. No `ISJSON` CHECK** | (a) The CHECK would run inside the business transaction ADR 0004 requires to succeed atomically; (b) `ISJSON` proves syntax, never **self-sufficiency**, which is ADR 0004's actual requirement — so it buys little while adding a failure mode in the hot path; (c) the real contract is the payload *schema*, and ADR 0019 level 2 verifies it from both runtimes, which is where the check belongs. **Cost:** a malformed payload is detected only at consumption, as a poison message; ADR 0010's `PERMANENTE` class is its disposal path. Adding `CHECK (ISJSON(Payload)=1) WITH NOCHECK` later is a cheap forward migration — which is why not doing it now is safe | n/a |
| 10 | `OutboxEvent.Secuencia` | `BIGINT NOT NULL`, fed from `SEQUENCE fact.SeqOutbox` | Monotonic per aggregate is implied by globally monotonic, so no counter table is needed. The `SEQUENCE`/`IDENTITY` objection of ADR 0006 does **not** transfer: it exists because a burned *accounting correlative* is a hole in a fiscal series; a gap in the outbox sequence is harmless. `BIGINT` maps to C# `long` and Python `int` without ambiguity. **Downstream consequence for item #16:** the sequence column written to Google Sheets must be written as **text**, because Sheets stores numbers as IEEE-754 doubles and would silently round above 2^53 | n/a |
| 11 | `Configuracion` | New table, key/value with declared type — see below | see below | see below |
| 12 | `AdjuntoManual` | New table — see below | see below | see below |
| 13 | `AuditoriaCorreccion` | New table — see below | see below | see below |
| 14 | `HashContenido` | `CHAR(64) COLLATE Latin1_General_100_BIN2` | SHA-256 hex, fixed 64, ASCII. **Choosing 64 is a de facto decision that the hash is SHA-256** — no document states the algorithm; if item #5 chooses another, this column changes. `VARBINARY(32)` was the alternative (half the storage, no encoding question) and was rejected for legibility: a human comparing two hashes in a query window is a real diagnostic path in this system. `BIN2` is safe here because this column never joins to `dbo` | Cannot happen for SHA-256 |
| 15 | `Percepcion` (`Factura.PercepcionOrig`) | `DECIMAL(18,2)` | It is an **amount**, not a rate — `REGLAS.md` §10.4: "Percepción 23.60", abonada al proveedor como `total + percepción`. Money rule applies | n/a |
| 16 | `Factura` amounts | `TotalOrig`, `IgvOrig`, `PercepcionOrig` — all `DECIMAL(18,2)`; `TipoCambioAplicado DECIMAL(12,6)` | `IgvOrig` is not named in TECH-DESIGN's prose but is required by `REGLAS.md` §6's derivation (base = total − IGV) and appears in the `FacturaExtraccion.CampoNombre` set (`'igv'`). The `Orig` suffix is CONVENTIONS.md's multi-currency rule applied at the exact boundary where conversion happens: `Factura` carries original currency, `AsientoContable` carries `BasePEN`/`IgvPEN`/`NetoPEN` | n/a |
| 17 | `Version` on `Factura` and `AsientoContable` | `rowversion` | The type name, not the deprecated `timestamp` synonym. It cannot be inserted or updated explicitly, so it must be excluded from every column list; it maps to `byte[8]` / `bytes` and is hex-encoded for the `If-Match`/ETag of item #11 | n/a |
| 18 | `CorrelativoAsiento` | `Anio SMALLINT`, `Mes TINYINT`, `Origen CHAR(2)`, `Ultimo INT`, PK `(Anio, Mes, Origen)` + `CHECK (Mes BETWEEN 1 AND 12)` | As stated by TECH-DESIGN/ADR 0006. `TINYINT` maps to C# `byte`; the CHECK is what makes the monthly-reset key self-describing | n/a |
| 19 | `AsientoContableDetalle.Debe` / `Haber` | `DECIMAL(18,2) **NOT NULL** DEFAULT 0` | Not a style choice: `CK_Linea_Tipo` compares `Haber = 0`, and a `NULL` makes the predicate `UNKNOWN`, which **rejects the row**. `NOT NULL DEFAULT 0` is required for the stated constraint to behave as written | n/a |
| 20 | `ProcesamientoIntentos.NumeroIntento`, `Orden`, `Intentos` | `SMALLINT` | `TINYINT` (0–255) would also fit but maps to C# `byte` while Python has no unsigned type; `SMALLINT` → C# `short` is the cleaner two-runtime mapping | n/a |
| 21 | `CommandQueue.CorrelationId` | `UNIQUEIDENTIFIER` | Generated by the API for a request that has not touched the database yet, and travelling into logs across two runtimes. Maps to C# `Guid` / Python `uuid.UUID` natively; 16 bytes vs 36 as text | n/a |
| 22 | `Email.GmailMessageId` | `VARCHAR(32)` + `UNIQUE` | Opaque Google-controlled hex id (currently 16 chars); it is the **idempotency key of ingestion**, hence unique. 32 is 2× headroom on a format we cannot influence | Widening is metadata-only but rebuilds the unique index |
| 23 | `Email.Remitente` | `NVARCHAR(320)` | RFC 5321: 64 local + `@` + 255 domain. Non-arbitrary | Cannot happen |
| 24 | `Email.Asunto` | `NVARCHAR(500)` | Subjects are unbounded in principle. **Truncation here is acceptable** and must be done deliberately by the application, because the value is display-only | Truncate on write, by design |
| 25 | Error/diagnostic text (`ProcesamientoError.Mensaje`, `UltimoError`, `Detalle`) | `NVARCHAR(2000)` | Google API exception text is long; 2000 is generous and stays off `MAX`, so the row stays in-page. Truncation is acceptable and deliberate, same as `Asunto` | Truncate on write, by design |
| 26 | File metadata (`NombreArchivo`, `RutaRelativa`, `MimeType`, `TamanoBytes`) | `NVARCHAR(255)`, `NVARCHAR(400)`, `VARCHAR(100)`, `BIGINT` | 255 is the practical single-component limit on NTFS and Drive. **400 for `RutaRelativa` is deliberate**: it is relative to a configurable root (ADR 0013), and `NVARCHAR(400)` = 800 bytes stays **under SQL Server's 900-byte index key limit**, so a unique index on the path remains possible. `TamanoBytes BIGINT` because `INT` caps at 2 GiB and the max size is configurable. `Tamano`, not `Tamaño` — CONVENTIONS.md forbids `ñ` in identifiers | Widening `RutaRelativa` past 450 chars forfeits indexability — the one length here that is *not* free to widen |
| 27 | `Usuario` credential | **One column: `ClaveHash NVARCHAR(200)` in PHC string format** | ADR 0007 permits Argon2id **or** PBKDF2 and does not choose. A PHC-encoded string (`$argon2id$v=19$m=…,t=…,p=…$salt$hash`) stores algorithm, parameters and salt **with** the hash, so changing algorithm or raising the work factor needs no schema change, and a mixed population during rotation is representable. Separate `Hash`/`Sal`/`Algoritmo` columns were rejected for pinning the shape. **Cost:** the database cannot constrain the format; the parser lives in .NET. **`BloqueadoHasta` is `DATETIME2(3)`** — a precision-only deviation from ADR 0007's snippet, which wrote the unqualified default | n/a |
| 28 | `AsientoContable.Glosa`, `MotivoDescripcion` | `NVARCHAR(250)`, `NVARCHAR(120)` | No document states either. Motivo names in `MOTIVOS-CLASIFICACION.md` top out around 30 chars; 120 is 4× headroom on a frozen copy. Glosa is a free line of accounting narrative | Both trivially widened; neither is indexed |

### `Configuracion` — designed here (no schema existed)

```
fact.Configuracion
  Seccion          VARCHAR(30)   NOT NULL
  Clave            VARCHAR(60)   NOT NULL
  Tipo             VARCHAR(20)   NOT NULL  CHECK IN ('TEXTO','ENTERO','DECIMAL','BOOLEANO','FECHA','LISTA')
  Valor            NVARCHAR(400) NULL      -- canonical text form; NULL = use default
  ValorPorDefecto  NVARCHAR(400) NULL
  Descripcion      NVARCHAR(200) NOT NULL
  ActualizadoPorUsuarioId BIGINT NULL FK -> fact.Usuario
  ActualizadoEn    DATETIME2(3)  NULL
  PRIMARY KEY (Seccion, Clave)
```

**Choice:** one key/value table with a section and a declared type. **Rejected:** a table per section
with typed columns — every new setting would then be a schema migration plus a coordinated deploy of
both runtimes under ADR 0016's backward-compatibility rule, and the settings list is explicitly still
growing (ADR 0013 leaves attachment types and max size *pendiente*). A one-row table per section is
also a table pretending to be a struct. **"Tipada por secciones" is preserved as an application-level
contract**: .NET exposes one typed settings object per section, parsed and validated from these rows.
**Cost, stated:** the engine does not enforce that `Valor` matches `Tipo`; a bad value surfaces in the
settings parser, not at write. Accepted because the failure lands on a configuration screen, not on a
money path. `LISTA` means a JSON array of strings, parsed by the same layer — consistent with the
payload decision.

### `AdjuntoManual` and `AuditoriaCorreccion` — designed here (narrative only, no DDL anywhere)

```
fact.AdjuntoManual
  AdjuntoManualId  BIGINT IDENTITY PK
  FacturaId        BIGINT NOT NULL      FK -> fact.Factura
  NombreArchivo    NVARCHAR(255) NOT NULL
  RutaRelativa     NVARCHAR(400) NOT NULL
  MimeType         VARCHAR(100)  NOT NULL
  TamanoBytes      BIGINT        NOT NULL
  SubidoPorUsuarioId BIGINT      NOT NULL FK -> fact.Usuario
  SubidoEn         DATETIME2(3)  NOT NULL
  EliminadoEn      DATETIME2(3)  NULL
  EliminadoPorUsuarioId BIGINT   NULL FK -> fact.Usuario
  MotivoEliminacion NVARCHAR(300) NULL
  CHECK (all three deletion columns NULL, or all three NOT NULL)
```

The three-way CHECK is what makes ADR 0013's "borrado lógico auditado" a property of the row rather
than a hope: a deletion without a recorded author or reason cannot be stored.

```
fact.AuditoriaCorreccion
  AuditoriaCorreccionId BIGINT IDENTITY PK
  EntidadTipo   VARCHAR(20)  NOT NULL  CHECK IN ('FACTURA','ASIENTO','ADJUNTO')
  EntidadId     BIGINT       NOT NULL  -- deliberately NOT a foreign key
  Accion        VARCHAR(30)  NOT NULL  CHECK IN ('CORRECCION','REAPERTURA','ANULACION',
                                                 'TRASLADO_PERIODO','CONFIRMACION_AFECTACION',
                                                 'ELIMINACION_ADJUNTO','REPARTO_MANUAL')
  Campo         NVARCHAR(60)  NULL     -- NULL for whole-act actions
  ValorOriginal NVARCHAR(1000) NULL
  ValorNuevo    NVARCHAR(1000) NULL
  Motivo        NVARCHAR(300) NULL
  UsuarioId     BIGINT NOT NULL FK -> fact.Usuario
  OcurridoEn    DATETIME2(3) NOT NULL
```

`EntidadId` is **intentionally not an FK**: the table is polymorphic across three entities, and one
nullable FK column per entity plus a discriminating CHECK is disproportionate for an append-only log
that is never joined in a hot path. **Cost:** nothing prevents an orphan `EntidadId`; acceptable
because rows are written inside the same transaction as the change they record. The seven `Accion`
values are the four TECH-DESIGN v2 cases plus the three base ones — the CHECK is what stops an act
being recorded under an ad-hoc label.

`ValorOriginal`/`ValorNuevo` at **1000**, wider than any value they hold, because this is the one
place where "too small" is *not* cheap: a truncated audit value is a silent falsification, invisible
after the fact. The writer must fail rather than truncate — the opposite rule from `Asunto`.

### `OutboxEvent` per-integration state — a child table

ADR 0004 requires "estado independiente por integración". **Choice:** `fact.OutboxEventIntegracion`
with PK `(OutboxEventId, Integracion)`, carrying `Estado`, `Intentos`, `ProximoIntentoEn`,
`UltimoError`, `ActualizadoEn`. **Rejected:** four column groups on the outbox row — the integration
set is configuration-driven (Drive, Sheets, Telegram, correo, with a mail fallback per item #17), and
a fifth integration must not be a schema change to the outbox itself.

**Permission consequence, and it refines ADR 0003's matrix:** `usr_api` gets `INSERT`/`SELECT` on both
parent and child (it inserts one child row per configured integration inside the same transaction);
`usr_worker` gets `SELECT` on the parent, `UPDATE` on the parent's global state, and `SELECT`/`UPDATE`
on the child. This is a table ADR 0003's matrix does not name; the matrix is extended, not
contradicted. The spec phase must carry it.

---

## Decision 3 — How the two database users are created

**Preferred: DBA-created login at instance level, plus user, role and grants in the versioned SQL.**

The boundary is exactly the security boundary: `CREATE LOGIN` is instance-level and **carries a
password**, so it stays out of the repo — which is the same rule that keeps the `Usuario` row out.
`CREATE USER … FOR LOGIN`, role membership and every `GRANT`/`DENY` are database-level and travel in
`008_usuarios_y_permisos.sql`, satisfying ADR 0016.

**Contained database users were rejected, and not on preference.** `CREATE USER … WITH PASSWORD`
requires `ALTER DATABASE … SET CONTAINMENT = PARTIAL`. On a shared database that is a change to a
property whose blast radius reaches the co-tenant: in a partially contained database, temporary
objects resolve with the **contained database's** collation rather than tempdb's, which can change the
behaviour of the accounting system's existing code. Changing containment also requires briefly
exclusive access to a database the accounting system is connected to. This project does not get to
make that change. Contained users remain the documented contingency **only if this project is ever
given a database it does not share**.

**What has to be true for the preferred path — a premise about the environment, not a preference:**

1. The instance administrator creates SQL logins `usr_api` and `usr_worker` with passwords this
   project never sees. ADR 0015's secret manager holds the resulting connection strings.
2. The deploy principal holds, in this database: `CREATE SCHEMA`, `ALTER ANY USER`, `ALTER ANY ROLE`
   and `ALTER ON SCHEMA::fact`. `db_owner` covers all of it but is broader than needed on a shared
   database; explicit grants are preferred, `db_owner` accepted if the DBA prefers a role.
3. **The script fails loudly if the premise is unmet.** `008` starts with
   `IF DATABASE_PRINCIPAL_ID('usr_api') IS NULL AND SUSER_ID('usr_api') IS NULL THROW 50001, 'Login
   usr_api no existe: debe crearlo el administrador de la instancia antes de aplicar el esquema.', 1;`
   — a missing login halts the deploy with a sentence an operator can act on, instead of a schema with
   no permissions.

**Grants go to roles, never to users directly.** `008` creates database roles `fact_api` and
`fact_worker`, grants to the roles, and adds the users as members. Three reasons: the permission matrix
becomes a single reviewable object; an environment can use different login names without touching the
grant script; and the ADR 0019 level-2 test can add a throwaway principal to a role (see below).

**Explicit `DENY` on the cross-boundary tables, not merely absent `GRANT`.** ADR 0003's strongest claim
is that `usr_api` **cannot** read `fact.Procesamiento`. Absence of a grant delivers that only until
someone writes `GRANT SELECT ON SCHEMA::fact` — an easy and plausible mistake. `DENY` beats `GRANT`, so
`DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::fact.Procesamiento TO fact_api` (and the symmetric
denies for the Python-private set, and for the full .NET-private bucket ADR 0003 names —
"negocio" + "satélites de datos maestros" + "seguridad" — to `fact_worker`: `fact.Factura`,
`fact.AsientoContable`, `fact.AsientoContableDetalle`, `fact.AdjuntoManual`,
`fact.AuditoriaCorreccion`, `fact.FacturaExtraccion`, `fact.CorrelativoAsiento`,
`fact.ProveedorAtributo`, `fact.MotivoAtributo`, `fact.SugerenciaCuenta`, `fact.Usuario`) makes the
claim hold under a future mistake. **Widened from the first draft**, which named only four of these
eleven tables (`Factura`/`AsientoContable*`/`AdjuntoManual`/`Usuario`); the other six relied on
absence-of-`GRANT` alone. Independent verification (Work Unit 3) found the gap between this
paragraph and the full "Privadas propias de .NET" bucket the same Decision already grants
SELECT/INSERT/UPDATE to `fact_api`, and the user decided to close it — this paragraph and `008` now
agree on the complete eleven-table set. **Cost:** `DENY` is sticky — a later legitimate need
requires remembering to `REVOKE` the deny first, and the error message does not say so.

**`dbo` is granted at object level only** — `GRANT SELECT ON OBJECT::dbo.Proveedor`, and the same for
`CuentaContable`, `Motivo`, `Origen`. Never `GRANT SELECT ON SCHEMA::dbo`, which would expose every
other table of the accounting system. No `INSERT`/`UPDATE`/`DELETE` and no `EXECUTE` anywhere.

`ALTER USER … WITH DEFAULT_SCHEMA = fact` for both, so unqualified names resolve to `fact` first.
ADR 0003's rule that `dbo` references are always written qualified still stands and the CI lint checks
it.

---

## Decision 4 — Rollback

**There is no whole-database rollback path available to this project, and there will not be one.**
Restoring this database to a point in time reverts the company's accounting data as well (ADR 0014
revision 2, consequence 3; adversarial review finding C7). The proposal's first draft offered a
pre-migration snapshot; that option does not exist.

DbUp has no down-script support (Decision 1, cost 2). Rollback is therefore **forward-only**, in three
tiers:

| Failure | Path |
|---|---|
| Script fails mid-apply | `WithTransactionPerScript` rolls that script back entirely. Earlier scripts stay journalled. The runner exits non-zero and the deployment halts **before** the API is deployed (ADR 0012 order). Fix forward, re-run |
| Script applied but wrong | A **new numbered compensating migration**, `NNN_revierte_MMM_<motivo>.sql`. It is scoped to `fact` by the same permission boundary that scopes everything else — the deploy principal has no rights on `dbo`, so a compensating migration cannot reach the accounting system even by mistake |
| Data loss inside `fact` | The only recovery is the instance-level backup, which is not this project's to invoke and would revert the co-tenant. **Therefore: no migration in this project may `DROP` or `TRUNCATE` a table that holds data.** Column removal follows ADR 0016's two-deploy rule; a destructive change must first copy into a `fact.<Tabla>_Respaldo` table inside the schema. This rule is what replaces the impossible restore |

**Advisory down scripts.** Each numbered migration ships a companion `SmartNet/db/schema/rollback/
NNN_down.sql` that the tool **never runs**. It is authored in the same PR, while the knowledge is
fresh, and exists to be reviewed and — if ever needed — promoted verbatim into the next numbered
forward migration, at which point it is executed and journalled like anything else. **Cost:** a file
the tool does not run can rot; the mitigation is that it is a review item in the same PR and that
using it means renumbering it forward.

**One bootstrap exception, and it closes.** While `fact` is empty — this change, before any production
apply — `DROP SCHEMA fact` and re-apply is a legitimate reset, and it is safe *precisely because* it
touches nothing outside `fact`. That window closes at the first production apply and never reopens.

---

## File organisation and numbering

```
SmartNet/db/
  runner/                                  SmartNet.Db.Runner (.NET console, DbUp)
  schema/
    001_esquema_fact.sql                   CREATE SCHEMA fact  (DbUp creates fact.SchemaVersions itself)
    002_seguridad.sql                      Usuario
    003_ingesta_y_procesamiento.sql        Email, DocumentoRecibido, Procesamiento, DatosExtraidos,
                                           ProcesamientoError, ProcesamientoIntentos
    004_satelites_datos_maestros.sql       ProveedorAtributo, MotivoAtributo, SugerenciaCuenta
    005_negocio.sql                        Factura, FacturaExtraccion, AsientoContable,
                                           AsientoContableDetalle, CorrelativoAsiento,
                                           AdjuntoManual, AuditoriaCorreccion
    006_contratos.sql                      OutboxEvent, OutboxEventIntegracion, CommandQueue, InboxEvent
    007_publicacion.sql                    TipoCambio, Configuracion, EstadoIntegracion
    008_usuarios_y_permisos.sql            CREATE USER, roles fact_api/fact_worker, GRANT + DENY matrix
    009_datos_base.sql                     EstadoIntegracion rows, Configuracion defaults
    010_motivo_atributo_demo.sql           the reclassified MotivoAtributo rows
    checksums.txt                          CI-verified hash manifest (compensates DbUp's missing checksums)
    rollback/NNN_down.sql                  advisory, never applied by the tool
  test-bootstrap/                          test-only, never numbered, never run in production
```

**One migration per concern**, following ADR 0016's own sketch. Rejected: **one file for everything** —
it would be the largest reviewable unit in the project, and with transaction-per-script a failure
anywhere leaves the entire schema unapplied behind one opaque error. Rejected: **one file per table**
(~25 files) — it multiplies the journal and forces the FK dependency graph to be encoded in file
names, which is fragile. Concern granularity maps onto ADR 0003's ownership classes, so a reviewer
reads one file per class and checks the permission matrix against it.

**The numbering already satisfies every cross-concern FK**, so no trailing `claves_foraneas.sql` is
needed: `negocio` (005) needs `Usuario` (002) and `Procesamiento` (003); `contratos` (006) needs
`Factura` (005) and `Procesamiento` (003); `publicacion` (007) needs `Usuario` (002); the seeds (009,
010) need `MotivoAtributo` (004) and the publication tables (007).

**This is the only change where "everything at once" is available.** From `011` onward ADR 0016's
backward-compatibility rule applies and one file is one deployable, backward-compatible change.

---

## Where the base data lives

**Separate scripts, not mixed with the DDL.** Three reasons: the DDL files stay pure structure and
diff cleanly against what the two runtimes expect (which is what ADR 0019 level 2 reads); base data
has different failure semantics (a duplicated seed is an operational annoyance, a wrong DDL is a
schema break), so repairing one should not touch the other; and the `MotivoAtributo` seed is the one
insert that can fail for reasons **outside this schema**, which deserves to fail in its own file.

`009_datos_base.sql`:
- **`EstadoIntegracion` — five rows**: `GMAIL`, `DRIVE`, `SHEETS`, `SBS`, `WORKER`. **Corrected
  (Work Unit 4).** This paragraph originally said seven, adding `TELEGRAM` and `CORREO` on the
  reasoning that ADR 0003 revision 4 — the later document at the time — should win over TECH-DESIGN's
  five. `spec.md` was written after this paragraph and settled the question explicitly and literally
  ("EstadoIntegracion is seeded with exactly the five known integration names... five rows, no more,
  no fewer") without this paragraph or the Open Question below being updated to match — a real
  document/document conflict, found and reported rather than silently resolved, then fixed here in
  the direction `spec.md` already committed to. The `007` schema's own `CK_EstadoIntegracion_Nombre`
  still allows all seven values; only the *seed* is five. `TELEGRAM`/`CORREO` rows, if ever needed,
  are for whoever writes the API to insert when it starts executing those integrations, not this
  migration's job.
- **`Configuracion` defaults**: seed every key named in TECH-DESIGN with its `Seccion`, `Tipo` and
  `Descripcion`. `ValorPorDefecto` is filled **only where a document states one**; where no document
  states a value (Gmail label, Telegram destination, allowed attachment types and max size — ADR 0013
  leaves the last two *pendiente*), the key is seeded with `Valor` and `ValorPorDefecto` both `NULL`,
  so an unconfigured system fails visibly at the configuration screen rather than silently running on
  an invented default. This is how the seed avoids inventing business meaning.

`010_motivo_atributo_demo.sql`:
- The reclassified motives, set to `OrigenLibro = '02'`, `Activo = 1`.
- **Seeded by `INSERT … SELECT` from `dbo.Motivo` matched on the motivo's own number, never on a
  hardcoded external id** — this project does not know `dbo.Motivo`'s key values and must not guess
  them.
- **Guarded**: `IF @@ROWCOUNT <> <n> THROW …`. Without the guard, a mismatch against the external
  catalog seeds a partial set silently, and the validation screen would then be missing motives with
  nothing to explain why.
- All seeds are `NOT EXISTS`-guarded so a re-run in a fresh environment is deterministic.

**Satellite semantics that make this scope coherent.** `MotivoAtributo` is an **override**: absence of
a row means "no override — use the external catalog's own values". That is why seeding only the
reclassified motives here is complete rather than partial, and it is what lets item #3 add the six
`Activo = 0` rows and the rest of the classification later without this change being wrong in the
meantime. Item #3 must implement the resolution as a `LEFT JOIN` with fallback, not an `INNER JOIN`.

---

## How the ADR 0019 level-2 tests reach a database

They run against **real SQL Server**, with **the same scripts applied by the same runner** — ADR 0019
states this dependency explicitly. Not SQLite, not an in-memory provider: filtered indexes,
`rowversion`, `READPAST`, `UPDLOCK` and `DENY` semantics do not exist there, and those are exactly what
is under test.

**Per run:** create an empty database `fact_test_<id>` on a local/CI instance (SQL Server Developer
edition, or the `mssql/server` container image) → run `SmartNet.Db.Runner` over `SmartNet/db/schema/`
→ run `SmartNet/db/test-bootstrap/` → run the .NET and pytest contract suites → drop the database.

**The permission matrix does not need instance rights.** `test-bootstrap` creates
`CREATE USER usr_api WITHOUT LOGIN` and `CREATE USER usr_worker WITHOUT LOGIN` **before** the runner
executes; `008` finds the principals already present, skips creation and applies role membership and
grants unchanged. The assertions then run as `EXECUTE AS USER = 'usr_api'; SELECT … FROM
fact.Procesamiento;` and expect error 229 — `EXECUTE AS USER` is the documented way to evaluate
database-level permissions, and it removes the `CREATE LOGIN` dependency from the matrix test
entirely. `008` must therefore be written as *create-if-absent, always-grant*, which is also what
makes a re-applied environment converge.

**The cross-runtime half still needs real logins** (.NET writes / Python reads over real connection
strings). On the test instance the CI job owns the instance, so `test-bootstrap` creates the two
logins with passwords generated per run and held only in the process environment — never written to a
file, never committed. That keeps the no-credential-in-git rule intact on the test path too.

---

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The one adjacent surface is the CI lint/hash step, which reads files and
returns an exit code.

---

## Migration / Rollout

No data migration (ADR 0016's terminology note applies). Rollout is ADR 0012's order: schema → API →
worker. This change delivers only step 1; steps 2 and 3 have no artifact yet.

---

## Open Questions

- [ ] **BLOCKING for `004_satelites_datos_maestros.sql`.** The real key types of `dbo.Proveedor`,
      `dbo.CuentaContable`, `dbo.Motivo` and `dbo.Origen` were unknown. **RESOLVED — the project
      owner confirmed all four**, and the suspicion was correct: they are **business codes, not
      surrogate ids**. ADR 0011's `ProveedorId BIGINT` / `MotivoId BIGINT` snippet was wrong and has
      been corrected in the ADR and in `TECH-DESIGN.md`.

      | External table | Key |
      |---|---|
      | `dbo.Proveedor` | **`CHAR(6)`** — `P00000` is literally the key |
      | `dbo.CuentaContable` | **account code, variable-length text** |
      | `dbo.Motivo` | **`INT`** |
      | `dbo.Origen` | **`CHAR(2)`** — matches what `CorrelativoAsiento` already assumed |

      Consequences for `004` and beyond: `ProveedorAtributo`, `SugerenciaCuenta` and the supplier
      reference on `Factura` key on **`ProveedorCodigo CHAR(6)`**, not a `BIGINT` id. The design's
      own choice of `VARCHAR` (not `CHAR`) for `CuentaCodigo` is confirmed as load-bearing: fixed
      length would pad the 2-to-6-digit prefixes and break `LIKE prefix + '%'`.
- [x] **RESOLVED — `MOTIVOS-CLASIFICACION.md` contradicted itself on the count.** The prose said 22
      reclassified motives; the table carries **23** `†` rows (#5, 13, 16, 17, 18, 19, 20, 21, 30,
      38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, **88**, 90) and 27 plain `02` rows — 23 + 27 =
      50, so the table was right and the "28 + 22" split was off by one on each side.
      **The orchestrator verified the count directly and corrected every document that repeated the
      wrong figure**: `MOTIVOS-CLASIFICACION.md`, `TECH-DESIGN.md`, `REGLAS.md`,
      `DECISIONES-REVISION.md`, the proposal and the spec. The spec's scenario had also omitted
      motive **88** (`Devolución Comprobante CChica`, `169105`) from its enumerated list; it is now
      included.
      The seed uses the **table**, and its `@@ROWCOUNT` guard expects **23**.
- [ ] **The shared database's collation is unknown**, and it is not ours to set. If it is
      case/accent-insensitive, `Numero` and `RucProveedor` comparisons are CI — harmless for digits,
      but `IX_Factura_Identidad`'s detection semantics should be confirmed against it.
- [ ] **The `EstadoIntegracion` write split is convention, not engine-enforced.** ADR 0003 partitions
      it by row value (`Nombre`), and no `GRANT` can express a per-value split. Row-level security
      could, but it cannot be assumed on a shared instance and would be disproportionate. This is the
      one class where ADR 0003's "impuesto, no confiado" claim does not hold, and ADR 0019 level 2 is
      its only check. Worth stating in the spec rather than leaving implied.
- [ ] **The internal/external comprobante reference is asymmetric** — internal is one `Numero`,
      external is `Serie` + `Numero`. Honoured as documented; confirm it is intentional before item
      #10 builds credit notes on it.
- [x] **RESOLVED (Work Unit 4).** `EstadoIntegracion`'s row set: five names in TECH-DESIGN, seven in
      ADR 0003 rev 4. `spec.md`'s own Scenario already settled this explicitly, in favor of five —
      "no more, no fewer" — before Work Unit 4 began; this Open Question and the 009 planning note
      above simply hadn't been updated to match. `009_datos_base.sql` seeds five.
