# Exploration: asociacion-pdf-clave-desde-xml

Follow-up to BACKLOG #6 (Extracción y asociación); sibling of shipped #25
(`pdf-asociado-en-documento-factura`). Investigation only.

## Problem confirmed against code

XML+PDF pair never associates: `fact.Procesamiento.DocumentoAsociadoId` stays NULL both sides →
PDF never becomes `fact.DocumentoFactura` → never reaches detail viewer.

### Confirmed facts

- `comprobante.asociar` (comprobante.py:113): groups `Documento` by frozen 4-tuple
  `ClaveComprobante(ruc_emisor,tipo,serie,numero)`, emits `Par` only when a key bucket has
  exactly 1 XML + 1 PDF. `nuevos`/`huerfanos` merged before grouping (symmetric). `clave=None`
  dropped. Pure (ADR 0019): no email metadata is even a parameter.
- `_asociar_pendientes` (cli_procesamiento.py:225): runs once at end of every run, after all XML
  then all PDF. `listar_huerfanos` → `asociar((),huerfanos)` → per Par: `obtener_procesamiento_id`
  both sides + `asociar_documentos` (2 UPDATEs, FK on both Procesamiento rows, one tx).
- `procesamiento_repo._LISTAR_HUERFANOS` (repo:73): `SELECT dr.TipoDocumento, de.RucProveedor,
  de.TipoComprobante, de.Numero ... WHERE p.DocumentoAsociadoId IS NULL` INNER JOIN
  `fact.DatosExtraidos` (no DatosExtraidos row ⇒ not a candidate). `clave` built only
  `if ruc and tipo and numero` via `construir_clave`.
- `pdf_texto.extraer(texto, nombre_archivo, ruc_propio=None)` (pdf_texto.py:86): content-first
  (`_clave_desde_texto` needs ruc_emisor AND serie_numero AND tipo) then filename fallback
  (`_respaldo_desde_nombre_archivo`) else `clave=None`.
- `_datos_desde_pdf` (cli_procesamiento.py:268): persists ruc/tipo/numero as None when
  `e.clave is None` → orphan row NULL identity cols → `listar_huerfanos` builds `clave=None` →
  PDF never in a bucket. Exact failure path.
- `fact.Configuracion` RUC read by `_leer_ruc_propio` (cli_procesamiento.py:97,285), threaded as
  `ruc_propio`. NULL here → `_extraer_ruc_emisor` multi-RUC disambiguation returns None.

### Real invoice: `85877-20127765279-fa-f96x-00001230.pdf`

`_NOMBRE_ARCHIVO_RE = ^(\d{11})-(\d{1,2})-([A-Za-z0-9]{1,4})-(\d{1,20})\.pdf$`. Fails for 2
independent reasons: (1) leading extra segment `85877-` (pattern anchors `^(\d{11})`, `85877` is
5 digits); (2) non-numeric tipo `fa` (group 2 is `\d{1,2}`). Serie group `[A-Za-z0-9]{1,4}` WOULD
accept `f96x`.

Content path also fails on 2 of 3 sub-extractions:

- `_extraer_ruc_emisor`: emitter RUC `R.U.C. 20127765279` probably in OCR text but buyer RUC also
  printed → ≥2 RUCs, `ruc_propio` NULL → returns None (ADR 0017 no non-inferential choice). Most
  likely cause of NULL RUC.
- `_extraer_serie_numero`: regex `\b([A-Za-z]\d{3}|\d{3})\s*-\s*(\d{1,20})\b` only accepts
  letter+exactly-3-digits (`F001`). Series is `F96X` (valid SUNAT alphanumeric) → cannot parse.
  Tests only exercise `F001`.
- `_extraer_tipo`: "FACTURA" keyword likely present → "01". Works.

Net: only place all 4 normalized components co-occur is the filename. XML carries authoritative
complete key.

### Sequencing gotcha

`fact.InboxEvent` emitted once per `fact.Procesamiento` (`inbox_event_repo._LISTAR_NO_NOTIFICADOS`
+ NOT EXISTS), never re-emitted; payload `documentoAsociadoId` read from `p.DocumentoAsociadoId`
at emission. #25's .NET merge keys on it. Same-run XML+PDF: FK set before `cli_inbox` → OK. PDF
event emitted run N, XML associates run N+1: PDF event already NULL, never revisited → #25 never
merges. Pre-existing gap; every option inherits it.

## ADR 0017 stance (verbatim highlights)

Key = RUC emisor+tipo+serie+número; PDF associates "únicamente si los cuatro componentes
normalizados coinciden de forma exacta". Filename fallback allowed "siempre que la coincidencia
sea inequívoca". Email subject/sender/date/attachment-position "no establecen asociación en ningún
caso". "Nunca se asigna a un comprobante por proximidad o descarte." Alternatives section already
REJECTED "asociar por convención de nombre SUNAT únicamente" (as sole mechanism, not as fallback).
CLAUDE.md rule 1: ADR change = deliberate documented amendment, never silent.

## Approaches

### A — Partial-key match tipo+serie+numero, adopt XML RUC

Fixes THIS invoice? No alone — PDF can't produce serie F96X from text nor filename; needs
stacking on C. ADR impact: direct contradiction of "cuatro componentes exactos"; amendment for
bounded 3-component + XML-RUC-adoption rule. Mis-assoc risk: HIGHER than 4-tuple — RUC is exactly
the issuer-disambiguator; 2 XMLs sharing tipo+serie+numero w/ different RUC must refuse; global
candidate set (cross-email/run). Purity: OK if `asociar` gets separate 2nd pass; needs `Documento`
to carry partial key (`ClaveParcial`), `listar_huerfanos` stop collapsing partials to None. Size
M (~120-180 LOC), worker-only.

### B — Extraction hint: feed sibling XML RUCs into `pdf_texto.extraer`

Positively select the found-in-PDF RUC matching a sibling XML (instead of `ruc_propio` exclusion).
Fixes THIS invoice? Only if emitter RUC literally in OCR text AND serie parseable (still blocked
by `_extraer_serie_numero`); benefit unverifiable without real OCR. Never fabricates — chooses
among `_RUC_RE` matches only; strong ADR story (disambiguation, not inference); smallest/no
amendment (clarification note prudent). Risk low. Purity: `extraer` gains
`rucs_candidatos: Sequence[str]=()`. Plumbing bigger than looks: PDFs currently processed with
zero knowledge of sibling XML; `run` must accumulate XML RUCs (batch DatosExtraidos + existing
orphan XMLs) before PDF loop, thread through `_procesar_documento`→`_datos_desde_pdf`. Size M
(~100-160 LOC).

### C — Looser filename fallback (leading segment + alpha tipo table)

Widen `_NOMBRE_ARCHIVO_RE` to optionally skip leading `\d+-` and accept alpha tipo via small map
(fa→01, bo→03, nc→07, nd→08); keep alphanumeric serie. Fixes THIS invoice? YES — only option
making all 4 components available for this exact file. ADR impact moderate: ADR already blesses
filename backup + already rejected filename-only as sole mechanism; amendment must whitelist exact
shapes, keep all-or-nothing. Risk: broad regex → spurious 4-tuple that then exact-matches wrong
XML; alpha-tipo map is riskiest part (guessing issuer conventions). Purity: contained in
`pdf_texto.py`, no signature change. Size S (~50-90 LOC). Fold in `_extraer_serie_numero`
alphanumeric-serie fix here.

### D — XML-authoritative key confirmed against sanitized PDF filename (RECOMMENDED core)

In IO layer / new `comprobante.py` pure fn: for each orphan XML with complete key and each PDF
orphan with `clave is None`, check all three of XML's normalized RUC, serie, número appear as
delimited tokens in PDF's sanitized `dr.NombreArchivo` (sanitized by #5). If exactly one XML
matches a PDF and vice versa (bilateral 1:1) → associate with XML's key as authority. Fixes THIS
invoice? YES (`20127765279`, `f96x`, `00001230` all `-`-delimited tokens). ADR impact: literal
reading of "nombre de archivo como respaldo, coincidencia inequívoca" — match is XML→filename
(verified vs authority), not filename→guessed-key (inferred). Amendment paragraph: filename backup
may be evaluated as containment of XML's authoritative components, bilateral exclusivity. Smaller
conceptual jump than A (no partial-key algebra) or C (no tipo table). Mis-assoc risk: RUC(11
digits)+número in one filename already strong fingerprint; +serie makes coincidental cross-issuer
collision extremely unlikely; bilateral exclusivity refuses ambiguity. Tipo deliberately not
required from filename (the mangled component). Purity: containment check pure, new fn in
`comprobante.py` (module already implicitly depends on sanitized filename via the filename-fallback
precedent). Plumbing: `_LISTAR_HUERFANOS` add `dr.NombreArchivo`; `Documento` gains
`nombre_archivo: str|None`; new 2nd pass called by `_asociar_pendientes`; `asociar_documentos`
unchanged. Size M (~110-160 LOC), worker-only.

## Interaction with shipped #25

#25 branch `PoliticaDeDocumentoAsociado.EsDocumentoAsociado` (`DocumentoAsociadoId != null &&
TipoDocumento == "PDF"`) is policy-independent: merges PDF's `fact.DocumentoFactura` onto XML's
factura with NO sufficiency check, defers if partner not promoted, no self-promote if XML
discarded. Associated PDF with incomplete OCR already handled correctly. NO CONFLICT. Consequence:
each new pair → exactly one additional #25 merge (intended). Only new load: more
`fact.DocumentoFactura` insert-merges hitting `UQ_DocumentoFactura_DocumentoRecibidoId` (already
idempotent 2601/2627 catch). Sequencing gotcha limits cross-run delivery regardless of option.

## Recommendation

D as core mechanism + fold in C's orthogonal fixes (alphanumeric-serie regex fix). D is the only
option that (a) rescues the observed invoice, (b) stays closest to ADR 0017's "filename backup,
unambiguous" language (verification vs XML authority, not inference), (c) needs no partial-key
algebra or issuer-convention table. `_extraer_serie_numero` alphanumeric fix and optionally B's
RUC hint are independently correct future-hit-rate improvements, can ride along or be a follow-up.
Reject A as primary (doesn't fix alone; weakens the RUC issuer-disambiguator). Keep B as low-risk
unverifiable secondary.

## Owner rulings needed (blocks proposal)

1. ADR 0017 amendment shape: (a) D filename-as-containment-of-XML-authoritative-components +
   bilateral 1:1 [recommended]; (b) A 3-component + adopt XML RUC; (c) C widen SUNAT filename
   shapes + alpha-tipo table; (d) B only (accept invoice stays unassociated for now).
2. Exclusivity scope: confirm new match must refuse when >1 XML OR >1 PDF qualifies anywhere in
   the global `listar_huerfanos` set (consistent with `asociar`), accepting a stale unrelated
   orphan can suppress a valid association (fails safe → manual resolution surface, ADR 0017
   accepts this cost).
3. `_extraer_serie_numero` alphanumeric SUNAT series (e.g. `F96X`): fix in this change or split
   to follow-up?
4. Cross-run delivery gap (association after PDF InboxEvent already emitted NULL → #25 never
   merges): (a) accept gap; (b) re-open InboxEvent emission for Procesamiento whose
   DocumentoAsociadoId transitioned NULL→non-null (new `cli_inbox` candidate query); (c) out of
   scope, own backlog item.
5. (only if C chosen) Who ratifies tipo map fa→01/bo→03/nc→07/nd→08 across the mailbox's actual
   providers?

## BACKLOG

Recommend NEW item #26 (sibling to #25, lineage #19→#24, #25):

> #26 — Asociación PDF↔XML cuando el PDF no produce clave propia. Cuando la extracción del PDF
> (texto y respaldo de nombre) no aísla los cuatro componentes —RUC ausente por multi-RUC sin
> EMPRESA.RUC, serie alfanumérica no estándar, nombre con segmento extra—, el worker asocia el
> PDF al XML huérfano cuyos componentes autoritativos (RUC + serie + número) aparecen de forma
> inequívoca en el nombre de archivo saneado del PDF, con exclusividad 1:1 bilateral. Enmienda
> deliberada a ADR 0017 §"Asociación PDF ↔ XML". Incluye ensanchar `_extraer_serie_numero` a
> series alfanuméricas SUNAT. No reabre el núcleo contable ni el payload del InboxEvent.

## Ready for Proposal

No — blocked on owner rulings 1-4. All four candidate mechanisms are worker-only,
ADR-0019-pure-compatible, single-PR size.

## Test surface (all options)

`tests/unit/test_comprobante.py` (new pass: happy / 2-XML ambiguous / 2-PDF ambiguous /
near-miss), `tests/unit/test_pdf_texto.py` (serie alphanumeric; filename shapes accepted +
rejected; multi-RUC hint for B), `tests/unit/test_procesamiento_repo.py` (Documento build w/ new
column), `tests/unit/test_cli_procesamiento*.py` (threading for B), `tests/integration/`
association pass.
