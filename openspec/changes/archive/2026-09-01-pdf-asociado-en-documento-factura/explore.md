# Exploration: `pdf-asociado-en-documento-factura`

Associated PDF of a SUNAT XML+PDF pair never reaches the SPA document viewer
(`app-visor-documento`): only the XML becomes a `fact.DocumentoFactura`, and the viewer MIME
allow-list (`application/pdf`, `image/png`, `image/jpeg` — `DocumentoContenido.MimeAllowList`)
never renders XML inline.

## Current State (verified against code)

### XML<->PDF pairing lifecycle (worker)

1. Ingesta (`cli_gmail`, #5): each attachment -> one `fact.DocumentoRecibido` (`DESCARGADO`,
   `TipoDocumento` NULL). XML and PDF are independent rows sharing `EmailId`.
2. Extraccion (`cli_procesamiento.py::ejecutar`, #6): ALL XML first, then each PDF
   (`xml_docs`/`pdf_docs`, ADR 0017). Per doc, own transaction: `upsert_procesamiento` -> ONE
   `fact.Procesamiento` per `DocumentoRecibido` (`UQ_Procesamiento_DocumentoRecibido`, schema
   014); `insertar_datos_extraidos` -> `fact.DatosExtraidos`; `fijar_tipo_documento` 'XML'/'PDF'.
   Failed doc -> `Estado='ERROR'`, NO `DatosExtraidos`. CONFIRMED: XML and each PDF each get
   their own `Procesamiento` + `DatosExtraidos`.
3. Asociacion pass (`_asociar_pendientes`, once after batch): `listar_huerfanos` = every
   `Procesamiento` with `DocumentoAsociadoId IS NULL` that HAS a `DatosExtraidos` row
   (`_LISTAR_HUERFANOS` inner-joins it). `comprobante.asociar` matches on 4 normalized key parts
   (RUC emisor, tipo, serie, numero); >1 candidate either side => no association (ADR 0017).
   Matched `Par` -> `asociar_documentos` writes TWO UPDATEs: `DocumentoAsociadoId` on BOTH
   `Procesamiento` rows, same tx (schema 014 `FK_Procesamiento_DocumentoAsociado`,
   `CK_Procesamiento_NoAutoAsociacion`).
   - Candidate set merges this-run docs + prior-run orphans; association can happen in a LATER
     run than extraction.
   - Unmatched/orphan PDF: `DocumentoAsociadoId` stays NULL; still a normal `Procesamiento`.
   - Failed XML: no `DatosExtraidos` => never a candidate => its PDF never associated.
   - PDF-only, no XML: PDF `Procesamiento` stays NULL assoc; if OCR got the 4 required fields it
     promotes as its own `fact.Factura`. THIS PATH MUST KEEP WORKING.

### Inbox event emission (`cli_inbox.py`, #7/#11)

- `inbox_event_repo._LISTAR_NO_NOTIFICADOS`: ONE candidate per `fact.Procesamiento` with no
  `fact.InboxEvent` (`NOT EXISTS ... ie.ProcesamientoId = p.ProcesamientoId`). No pairing
  awareness -> XML and PDF each get their own event.
- `payload_inbox.construir_payload` (`_VERSION = 1`): payload carries a SINGLE `documento`
  object (`documentoRecibidoId`, `tipoDocumento`, `documentoAsociadoId`, `nombreArchivo`,
  `mimeType`, `rutaRelativa`, `tamanoBytes`) + `comprobante`, `evidencia`, `afectacionMixta`,
  `camposNoExtraidos`, `advertenciasAsociacion` (`["SIN_PAREJA"]` iff `documentoAsociadoId IS
  NULL`). The associated doc's nombreArchivo/mimeType/rutaRelativa/tamanoBytes are NOT in the
  payload — only the bare `documentoAsociadoId` (a `DocumentoRecibidoId`).
- `insertar_evento`: idempotent `INSERT...WHERE NOT EXISTS` per `ProcesamientoId`; an event is
  written once, never updated.
- Timing: `cli_procesamiento` and `cli_inbox` run on INDEPENDENT external schedules. If
  association is in a later run, the XML event may already be emitted + promoted carrying
  `documentoAsociadoId: null` + `SIN_PAREJA`, which then goes stale.

### Promotion (.NET `SmartNet.Inbox.*`)

- `PromocionBackgroundService.ProcesarPendientesAsync`: `foreach` over `SELECT ... WHERE
  EstadoConsumo='PENDIENTE'` — NO ORDER BY (XML vs PDF event order non-deterministic).
- `PoliticaDePromocion.Decidir`: pure; `Promueve` iff `EstadoProcesamiento=="COMPLETADO"` and
  TipoComprobante+Monto+Moneda+FechaEmision all present; else `Descarta`.
- `SqlPromocionRepository.PromoverAsync`: one tx — `InsertarFacturaAsync` (`fact.Factura` keyed
  by `ProcesamientoId`, `UQ_Factura_Procesamiento`), `InsertarExtraccionesAsync`,
  `InsertarDocumentoFacturaAsync` (ONE `fact.DocumentoFactura` per event, keyed by
  `DocumentoRecibidoId`, `UQ_DocumentoFactura_DocumentoRecibidoId`), `MarcarPromovidoAsync`.
- Idempotent-catch on 2601/2627: `UQ_Factura_Procesamiento` -> resolve existing FacturaId;
  `UQ_DocumentoFactura_DocumentoRecibidoId` -> skip. NEITHER fires for the PDF event (its
  `ProcesamientoId` and `DocumentoRecibidoId` differ from the XML's).
- `PosibleDuplicado` = `ExisteIdentidadPreviaAsync` (RUC+tipo+numero on a non-DESCARTADA
  `fact.Factura`).

Net effect for a pair, both events promoted independently:

- (a) PDF OCR got all 4 fields -> PDF event `Promueve` -> SECOND `fact.Factura`
  (`PosibleDuplicado=1`), PDF's `DocumentoFactura` on that duplicate, not the XML's factura.
- (b) PDF OCR incomplete (common) -> `Descarta` -> event `DESCARTADO`, PDF lost from domain
  side, no `DocumentoFactura`.
- PDF event before XML event -> `PosibleDuplicado` lands on the wrong side.

### Viewer (SPA + API)

- `GET /api/facturas/{id}/documentos` (`ObtenerDocumentosAsync`): union `fact.DocumentoFactura`
  (INGESTA) + non-deleted `fact.AdjuntoManual` (MANUAL), by fecha. Only rows on THAT FacturaId.
- `GET /api/documentos/{id}/contenido`: `ContentTypeFor` allow-list
  `application/pdf`/`image/png`/`image/jpeg`; else `application/octet-stream` (download, never
  inline). XML => octet-stream.
- `VisorDocumento`: same-origin `<iframe>` for `seleccionado()` (defaults `documentos[0]` by
  fecha), selector when >1.
- PDF row missing (b) or on a duplicate factura (a) => XML factura shows only the XML row =>
  octet-stream => viewer empty/download-only. Once the PDF row is on the right factura,
  `application/pdf` renders inline with NO viewer change.

### Constraints

- ADR 0003: `usr_api` DENY SELECT on `fact.DocumentoRecibido` AND `fact.Procesamiento`. .NET
  cannot read the PDF metadata directly nor map `DocumentoRecibidoId`->`ProcesamientoId`.
  Non-derivable metadata must arrive via the InboxEvent payload.
- .NET CAN SELECT `fact.DocumentoFactura` + `fact.Factura` (016 grants; used by
  `ExisteIdentidadPreviaAsync`). `fact.DocumentoFactura.DocumentoRecibidoId` is already stored —
  enables a partner lookup with NO payload/schema change.
- ADR 0019/0017: `payload_inbox.py` + `PoliticaDePromocion`/`ConstruccionDeFactura` are pure,
  covered by boundary contract tests (`test_payload_inbox_contract.py`, `PayloadInboxParser`,
  golden fixtures). Payload shape change => `_VERSION` bump + coordinated worker/API deploy +
  both-side contract test updates.
- NO REGLAS.md rule / accounting invariant touched — document projection only. Removing the
  spurious duplicate factura IMPROVES `PosibleDuplicado` accuracy.
- Schema is versioned SQL (ADR 0016).

## Approaches

### Design A — one InboxEvent per comprobante, payload carries both documents

Worker emits one event for the pair (XML primary), payload `documentos:[primary, associated]`
with full metadata each; suppress the paired PDF's own event (`_LISTAR_NO_NOTIFICADOS` excludes
a `Procesamiento` whose `DocumentoAsociadoId` -> an XML). Promotion projects N `DocumentoFactura`
for one `Factura`.

- Pros: clean model (one event = one factura); `advertenciasAsociacion` never stale; no
  wrong-side `PosibleDuplicado`; viewer gets both rows.
- Cons: BREAKING payload contract (`_VERSION` 1->2), coordinated worker+API deploy, boundary
  tests rewritten both sides; worker joins partner `DocumentoRecibido` columns; still races an
  XML event already emitted before association (needs fallback); `EventoInbox`/parser/promotion
  all change.
- Effort: High (~250-400 LOC + fixtures + coordinated deploy).

### Design B (RECOMMENDED) — per-Procesamiento events, de-duplicate at promotion, no contract change

When event `DocumentoAsociadoId` non-null, .NET resolves partner's promoted factura:
`SELECT f.FacturaId FROM fact.DocumentoFactura df JOIN fact.Factura f ON f.FacturaId=df.FacturaId
WHERE df.DocumentoRecibidoId=@documentoAsociadoId AND f.Estado<>'DESCARTADA'` (within `usr_api`
grants; uses existing stored `DocumentoRecibidoId`). Found -> project this event's
`DocumentoFactura` onto that FacturaId, mark event `PROMOVIDO`, no second `Factura`, skip
sufficiency. Not found (partner not yet promoted) -> DEFER: leave event `PENDIENTE`, self-heal
next cycle.

- Pros: NO payload change, no `_VERSION` bump, no schema change, no coordinated deploy; smallest
  surface; PDF-only path untouched (only fires when `DocumentoAsociadoId!=null`); kills outcomes
  (a) and (b); removes spurious duplicates.
- Cons: relies on defer/self-heal for ordering; a paired PDF whose XML never promotes (malformed
  XML -> `Descarta`; rare, and such XMLs are usually never associated since association needs
  `DatosExtraidos`) is stranded -> needs bounded fallback; "associated PDF never creates its own
  factura" must be explicit; stale `SIN_PAREJA` on an early XML event not repaired (cosmetic).
- Effort: Low-Medium (~80-150 LOC + unit/boundary tests, single-runtime).

### Design C — payload carries associated-document metadata only, suppress PDF event

Extend XML event payload with a `documentoAsociado` sub-object (4 metadata fields), suppress
paired PDF event, promotion projects 2 `DocumentoFactura` from one event but still one `Factura`.

- Pros: no second-factura risk; simpler than A; viewer fixed.
- Cons: still a versioned payload contract change + coordinated deploy + contract tests both
  sides; same association-vs-emission race as A; asymmetric payload.
- Effort: Medium (~180-250 LOC + fixtures + coordinated deploy).

### Design D — add XML to the viewer MIME allow-list

Not a real fix: rejected by design D2 threat matrix (XML/HTML/SVG XSS vectors in same-origin
iframe vs session cookie) and doesn't fix (a)/(b). Note only.

## Recommendation

Design B, plus deterministic ordering handling (defer paired-PDF events whose partner factura
isn't found yet; make "an associated PDF never promotes as its own factura" explicit). Fixes the
bug end to end with NO cross-runtime contract change, no `_VERSION` bump, no schema change;
leverages the `DocumentoRecibidoId` already in `fact.DocumentoFactura`; keeps the PDF-only path
unchanged; shrinks reviewer surface and deploy risk. Design A is the cleaner long-term model —
raise with owner as future direction — but its payload break is disproportionate here.

## Owner rulings needed

1. Semantic model: per-document events + de-dupe at promotion (B) vs one event per comprobante
   with versioned payload (A)?
2. Ordering/defer: paired PDF event seen before its XML promoted -> defer (`PENDIENTE`,
   self-heal) vs promote-and-merge-later? Defer acceptable even if a permanently-non-promoting
   XML strands its PDF?
3. Paired XML fails structural promotion (`Descarta`): associated PDF promotes on its own as
   fallback, or discarded/deferred with the XML?
4. Stale `advertenciasAsociacion: ["SIN_PAREJA"]` on an XML event emitted before a later
   association — leave (cosmetic) or repair?
5. Backfill of existing pre-fix duplicate/lost facturas — leave untouched (no migration, ADR
   0003) or one-off cleanup?
6. Viewer default selection when a factura has both XML and PDF — default to the renderable
   (PDF) vs earliest-fecha? Small SPA tweak or out of scope.

## BACKLOG relationship

Gap spans #6 (association), #7 (inbox/promotion/DocumentoFactura projection), #12 (detail +
viewer). NO existing item covers it — same discovered-during-implementation pattern as #19/#24.
Recommend a NEW backlog item (sibling to #24), not amending a closed one.

## Risks

- Payload contract change (A/C) forces coordinated worker+API deploy + rewritten ADR 0019
  boundary tests both sides.
- Non-deterministic event processing order (`foreach` over unordered SELECT) already drives
  duplicate/PosibleDuplicado outcomes.
- `cli_procesamiento` / `cli_inbox` independent schedules; association lags event emission; XML
  event may be `PROMOVIDO` with `SIN_PAREJA` when PDF later associates.
- Design B defer: paired PDF stranded if its XML never promotes; bounded fallback needed.
- reprocesar idempotency (`CommandQueue` -> `Procesamiento` back to `PENDIENTE`): re-emitted
  events must not double-project; `UQ_DocumentoFactura_DocumentoRecibidoId` guards — verify the
  merge path also hits it.
- Design B lookup must stay within `fact.DocumentoFactura` + `fact.Factura` (it does).

## Ready for Proposal

Yes, once the owner rules on decisions 1-3 (2 and 3 can default: defer; discard-with-XML).
Recommend Design B. Decisions 4-6 are non-blocking.
