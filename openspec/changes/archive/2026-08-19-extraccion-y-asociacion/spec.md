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
