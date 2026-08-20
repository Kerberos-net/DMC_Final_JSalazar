# Design: Extracción y asociación (BACKLOG #6)

## Technical Approach

Extend `SmartNet/worker/` with a second processing stage that keeps item #4/#5's invariant intact —
**a module either decides or does IO, never both**. Five new pure modules (`ubl.py`, `pdf_texto.py`,
`comprobante.py`, `afectacion.py`, `errores.py`), one new IO module (`pdf_lectura.py` — disk +
Tesseract subprocess), one new cursor-shaped repository (`procesamiento_repo.py`), an extension of
the existing `documento_repo.py`, and one new single-run orchestrator (`cli_procesamiento.py`). One
migration, `014`, adds exactly two columns plus their guards.

The stage reads what #5 left behind (`DocumentoRecibido.Estado='DESCARGADO'`, `TipoDocumento` NULL),
parses XML as the authority, extracts PDF text and OCRs only the pages that have none, associates the
two by ADR 0017's four-component key, computes `AfectacionMixta` per REGLAS §8, and stops. It does
**not** promote anything — `Factura`/`FacturaExtraccion` are #7's and .NET's (ADR 0003).

Money note: `DatosExtraidos.Monto` is `DECIMAL(18,2)`. Every monetary value read from a UBL
`cbc:PayableAmount` or from OCR text is built with `Decimal(str(...))`, never `float` — CONVENTIONS.md,
and the first item in this project where the rule actually has a surface.

## Architecture Decisions

### Decision 1 — `lxml` with an explicitly hardened parser, not stdlib `xml.etree`

The XML is attacker-influenced: anyone can email the monitored inbox. This is the same threat framing
that #5 applied to the attachment filename.

| Option | Tradeoff | Decision |
|---|---|---|
| `xml.etree.ElementTree` (stdlib) | Zero dependency, but the entity/DTD posture is inherited from whatever `expat` the interpreter was linked against — a reviewer cannot verify the invariant by reading the module, and neither can a test | Rejected |
| `defusedxml` over stdlib etree | Restores the hardening, but as a wrapper whose guarantees live in a third package, and UBL's namespace-heavy access still needs manual `{ns}tag` juggling | Rejected |
| `lxml>=5.3` with `etree.XMLParser(resolve_entities=False, no_network=True, load_dtd=False, dtd_validation=False, huge_tree=False)` | One compiled dependency (cp313 wheels exist for Windows and Linux); the security posture is a literal argument list a structural test can assert, and XPath with a namespace map matches how UBL is actually navigated | **Chosen** |

`no_network=True` is not decoration here: it is the machine-checkable half of the resolved business
decision that **documents never leave the organization**. A UBL file that declares an external DTD
cannot cause a fetch, and the structural suite asserts the parser is constructed with these exact
keywords (same style as `test_no_dbo_structural.py`).

### Decision 2 — "is this a SUNAT comprobante?" is three ordered gates, and **no XSD validation**

| Gate | Check | Failure |
|---|---|---|
| 1. Well-formedness | `etree.fromstring(bytes, parser)` | `XMLSyntaxError` → `PERMANENTE` ("XML mal formado") |
| 2. Document identity | Root `{namespace}localname` ∈ `{Invoice-2}Invoice`, `{CreditNote-2}CreditNote`, `{DebitNote-2}DebitNote` (UBL 2.1) | `PERMANENTE`, with the root name in the message |
| 3. Identity fields present | `cbc:ID` (serie-número), supplier RUC (`cac:AccountingSupplierParty//cac:PartyIdentification/cbc:ID`), tipo de comprobante | `PERMANENTE` ("no es un comprobante") |

Gate 2 is where the real-world case lands that a schema check would miss: the **SUNAT CDR**
(`ApplicationResponse`, the constancia de recepción) is a perfectly valid UBL document that arrives
attached to the same emails and is *not* a comprobante. It fails gate 2 by root name, loudly, instead
of failing gate 3 with a confusing "missing field".

Rejected: validating against SUNAT's UBL 2.1 XSD set. It would mean vendoring a large, SUNAT-versioned
schema tree into the repository, and a SUNAT schema bump would start rejecting valid invoices we can
already read. Structural validation of exactly the fields we consume is the proportional check.

**Non-identity fields (`NombreProveedor`, `Monto`, `Moneda`, `FechaEmision`) are never fatal.** A
missing one is appended by name to `DatosExtraidos.CamposNoExtraidos NVARCHAR(500)` — the column
TECH-DESIGN created for exactly this and that nothing has written yet. Splitting fatal (identity) from
recorded (the rest) is what keeps ADR 0010's asymmetry cost on the right side: a `PERMANENTE` stops a
document, so it is reserved for documents that can never yield a comprobante.

Tipo de comprobante (SUNAT catálogo 01, `CHAR(2)`) comes from the root: `Invoice` → read
`cbc:InvoiceTypeCode` (`01` factura / `03` boleta), `CreditNote` → `07`, `DebitNote` → `08`. The type
code element only exists on `Invoice`; the notes carry it in their root name, which is why the mapping
is table-driven rather than a single XPath.

### Decision 3 — text layer first, OCR **per page** only where there is none

ADR 0017's own source table says *"Extracción de texto y, si hace falta, OCR"*. The conditional is
normative, not an optimization: OCR is the declared highest technical risk of the project, so every
page that has a real text layer and gets OCR'd anyway is precision thrown away on purpose.

| Concern | Chosen | Rejected, and why |
|---|---|---|
| Text layer + document diagnosis | `pypdf>=5.1` — pure Python; `reader.is_encrypted` and `PdfReadError` map 1:1 onto ADR 0010's "corrupto / protegido con contraseña / formato no soportado" | `pdfplumber` — a layout/geometry engine; we need characters, not word boxes |
| Rasterization for OCR | `pypdfium2>=4.30` — Apache-2.0/BSD-3, self-contained wheels, renders a page to a bitmap with no system binary | `pdf2image` + **Poppler** — a *second* system binary prerequisite, doubling the deployment surface for nothing. `PyMuPDF` — AGPL-3.0 for a company-internal system; the license question alone outweighs its (real) speed advantage |
| OCR | `pytesseract>=0.3.13` + `Pillow>=11` + the **Tesseract** system binary with the `spa` traineddata | Any cloud OCR — closed by the resolved business decision |

Per-page rule: strip whitespace from `page.extract_text()`; below `_MINIMO_CARACTERES_PAGINA = 100`
non-whitespace characters, treat the page as scanned and OCR it at 300 DPI (`scale = 300/72`). The
threshold's failure mode is deliberately one-sided — a short page that *did* have text simply gets
OCR'd redundantly, which costs seconds and never costs correctness. Text is concatenated in page
order regardless of which path produced it.

`_MAXIMO_PAGINAS_OCR = 5`: a comprobante's identity fields are on page one; a 500-page attachment must
not hang the run. Pages beyond the cap are not OCR'd, and if the identity fields were not found the
document simply stays unassociated — which is the already-designed, already-non-blocking
"PDF sin XML pareja" path, not a new failure mode.

### Decision 4 — two seams, because ADR 0017 demands one and testability demands the other

ADR 0017 requires the extraction engine behind *"una interfaz propia con una implementación
sustituible"*. That gives `MotorOcr`. But the CLI also needs a substitution point that keeps the whole
orchestration unit-testable on a machine with no Tesseract at all — that gives `LectorPdf`. Two
protocols, nested, exactly as #5 had both `ClienteGmail` and `cursor`.

    cli_procesamiento ──► LectorPdf (protocol) ──► LectorPdfLocal ──► MotorOcr (protocol) ──► MotorTesseract
         │ fake in unit tests                          │ pypdf + pypdfium2      │ fake in unit tests

### Decision 5 — normalization is what makes "exact match" actually match

The four components are normalized once, in `comprobante.py`, and compared as a frozen dataclass:

| Component | Normalization | Why |
|---|---|---|
| RUC emisor | keep digits only | OCR and XML both emit `20123456789`, `20123-456-789`, `R.U.C. 20123456789` |
| Tipo comprobante | 2 chars, zero-padded left (`'1'` → `'01'`) | SUNAT catálogo 01 has a significant leading zero (003's own DDL comment) |
| Serie | upper-cased, whitespace stripped, **never zero-padded** | `F001` (electronic) and `001` (printed) are different namespaces, not the same serie padded differently |
| Número | leading zeros stripped (`'00000123'` → `'123'`) | 003's DDL says it literally: *"VARCHAR because issuers do not always pad the correlativo"*. Without this, exact matching fails on the most common real-world difference between an XML and its PDF |

Serie is parsed from the compound `Numero VARCHAR(20)` at comparison time (`'F001-00000123'` →
`('F001','123')`), per the resolved decision — **no `Serie` column is added**. A `Numero` with no `-`
yields no key and therefore no association, never a partial match.

### Decision 6 — the candidate set is bounded by *unpaired-ness*, not by a time window

The question the exploration left open: does association search the same email, a time window, or all
of history?

| Option | Tradeoff | Decision |
|---|---|---|
| Same `Email` only | Covers the dominant case (SUNAT senders attach XML+PDF to one message) but silently drops the legitimate late arrival, manufacturing "PDF sin pareja" warnings that item #13 would then have to triage by hand | Rejected |
| Time window (e.g. ±30 days) | A date is a **proximity signal**, and ADR 0017 names *"la fecha del correo"* as evidence that never establishes association. Using it even as a *filter* smuggles the forbidden signal into the decision | Rejected |
| All `Procesamiento` rows whose `DocumentoAsociadoId IS NULL` | The set shrinks as pairs form, so it grows with the number of genuinely orphan documents — exactly the working set #13 will display — not with volume or with time | **Chosen** |

Order within a run follows ADR 0017 literally: **all XMLs first**, then each PDF. The candidate set for
any document is `(the run's own already-processed documents) ∪ (previously processed, still-unpaired
documents)`, read with one `SELECT` joining `Procesamiento` + `DatosExtraidos` + `DocumentoRecibido`,
served by a filtered index (`IX_Procesamiento_SinAsociar ... WHERE DocumentoAsociadoId IS NULL`).

**Ambiguity refuses to associate.** If a normalized key matches more than one unpaired counterpart,
neither is linked — ADR 0017: *"Nunca se asigna a un comprobante por proximidad o descarte."* This is a
RED test, not a comment.

**The FK is written on both rows.** The XML's `Procesamiento.DocumentoAsociadoId` points at the PDF's
`DocumentoRecibidoId` and vice versa, inside one transaction. Cost: two `UPDATE`s. Benefit: "do I have
my pair?" is one read from either side, so #13 needs no direction convention and no reverse scan. A
`CHECK (DocumentoAsociadoId IS NULL OR DocumentoAsociadoId <> DocumentoRecibidoId)` makes
self-association impossible in the engine rather than by discipline.

Content-first, filename-as-backup: when the PDF's text does not yield all four components,
`pdf_texto.py` falls back to the strict SUNAT filename convention
`<RUC>-<TIPO>-<SERIE>-<NUMERO>.pdf` on `DocumentoRecibido.NombreArchivo`. ADR 0017 authorizes this
explicitly (*"el nombre del archivo puede usarse como respaldo, siempre que la coincidencia sea
inequívoca"*), and the pattern is all-or-nothing: a partial match yields no key.

### Decision 7 — a missing Tesseract aborts the run; it is never a document's `PERMANENTE`

ADR 0010 warns that misclassification is asymmetric: marking permanent something environmental
*"detiene el procesamiento y exige intervención"*. A `TesseractNotFoundError` is an infrastructure
fault — classifying it per-document would stamp an irreversible `PERMANENTE` on every PDF in the batch
because the *host* was misconfigured.

So `cli_procesamiento` runs a **preflight** (`pytesseract.get_tesseract_version()`) once, before the
first document, and fails the whole run into `EstadoIntegracion(Nombre='WORKER')` if it raises. This is
the exact shape of #5's Decision 3 label gate: a misconfiguration must fail loudly and create nothing,
never convert into a silently-working wrong configuration.

`SMARTNET_WORKER_TESSERACT_CMD` is an **optional** env var overriding the binary path (Windows installs
to `C:\Program Files\Tesseract-OCR\tesseract.exe`, which is not on `PATH` by default). Unlike the
credential and storage-root variables, its absence is legal and means "expect `tesseract` on `PATH`" —
it carries no secret and has no wrong-default hazard.

### Decision 8 — error classification is a pure lookup, and retries are one `WHERE` clause

`errores.py` maps an exception type to an ADR 0010 class, with no IO and no side effect:

| Cause | Class | `ProximoReintentoEn` |
|---|---|---|
| `XMLSyntaxError`, `UblInvalidoError`, `PdfReadError`, encrypted PDF, unsupported/zero-page PDF | `PERMANENTE` | `NULL` — never retried |
| `pyodbc.OperationalError`, transient IO on the shared volume | `TRANSITORIO` | `instante + 2^n` seconds, `n ≤ 3` |
| Anything unrecognised | `TRANSITORIO` | ADR 0010: *"la clasificación debe errar hacia transitorio ante la duda"* |

`DIFERIBLE` has no producer in this item — nothing here calls a quota-bearing API. Recorded as
deliberately unused, not forgotten.

Retry selection is the same predicate that implements both classes, with no scheduler:

```sql
WHERE d.Estado = 'DESCARGADO'
   OR (d.Estado = 'ERROR' AND EXISTS (SELECT 1 FROM fact.ProcesamientoIntentos i ...
                                      WHERE i.ProximoReintentoEn <= ? AND i.NumeroIntento < 3))
```

A `PERMANENTE` wrote `ProximoReintentoEn = NULL`, so it is never re-selected. One predicate, two
behaviors, zero state machine.

### Decision 9 — one `Procesamiento` per document, written once in its terminal state

`ProcesamientoIntentos.NumeroIntento` already presupposes one `Procesamiento` with N attempts, so a
retry **updates** the existing row rather than inserting a second one. `'PENDIENTE'` and `'EN_PROCESO'`
stay unused by this item and that is stated rather than papered over: with one transaction per document
an intermediate state is rolled back on any crash, so writing it would produce a state no observer can
ever see. `IniciadoEn`/`FinalizadoEn` are both set at commit.

Terminal states: success → `Procesamiento.Estado='COMPLETADO'`, `DocumentoRecibido.Estado='PROCESADO'`,
`TipoDocumento` finally set to `'XML'`/`'PDF'` (the hook #5 left explicitly NULL). Failure →
`'ERROR'` on both. `Email.Estado` moves `'CANDIDATO'` → `'PROCESADO'` when every one of its documents is
`'PROCESADO'`, `'ERROR'` if any ended in error and none is still pending — closing the loop #5's design
documented as this item's job.

**The "PDF sin XML" warning is a predicate, not a column.** `TipoDocumento='PDF' AND
Procesamiento.Estado='COMPLETADO' AND DocumentoAsociadoId IS NULL` *is* the visible, non-blocking
indicator #13 consumes. It cannot drift out of sync with the association, because it is the
association. The same predicate with `TipoDocumento='XML'` is deliberately **not** a warning — a lone
XML is authoritative and sufficient (resolved round-2 decision).

## Data Flow

    fact.DocumentoRecibido  Estado='DESCARGADO'  ∪  reintento vencido (Decision 8)
            │ documento_repo.listar_pendientes(cursor, ahora)
            ▼
    <SMARTNET_WORKER_STORAGE_ROOT>/<RutaRelativa>   (lectura del volumen, ADR 0013)
            │
      ┌─────┴──────── XML primero (ADR 0017) ────────────────┐
      ▼                                                       ▼
    ubl.parsear(bytes)               PURO        lector.leer_paginas(ruta)          IO
      │ lxml endurecido (Decision 1)                │ pypdf: ¿cifrado? ¿capa de texto?
      │ 3 compuertas (Decision 2)                   │   sí → texto embebido
      │ fallo → PERMANENTE                          │   no → pypdfium2 300dpi → MotorOcr
      ▼                                             ▼
    ComprobanteUbl{clave, campos,        pdf_texto.extraer(texto, nombre_archivo)  PURO
                   codigos_afectacion}             │ regex + respaldo nombre SUNAT
      │                                            ▼
      ├─► afectacion.calcular(codigos)      ClaveComprobante | None
      │      true / false                          │
      ▼                                            ▼
                comprobante.asociar(nuevos, huerfanos)                             PURO
                  4 componentes normalizados · exacto · ambiguo ⇒ sin asociar
                                    │
                                    ▼
        por documento, UNA transaccion (aislamiento como #5, Decision 7):
          Procesamiento (INSERT/UPDATE terminal) → DatosExtraidos (+ AfectacionMixta)
          Procesamiento.DocumentoAsociadoId en AMBOS lados
          DocumentoRecibido.TipoDocumento / .Estado     Email.Estado
          ProcesamientoIntentos (+ ProcesamientoError si fallo)
                                    ▼
                    fact.EstadoIntegracion (Nombre='WORKER', UPDATE, rowcount=1)

## File Changes

| File | Action | Description |
|---|---|---|
| `.../smartnet_worker/ubl.py` | Create | **Puro**: `parsear(datos) -> ComprobanteUbl`, `_PARSER` endurecido, mapeo raíz→tipo, `UblInvalidoError` |
| `.../smartnet_worker/pdf_texto.py` | Create | **Puro**: `extraer(texto, nombre_archivo) -> ExtraccionPdf`, regex de RUC/serie-número/tipo/monto/fecha, respaldo de nombre SUNAT |
| `.../smartnet_worker/comprobante.py` | Create | **Puro**: `ClaveComprobante`, `normalizar_*`, `parsear_serie_numero`, `asociar(nuevos, huerfanos) -> tuple[Par, ...]` |
| `.../smartnet_worker/afectacion.py` | Create | **Puro**: `calcular_afectacion_mixta(codigos) -> bool \| None` (REGLAS §8) |
| `.../smartnet_worker/errores.py` | Create | **Puro**: `clasificar(error) -> Clasificacion`, `proximo_reintento(instante, intento)` (ADR 0010) |
| `.../smartnet_worker/pdf_lectura.py` | Create | **IO**: `LectorPdf`/`MotorOcr` protocols, `LectorPdfLocal`, `MotorTesseract`, `verificar_tesseract()` |
| `.../smartnet_worker/procesamiento_repo.py` | Create | `cursor`-shaped: `upsert_procesamiento`, `insertar_datos_extraidos`, `asociar_documentos`, `insertar_error`, `insertar_intento`, `listar_huerfanos` |
| `.../smartnet_worker/documento_repo.py` | Modify | `listar_pendientes(cursor, ahora)`, `fijar_tipo_documento`, `fijar_estado_documento`, `refrescar_estado_email` |
| `.../smartnet_worker/config.py` | Modify | `TESSERACT_CMD_ENV_VAR` (opcional), `OCR_IDIOMA='spa'`, `OCR_DPI=300`, `obtener_tesseract_cmd() -> str \| None` |
| `.../smartnet_worker/cli_procesamiento.py` | Create | Orquestador único: preflight → XML primero → PDF → asociación → `EstadoIntegracion('WORKER')` |
| `SmartNet/worker/pyproject.toml` | Modify | `lxml`, `pypdf`, `pypdfium2`, `pytesseract`, `Pillow`; script `smartnet-procesamiento`; marker `ocr` |
| `SmartNet/worker/README.md` | Modify | Sección "Prerequisitos de sistema": Tesseract + `spa`, por SO; `SMARTNET_WORKER_TESSERACT_CMD` |
| `SmartNet/db/schema/014_asociacion_y_afectacion_mixta.sql` | Create | Contenido literal abajo |
| `SmartNet/db/schema/rollback/014_down.sql` | Create | Advisory, con la nota "CANNOT UNDO" de 009/013 |
| `SmartNet/db/schema/checksums.txt` | Modify | Una línea nueva para `014_*.sql` (`ChecksumManifestTests`) |
| `SmartNet/db/runner/.../BaseDataTests.cs` | Modify | `+[InlineData("EMPRESA", "RUC")]` (`Valor`/`ValorPorDefecto` ambos `NULL`) |
| `.../tests/unit/test_no_dbo_structural.py` | Modify | **Quita** `fact.procesamiento`/`fact.datosextraidos` de la lista prohibida (ahora son de este ítem), **agrega** `fact.facturaextraccion`; escaneo nuevo "sin red" |
| `.../tests/unit/test_ubl.py`, `test_pdf_texto.py`, `test_comprobante.py`, `test_afectacion.py`, `test_errores.py` | Create | Suites puras |
| `.../tests/unit/test_procesamiento_repo.py`, `test_cli_procesamiento.py` | Create | Cursor falso + `LectorPdf` falso |
| `.../tests/fixtures/ubl_*.xml`, `comprobante_con_texto.pdf`, `comprobante_escaneado.pdf` | Create | UBL real **redactado** (RUC y razón social reales son datos de un tercero) + dos PDFs sintéticos |
| `.../tests/integration/test_pyodbc_integracion.py` | Modify | `usr_worker` real contra las cuatro tablas nuevas y la FK de asociación |
| `.github/workflows/ci.yml` | Modify | `apt-get install tesseract-ocr tesseract-ocr-spa` + `pytest -m "integracion or ocr"` en `pruebas-de-worker-python` |

No permission change: `008_usuarios_y_permisos.sql` already grants `fact_worker`
`SELECT, INSERT, UPDATE` on all six tables this item writes. Verified against the script, not assumed.

## Interfaces / Contracts

```python
# comprobante.py — puro. La clave de ADR 0017, normalizada una sola vez.
@dataclass(frozen=True)
class ClaveComprobante:
    ruc_emisor: str      # solo digitos
    tipo: str            # 2 chars, cero a la izquierda significativo
    serie: str           # mayusculas, sin relleno
    numero: str          # ceros a la izquierda eliminados

def parsear_serie_numero(numero: str) -> tuple[str, str] | None   # 'F001-00000123' -> ('F001','123')
def asociar(nuevos: Sequence[Documento], huerfanos: Sequence[Documento]) -> tuple[Par, ...]
    # exacto sobre los 4 componentes; >1 coincidencia => ninguna (ADR 0017)

# ubl.py — puro: ni red, ni disco, ni DB, ni reloj.
@dataclass(frozen=True)
class ComprobanteUbl:
    clave: ClaveComprobante
    nombre_proveedor: str | None
    monto: Decimal | None        # nunca float (CONVENTIONS.md)
    moneda: str | None           # ISO 4217 alpha-3
    fecha_emision: date | None
    codigos_afectacion: tuple[str, ...]   # catalogo 07, en orden de linea
    campos_no_extraidos: tuple[str, ...]

def parsear(datos: bytes) -> ComprobanteUbl      # UblInvalidoError -> PERMANENTE

# afectacion.py — puro. REGLAS.md §8, tres estados.
def calcular_afectacion_mixta(codigos: Sequence[str]) -> bool | None
    # >1 codigo distinto -> True (rechazo 409) | exactamente 1 -> False | ninguno -> None

# pdf_lectura.py — IO. Los dos seams (Decision 4).
class MotorOcr(Protocol):
    def reconocer(self, imagen_png: bytes, idioma: str) -> str: ...
class LectorPdf(Protocol):
    def leer_paginas(self, ruta: Path) -> tuple[str, ...]: ...   # PdfIlegibleError -> PERMANENTE

# procesamiento_repo.py — recibe cursor, igual que documento_repo.py.
def upsert_procesamiento(cursor, documento_id: int, estado: str,
                         iniciado: datetime, finalizado: datetime) -> int
def insertar_datos_extraidos(cursor, procesamiento_id: int, d: DatosExtraidos) -> None
def asociar_documentos(cursor, procesamiento_a: int, documento_b: int,
                       procesamiento_b: int, documento_a: int) -> None   # ambos lados
def listar_huerfanos(cursor) -> tuple[Documento, ...]   # DocumentoAsociadoId IS NULL
```

| Seam (port) | Shape | Substituted in tests by | Consumer |
|---|---|---|---|
| Motor de extracción (ADR 0017) | `MotorOcr.reconocer` | `MotorOcrFalso` que devuelve texto fijo | `LectorPdfLocal` |
| Documento PDF | `LectorPdf.leer_paginas` | `LectorPdfFalso` — la suite unitaria corre sin Tesseract | `cli_procesamiento` |
| SQL Server | `cursor` de pyodbc | cursor falso que registra sentencia y parámetros (patrón #4/#5) | ambos repos |
| Volumen compartido | `Path` bajo la raíz configurada | `tmp_path` de pytest | `cli_procesamiento` |
| Reloj | `instante` como parámetro | valor fijo | todo el paquete |

### Migration content (`014_asociacion_y_afectacion_mixta.sql`)

`GO` is mandatory between the column additions and the objects that reference them: SQL Server compiles
a whole batch before executing it, so a `CHECK` or filtered index naming a column added earlier *in the
same batch* fails with "Invalid column name". DbUp splits on `GO` (proved by
`RunnerFailureHaltTests`) and wraps the whole script in one transaction (`WithTransactionPerScript`), so
the batches stay atomic together.

```sql
-- 014_asociacion_y_afectacion_mixta.sql
-- BACKLOG #6. Dos columnas aditivas y nullable: ninguna fila existente se rompe, ningun permiso
-- cambia (008 ya da SELECT/INSERT/UPDATE de estas dos tablas a fact_worker).
--
-- DocumentoAsociadoId cierra el hueco que 003 dejo: Procesamiento tenia un unico FK
-- (DocumentoRecibidoId) y NADA vinculaba el DocumentoRecibido de un XML con el de su PDF. Es la
-- decision resuelta del usuario (columna FK nullable, no una tabla fact.AsociacionDocumento).
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'DocumentoAsociadoId' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento ADD DocumentoAsociadoId BIGINT NULL;

-- AfectacionMixta: REGLAS.md §8, tres estados (true = el XML declara mas de un codigo de afectacion
-- -> rechazo 409; false = uno solo, verificada; NULL = sin XML, NO verificada). BIT NULL es el unico
-- tipo que representa los tres sin inventar un centinela.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE name = 'AfectacionMixta' AND object_id = OBJECT_ID('fact.DatosExtraidos'))
    ALTER TABLE fact.DatosExtraidos ADD AfectacionMixta BIT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Procesamiento_DocumentoAsociado')
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT FK_Procesamiento_DocumentoAsociado
            FOREIGN KEY (DocumentoAsociadoId) REFERENCES fact.DocumentoRecibido (DocumentoRecibidoId);

-- Un documento no puede ser su propia pareja. Invariante del motor, no de la disciplina del worker.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Procesamiento_NoAutoAsociacion')
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT CK_Procesamiento_NoAutoAsociacion
            CHECK (DocumentoAsociadoId IS NULL OR DocumentoAsociadoId <> DocumentoRecibidoId);

-- Indice filtrado: el conjunto candidato de la asociacion es "lo que sigue sin pareja" (Decision 6),
-- que encoge conforme se forman parejas -- no crece con el volumen historico.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Procesamiento_SinAsociar' AND object_id = OBJECT_ID('fact.Procesamiento'))
    CREATE INDEX IX_Procesamiento_SinAsociar ON fact.Procesamiento (DocumentoRecibidoId)
        WHERE DocumentoAsociadoId IS NULL;

-- Un Procesamiento por DocumentoRecibido: ProcesamientoIntentos.NumeroIntento ya presupone esto
-- (N intentos de UN procesamiento). Sin este UNIQUE, upsert_procesamiento necesitaria un
-- SELECT-then-INSERT -- la forma TOCTOU que #4/#5 rechazaron explicitamente. Con el UNIQUE, el
-- motor lo garantiza y el repo usa el mismo patron IntegrityError que insertar_email/
-- insertar_documento (decision explicita del usuario, Open Question 4).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_Procesamiento_DocumentoRecibido' AND object_id = OBJECT_ID('fact.Procesamiento'))
    ALTER TABLE fact.Procesamiento
        ADD CONSTRAINT UQ_Procesamiento_DocumentoRecibido UNIQUE (DocumentoRecibidoId);

-- RUC propio de la empresa: unica forma no-inferencial de distinguir, en un PDF sin XML que
-- declare dos RUC (proveedor y empresa propia), cual es el emisor -- ADR 0017 prohibe inferirlo por
-- proximidad de etiqueta. NULL-seeded, igual que las demas claves de Configuracion sin fijar por
-- este proyecto (decision explicita del usuario, Open Question 1).
IF NOT EXISTS (SELECT 1 FROM fact.Configuracion WHERE Seccion = 'EMPRESA' AND Clave = 'RUC')
    INSERT INTO fact.Configuracion (Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion)
    VALUES ('EMPRESA', 'RUC', 'TEXTO', NULL, NULL,
            N'RUC propio de la empresa (11 digitos). Usado para excluir el RUC propio al identificar el RUC emisor en un PDF sin XML que muestre ambos (ADR 0017, item #6).');
```

`rollback/014_down.sql` is advisory (never enumerated by the runner — `RollbackAdvisoryTests`), drops
the index, both constraints and both columns, and carries the "CANNOT UNDO" note: dropping
`AfectacionMixta` destroys the only record that a comprobante's afectación was ever verified.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit — `ubl.py` (puro) | Las tres compuertas: XML no bien formado; raíz `ApplicationResponse` (CDR de SUNAT) rechazada por nombre; `Invoice`/`CreditNote`/`DebitNote` → `01`/`03`/`07`/`08`; `Monto` es `Decimal`, nunca `float`; campo no-identidad ausente → `CamposNoExtraidos`, no error | Fixtures UBL redactadas |
| Unit — `ubl.py` (**adversarial**) | Billion laughs / entidad recursiva; entidad externa a `file:///etc/passwd`; `<!DOCTYPE ... SYSTEM "http://...">` → **ninguna** resolución ni petición; XML de 0 bytes; HTML renombrado `.xml` | RED antes del código; el parser endurecido es la respuesta |
| Unit — `comprobante.py` (puro) | `'00000123'` == `'123'`; `'F001'` ≠ `'001'`; `'1'` → `'01'`; `Numero` sin `-` → sin clave; **>1 candidato → ninguna asociación**; asociación simétrica en ambos lados | Tabla de casos |
| Unit — `afectacion.py` | 2 códigos → `True`; 1 → `False`; 0 → `None`; códigos repetidos (`['10','10']`) → `False` (distintos, no cantidad) | Directo, REGLAS §8 |
| Unit — `pdf_texto.py` (puro) | RUC junto a `R.U.C.` con y sin puntos; dos RUC en el documento (ver Open Question 1); serie-número con y sin espacios; respaldo `20123456789-01-F001-00000123.pdf`; **respaldo parcial → sin clave** | Blobs de texto, sin PDF |
| Unit — `errores.py` | Cada excepción → su clase ADR 0010; excepción desconocida → `TRANSITORIO`; `PERMANENTE` → `ProximoReintentoEn IS NULL` | Tabla de casos |
| Unit — `pdf_lectura.py` | Página con capa de texto → **`MotorOcr` nunca invocado** (el fake registra 0 llamadas); página sin texto → OCR de esa página y solo esa; PDF cifrado y PDF corrupto → `PdfIlegibleError`; tope de páginas respetado | `MotorOcrFalso` + fixtures PDF |
| Unit — repos (cursor falso) | SQL y parámetros exactos, `fact.` calificado; `asociar_documentos` escribe **dos** `UPDATE`; literales `'COMPLETADO'`/`'ERROR'`/`'PERMANENTE'`; guarda `rowcount` | Patrón `test_documento_repo.py` |
| Unit — `cli_procesamiento` | XML antes que PDF; XML presente ⇒ **cero** llamadas al lector de PDF para valores de campo; fallo de un documento no aborta el run; preflight de Tesseract falla ⇒ 0 filas escritas; PERMANENTE no agenda reintento | `LectorPdfFalso` + cursor falso |
| Estructural | Ningún módulo menciona `dbo.`; ninguno menciona `fact.Factura`/`fact.AdjuntoManual`/**`fact.FacturaExtraccion`**; **ningún módulo del camino de extracción importa `requests`/`urllib`/`http`/`socket`**; `ubl.py` construye el parser con `resolve_entities=False, no_network=True, load_dtd=False, huge_tree=False` | Escaneo literal del `src/`, patrón `test_no_dbo_structural.py` |
| Integration (`integracion`) | `usr_worker` real: `Procesamiento`+`DatosExtraidos`+`AfectacionMixta`; FK de asociación en ambos lados; `CK_Procesamiento_NoAutoAsociacion` rechaza la auto-asociación; **negativa**: `INSERT fact.FacturaExtraccion` falla por DENY | Job `pruebas-de-worker-python` existente |
| Integration (`ocr`, **marker nuevo, SÍ corre en CI**) | `comprobante_escaneado.pdf` → RUC, serie y número correctos vía Tesseract real | Ver nota abajo |

The `ocr` marker runs in CI, unlike `externa`. The `externa` exclusion exists because *"un CI rojo por
una caída de la SBS no dice nada sobre nuestro cambio"* — a third-party availability argument. Tesseract
is a deterministic local binary pinned by apt; there is no availability argument, and OCR is the
project's declared highest technical risk, so leaving it entirely unexercised would be the wrong trade.
Because the apt version may differ from a developer's local one, the `ocr` assertions check **extracted
fields**, never exact OCR text.

## Threat Matrix

| Boundary | Applicability | Design response | Planned RED tests |
|---|---|---|---|
| **Subprocess / process integration** (Tesseract) | **Applicable** | `pytesseract` is invoked with an image buffer and a fixed `lang`/`config`; **no attacker-controlled string ever reaches an argument** (the filename does not; the binary reads bytes we hand it). The binary path comes from an operator env var, validated once in the preflight, never from a document | Preflight failure ⇒ run aborts with 0 rows; a filename containing shell metacharacters produces no different invocation |
| **Classification of an untrusted document** (XML/PDF) | **Applicable** — every byte comes from an unauthenticated sender | Hardened lxml parser (no entities, no DTD, no network, no huge tree); root-element allowlist; `pypdf` failure and encryption become `PERMANENTE` instead of a crash; page cap bounds OCR cost | The adversarial `ubl.py` row and the corrupt/encrypted PDF row above |
| **Outbound network from the extraction path** | **Applicable** — the resolved business decision is "documents never leave the organization" | Structurally asserted: no `requests`/`urllib`/`http`/`socket` import in the new modules, and `no_network=True` on the parser | Structural scan row above |
| Documentation-like paths / filename → filesystem path | **N/A here** — this item *reads* paths #5 already sanitized and stored; it derives no new path from a name | — |
| Git repository selection / Commit state / Push state / PR commands | N/A | No component invokes `git`; no VCS or PR automation | — |

## Migration / Rollout

Additive and reversible. One new versioned script picked up by DbUp in lexical order (013 exists ⇒
014), every object `IF NOT EXISTS`-guarded so reapplication is a no-op, plus one line in
`checksums.txt`. No table or grant change. **Does** seed one new `Configuracion` key
(`EMPRESA.RUC`, resolved Open Question 1) — `BaseDataTests` needs
`+[InlineData("EMPRESA", "RUC")]`, same pattern as item #5's `ETIQUETA_PROCESADO`.

New deployment prerequisite, operational: install **Tesseract** and its `spa` traineddata on the worker
host — `apt-get install tesseract-ocr tesseract-ocr-spa` (Debian/Ubuntu) or
`winget install UB-Mannheim.TesseractOCR` (Windows, then set `SMARTNET_WORKER_TESSERACT_CMD`). Same
class of prerequisite as item #4's ODBC Driver 18: a system binary pip cannot supply, documented in
`SmartNet/worker/README.md`, not hidden in a comment. Then schedule `smartnet-procesamiento` after
`smartnet-gmail` (cron/Task Scheduler — out of this code's scope, as in #4/#5).

Rollback: revert the commit and promote `rollback/014_down.sql`. Documents fall back to
`Estado='DESCARGADO'`; no closed item depends on this stage having run.

## Open Questions — resueltas

- **RUC emisor en un PDF con dos RUCs**: se agrega la clave `Configuracion` `EMPRESA.RUC` a la
  migración 014 (NULL-seeded, igual que las demás claves de configuración sin fijar). El extractor
  excluye el RUC propio por valor exacto; el RUC restante es el emisor. Fallback sin cambios si esa
  clave sigue sin fijar en un deployment: sin key configurada, se recurre al respaldo de nombre de
  archivo SUNAT (ver Decision 6).
- **XML sin códigos de afectación**: `NULL` + `'AfectacionMixta'` agregado a `CamposNoExtraidos` —
  "no verificado", dirección conservadora, ratificado por el usuario.
- **`fact.InboxEvent`**: este ítem NO lo escribe. Confirmado — es trabajo de #7 al promover.
- **`UNIQUE (DocumentoRecibidoId)` en `fact.Procesamiento`**: se agrega en la migración 014.
  `upsert_procesamiento` usa el patrón `IntegrityError` (mismo que `insertar_email`/
  `insertar_documento` de #5), nunca SELECT-then-INSERT.
