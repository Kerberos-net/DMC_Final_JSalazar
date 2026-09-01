# Spec: Extracción y asociación (BACKLOG #6)

New capability: single-run Python worker stage that consumes `fact.DocumentoRecibido` rows with
`Estado='DESCARGADO'`, parses XML as the authoritative source when present, extracts text/OCR from
PDF locally when needed, associates XML↔PDF pairs by a four-component key, computes
`AfectacionMixta`, and classifies unrecoverable errors as `PERMANENTE`. Writes to
`fact.Procesamiento` / `fact.DatosExtraidos` / `fact.ProcesamientoError` / `fact.ProcesamientoIntentos`
(ADR 0003, Python-private). Depends on item #5 leaving `TipoDocumento` NULL.

## Non-Goals (explicit scope boundaries)

- **No promotion decision.** Whether a processed document becomes `Factura`/`FacturaExtraccion` is
  item #7's responsibility.
- **No per-field/source extraction evidence persisted in a table.** `FacturaExtraccion` is .NET's
  private table (ADR 0003); this item's data travels only in the `InboxEvent` payload.
- **No cloud OCR service.** Resolved business decision: documents never leave the organization.
- **No incidencias UI.** Item #13 consumes the state/warning this item leaves; it does not build the
  review screen.

---

## Capability: `parseo-xml-autoritativo`

### Requirement: When an XML attachment exists, it is the sole source of `fact.DatosExtraidos`

Per ADR 0017's source table, the worker MUST parse XML/UBL first and MUST NOT invoke OCR when XML is
present, even if a paired PDF also exists.

#### Scenario: XML alone populates DatosExtraidos without OCR
- **Given** a `DocumentoRecibido` with `TipoDocumento='XML'` and no paired PDF
- **When** processing runs
- **Then** `fact.DatosExtraidos` is populated from the XML and no OCR call is issued

#### Scenario: XML+PDF pair uses XML as source, PDF as evidence only
- **Given** an XML and its associated PDF (matched by the four-component key)
- **When** processing runs
- **Then** `fact.DatosExtraidos` values come from the XML; the PDF is not parsed for field values

---

## Capability: `extraccion-pdf-ocr-local`

### Requirement: When no XML exists, the worker extracts PDF text and falls back to local OCR

Per the resolved business decision, OCR MUST run via `pytesseract` + a Tesseract binary installed on
the worker host, with no network call to a third party.

#### Scenario: PDF-only document is OCR'd locally
- **Given** a `DocumentoRecibido` with `TipoDocumento='PDF'` and no paired XML
- **When** processing runs
- **Then** text extraction (and OCR if needed) executes locally, and no outbound network call to an
  OCR provider is made

---

## Capability: `asociacion-xml-pdf`

### Requirement: XML↔PDF association uses exact match on four normalized components only

Per ADR 0017, the key is RUC emisor, tipo de comprobante, serie, and número — serie parsed from
`DatosExtraidos.Numero`'s compound format at comparison time, not a separate column. Subject,
sender, date, and attachment position MUST NOT establish association.

#### Scenario: Exact four-component match associates the pair
- **Given** an XML and a PDF whose parsed RUC, tipo, serie, and número all match exactly
- **When** association runs
- **Then** `fact.Procesamiento`'s FK association column links the PDF's row to the XML's row

#### Scenario: Any mismatched component leaves the PDF unassociated
- **Given** a PDF whose parsed serie differs from every processed XML's serie
- **When** association runs
- **Then** the PDF remains unassociated; no non-exact signal (subject, sender, date, position)
  is consulted

---

## Capability: `calculo-afectacion-mixta`

### Requirement: `AfectacionMixta` is computed as a three-state indicator in `fact.DatosExtraidos`

Per ADR 0017: `true` when the XML declares more than one código de afectación, `false` when it
declares exactly one, `NULL` when there is no XML to verify against.

#### Scenario: XML with two afectación codes sets true
- **Given** an XML UBL declaring two distinct códigos de afectación across its lines
- **When** extraction runs
- **Then** `fact.DatosExtraidos.AfectacionMixta = 1`

#### Scenario: XML with one afectación code sets false
- **Given** an XML UBL declaring a single código de afectación
- **When** extraction runs
- **Then** `fact.DatosExtraidos.AfectacionMixta = 0`

#### Scenario: PDF-only document leaves it NULL
- **Given** a `DocumentoRecibido` processed without an associated XML
- **When** extraction runs
- **Then** `fact.DatosExtraidos.AfectacionMixta IS NULL`

---

## Capability: `asociacion-por-nombre-archivo`

### Requirement: A keyless orphan PDF associates to an orphan XML by unambiguous filename containment

When a PDF's own key extraction fails to isolate the four normalized components, the worker
MAY still associate it, but ONLY as a second bounded pass that runs after the exact
four-component pass and never replaces it. For each orphan XML holding a complete
`ClaveComprobante` (RUC emisor, serie, número — tipo not required) and each orphan PDF whose
key is `None`, the system MUST verify that the XML's normalized RUC AND serie AND número each
match, each as a SEPARATE delimited token (token equality after normalization, not substring;
the PDF's stored filename `fact.DocumentoRecibido.NombreArchivo` tokenized on `[^A-Za-z0-9]+`).
RUC, serie, and número MUST each match a DISTINCT token position — a single token that happens
to satisfy two normalizers at once (e.g. `001` normalizing to both serie `001` and número `1`)
does NOT count as two matches. Association MUST use the XML's key as the
sole authority and MUST be written as the FK on both `fact.Procesamiento` rows. The system
MUST refuse the association whenever more than one XML qualifies for a PDF, or more than one
PDF qualifies for an XML, anywhere in the full `listar_huerfanos` set (global bilateral 1:1
exclusivity); a refused document remains orphan for manual review. `tipo` MUST NOT be required
from the filename. `NombreArchivo` stores the raw Gmail attachment name (not the on-disk
sanitized stem); the `[^A-Za-z0-9]+` tokenization subsumes both raw and sanitized delimiters.

(ADR 0017 §"Asociación PDF ↔ XML" amended: the filename backup may be evaluated as
*containment* of an XML's authoritative components. Test level per ADR 0019: pure unit for the
containment/exclusivity function; integration for the association pass. `pytest`.)

#### Scenario: Unambiguous filename containment associates the pair — NEW

- **Given** one orphan XML with complete key RUC `20127765279`, serie `F96X`, número `1230`, and one
  orphan PDF with `clave is None` and stored `NombreArchivo` containing `20127765279`, `f96x`, `00001230`
  as three distinct delimited tokens
- **When** the second association pass runs
- **Then** the pair is associated using the XML's key, and the FK is written on both `fact.Procesamiento` rows

#### Scenario: Tipo token absent or non-standard still associates — NEW

- **Given** a qualifying XML/PDF pair whose filename tipo token is missing or non-standard (`fa`)
- **When** the second association pass runs
- **Then** association still succeeds because RUC, serie, and número are all present as tokens

#### Scenario: More than one qualifying XML refuses association — NEW

- **Given** a PDF whose stored `NombreArchivo` contains the RUC, serie, and número tokens of two distinct orphan XMLs
- **When** the second association pass runs
- **Then** neither XML is associated to that PDF; it stays orphan for manual review

#### Scenario: More than one qualifying PDF refuses association — NEW

- **Given** two orphan PDFs whose stored `NombreArchivo` values both contain a single orphan XML's RUC, serie, and número tokens
- **When** the second association pass runs
- **Then** that XML is not associated; both PDFs stay orphan for manual review

#### Scenario: Near-miss token does not match — NEW

- **Given** an XML número `1230` and a PDF filename whose only numeric token is `12300`
- **When** the second association pass runs
- **Then** no containment match is found (token equality after normalization, not substring)

#### Scenario: One token satisfying two normalizers is not two matches — NEW

- **Given** an XML with serie `001` and número `1`, and a PDF filename whose only relevant token is `001`
  (which normalizes to both serie `001` and número `1`)
- **When** the second association pass runs
- **Then** no containment match is found, because RUC, serie, and número must each match a DISTINCT token position

#### Scenario: XML with an incomplete key is not a candidate — NEW

- **Given** an orphan XML missing serie or número
- **When** the second association pass runs
- **Then** it is never offered as a containment candidate for any PDF

#### Scenario: Exact four-component path is unchanged — NEW (regression guard)

- **Given** a PDF that produces its own complete four-component key matching an XML
- **When** association runs
- **Then** it associates via the existing exact-match path and never enters the containment pass

### Requirement: `_extraer_serie_numero` accepts alphanumeric SUNAT series

PDF key extraction MUST parse SUNAT-shaped alphanumeric series (for example `F96X`), not only
`letter + exactly 3 digits`. Non-SUNAT-shaped garbage MUST still be rejected.

#### Scenario: Alphanumeric serie is parsed — NEW

- **Given** PDF text containing `F96X-00001230`
- **When** the key is extracted
- **Then** serie `F96X` and número `1230` are parsed

#### Scenario: Garbage is still rejected — NEW

- **Given** PDF text containing `ABCDE-` with no valid número
- **When** the key is extracted
- **Then** no serie/número is produced

---

## Capability: `clasificacion-errores-permanente`

### Requirement: Unrecoverable attachment/parse failures are classified `PERMANENTE`, never retried

Per ADR 0010, a corrupt, encrypted, unsupported, or otherwise unparseable attachment, and an invalid
XML, MUST be recorded in `fact.ProcesamientoError` with `Clasificacion='PERMANENTE'` and MUST NOT
consume retry attempts.

#### Scenario: Corrupt PDF is classified PERMANENTE immediately
- **Given** a PDF attachment that fails to open
- **When** processing attempts extraction
- **Then** `fact.ProcesamientoError` gets one row with `Clasificacion='PERMANENTE'`, and
  `fact.ProcesamientoIntentos` does not schedule a next retry

#### Scenario: Invalid XML is classified PERMANENTE
- **Given** an XML attachment that fails schema/well-formedness parsing
- **When** processing attempts extraction
- **Then** the same `PERMANENTE` classification and no-retry behavior apply

---

## Capability: `documentos-sin-pareja`

### Requirement: An unpaired XML promotes normally; an unpaired PDF promotes with a visible, non-blocking warning

Per the proposal's resolved round-2 decisions, a lone XML is sufficient and authoritative; a lone PDF
promotes using OCR data but leaves a flag for item #13's incidencias panel.

#### Scenario: XML without a matching PDF is not blocked
- **Given** an XML with no associated PDF after association runs
- **When** processing completes
- **Then** the document proceeds unblocked toward promotion, with no error raised for the missing PDF

#### Scenario: PDF without a matching XML is not blocked but is flagged
- **Given** a PDF with no associated XML after association runs
- **When** processing completes
- **Then** the document proceeds unblocked, and a visible, non-blocking indicator is left for #13's
  review surface
