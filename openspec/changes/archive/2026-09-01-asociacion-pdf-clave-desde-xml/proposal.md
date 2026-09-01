# Proposal: PDF↔XML association when the PDF yields no key of its own

## Intent

A real SUNAT pair (`85877-20127765279-fa-f96x-00001230.pdf` + its XML) never
associates: `fact.Procesamiento.DocumentoAsociadoId` stays NULL on both sides,
so the PDF never becomes a `fact.DocumentoFactura` and never reaches the detail
viewer. The PDF's own key extraction fails three independent ways — emitter RUC
unresolved (multi-RUC OCR + `fact.Configuracion.RUC` NULL), alphanumeric series
`F96X` unparseable by `_extraer_serie_numero`, and a mangled filename with an
extra leading segment and alpha `tipo`. Follow-up to BACKLOG #6; sibling of
shipped #25. Lineage #19→#24→#25→#26.

## Scope

### In Scope
- New pure fn in `comprobante.py`: for each orphan XML with a complete
  `ClaveComprobante` and each orphan PDF with `clave is None`, verify the XML's
  normalized RUC + serie + número each appear as delimited tokens in the PDF's
  sanitized `fact.DocumentoRecibido.NombreArchivo` (sanitized by #5). Associate
  on **bilateral 1:1 exclusivity**, XML key as authority. `tipo` deliberately
  not required from the filename (the mangled component).
- **Global exclusivity**: refuse when >1 XML OR >1 PDF qualifies anywhere in the
  full `listar_huerfanos` set (consistent with `comprobante.asociar`). A stale
  unrelated orphan may suppress a valid association — fails safe to manual review.
- `procesamiento_repo._LISTAR_HUERFANOS` + `Documento`: carry `nombre_archivo`.
- `cli_procesamiento._asociar_pendientes`: second association pass wiring.
- `pdf_texto._extraer_serie_numero`: widen to alphanumeric SUNAT series (`F96X`),
  not only letter + exactly 3 digits. Orthogonal, small, rides in this change.
- Re-emit `fact.InboxEvent` on `DocumentoAsociadoId` NULL→non-null transition:
  new candidate query in `cli_inbox` / `inbox_event_repo`. Constraint:
  `fact_worker` has SELECT/INSERT only on `fact.InboxEvent` (no UPDATE — that is
  `fact_api`), and the current `_INSERTAR_EVENTO` `WHERE NOT EXISTS (…
  ProcesamientoId = ?)` guard blocks re-emit. Schema permits multiple rows per
  `ProcesamientoId` (PK is identity, no UQ). Design phase resolves the exact
  mechanism (likely a 2nd `InboxEvent` row whose payload now carries the
  association).
- `adrs/0017-frontera-del-motor-de-extraccion.md` amendment (draft paragraph below).
- Tests (see below).
- New BACKLOG item #26.

### Out of Scope
- **.NET promotion side.** Shipped #25 (`PoliticaDeDocumentoAsociado.EsDocumentoAsociado`
  = `DocumentoAsociadoId != null && TipoDocumento == "PDF"`) already merges an
  associated PDF's factura with no sufficiency check and idempotent
  `UQ_DocumentoFactura_DocumentoRecibidoId` (2601/2627 catch). Confirmed
  non-conflicting; each new association → exactly one additional idempotent #25
  merge.
- `InboxEvent` payload **shape** — `_VERSION` stays 1; the re-emit reuses the
  existing payload builder, only the trigger is new.
- DB schema / `.sql` files.
- Accounting core (`REGLAS.md`, `AsientoContable`).
- `_extraer_ruc_emisor` / multi-RUC logic (Option B — rejected as primary).
- Alpha-`tipo` filename table (Option C — rejected).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `extraccion-y-asociacion`: adds a second, bounded association pass — an orphan
  PDF with no key of its own associates to the orphan XML whose authoritative
  RUC + serie + número appear unambiguously as delimited tokens in the PDF's
  sanitized filename, with bilateral 1:1 exclusivity; `_extraer_serie_numero`
  accepts alphanumeric SUNAT series.
- `inbox-event-publishing`: a `fact.Procesamiento` whose `DocumentoAsociadoId`
  transitioned NULL→non-null after its `InboxEvent` was emitted becomes a new
  emission candidate, so downstream promotion sees the association.

## Approach

Exploration Option D as core mechanism + Option C's orthogonal serie fix folded
in. D stays closest to ADR 0017's "filename backup, unambiguous match" language:
the match runs XML→filename (containment verified against the XML authority), not
filename→guessed key (inference). No partial-key algebra (Option A), no
issuer-convention `tipo` table (Option C's risky part). The containment check is
pure (ADR 0019); the second pass lives in the IO layer alongside
`_asociar_pendientes` and reuses `asociar_documentos` unchanged.

### ADR 0017 amendment (draft)

Add to §"Asociación PDF ↔ XML", after the filename-backup paragraph:

> Cuando la extracción del PDF —contenido y respaldo de nombre— no aísla los
> cuatro componentes normalizados, el respaldo por nombre de archivo puede
> evaluarse de forma inversa: como *containment* de los componentes
> autoritativos de un XML huérfano. Si el RUC del emisor, la serie y el número
> normalizados de un XML con clave completa aparecen los tres como tokens
> delimitados en el nombre de archivo saneado del PDF, y esa correspondencia es
> **exclusiva 1:1 en ambos sentidos** sobre todo el conjunto de huérfanos, el
> PDF se asocia a ese XML adoptando su clave. El tipo de comprobante no se exige
> del nombre. Sigue rigiendo el todo-o-nada y la regla de que cualquier
> ambigüedad rechaza la asociación: la asociación sigue siendo verificada contra
> la autoridad del XML, nunca inferida por proximidad o descarte.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `worker/.../comprobante.py` | Modified | New pure containment/exclusivity fn |
| `worker/.../cli_procesamiento.py` | Modified | Second association pass in `_asociar_pendientes` |
| `worker/.../procesamiento_repo.py` | Modified | `_LISTAR_HUERFANOS` + `Documento.nombre_archivo` |
| `worker/.../pdf_texto.py` | Modified | `_extraer_serie_numero` alphanumeric series |
| `worker/.../cli_inbox.py` | Modified | Re-emit candidate wiring |
| `worker/.../inbox_event_repo.py` | Modified | NULL→non-null transition candidate query |
| `adrs/0017-...md` | Modified | Deliberate amendment (draft above) |
| `BACKLOG.md` | Modified | New item #26 |
| worker tests | New/Modified | See Test strategy |

## Test strategy (Strict TDD, `pytest`, ADR 0019 pure/unit + integration)

- `test_comprobante.py`: containment happy path; 2-XML ambiguous → refuse;
  2-PDF ambiguous → refuse; near-miss token (substring not delimited) → no
  match; XML with incomplete key → not a candidate.
- `test_pdf_texto.py`: alphanumeric serie (`F96X`) accepted; still rejects garbage.
- `test_procesamiento_repo.py`: `Documento` built with `nombre_archivo`.
- `test_cli_procesamiento*.py`: second-pass wiring.
- `test_cli_inbox.py` / `test_inbox_event_repo.py`: re-emit candidate query
  (transitioned `DocumentoAsociadoId`, no reflecting event).
- `tests/integration/`: association pass end to end.
- No `dotnet test` — this change must not touch the .NET promotion side.

## BACKLOG item #26 (draft)

> #26 — Asociación PDF↔XML cuando el PDF no produce clave propia. Cuando la
> extracción del PDF (texto y respaldo de nombre) no aísla los cuatro
> componentes —RUC ausente por multi-RUC sin `Configuracion.RUC`, serie
> alfanumérica no estándar, nombre con segmento extra—, el worker asocia el PDF
> al XML huérfano cuyos componentes autoritativos (RUC + serie + número)
> aparecen de forma inequívoca en el nombre de archivo saneado del PDF, con
> exclusividad 1:1 bilateral. Enmienda deliberada a ADR 0017 §"Asociación PDF ↔
> XML". Incluye ensanchar `_extraer_serie_numero` a series alfanuméricas SUNAT y
> re-emitir el `InboxEvent` cuando `DocumentoAsociadoId` transiciona NULL→no-NULL.
> No reabre el núcleo contable ni el payload (`_VERSION` 1) del `InboxEvent`.
> Linaje #19→#24→#25→#26.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Mis-association to the wrong XML | Low | RUC (11 digits) + número + serie as delimited tokens in one filename is a strong fingerprint; bilateral 1:1 exclusivity refuses any ambiguity |
| Re-emit creates a 2nd `InboxEvent` row per `ProcesamientoId` | Med | Whole promotion chain is idempotent — `UQ_DocumentoFactura_DocumentoRecibidoId` (2601/2627 catch) guards the #25 merge; payload builder + `_VERSION` unchanged |
| Global exclusivity suppressed by a stale unrelated orphan | Med | Accepted by owner — fails safe to the manual-review surface (ADR 0017 accepts this cost) |
| `_extraer_serie_numero` widening over-matches | Low | Constrain to SUNAT-shaped alphanumeric series; add accept + reject tests |
| Design cannot find a clean re-emit mechanism within `fact_worker` grants | Low | Schema already permits multiple rows per `ProcesamientoId`; design phase owns the exact query/guard |

## Rollback Plan

Single PR, worker-only. Revert the merge commit. No schema or data migration, no
.NET change, no `InboxEvent` payload version bump — nothing to unwind beyond the
worker code and the ADR/BACKLOG text. Already-created associations remain valid
(they are correct `DocumentoAsociadoId` values); only the second pass stops
producing new ones.

## Dependencies

- Shipped #25 (`pdf-asociado-en-documento-factura`, archived) — consumes the new
  associations; confirmed non-conflicting.
- BACKLOG #5 filename sanitization — the containment check relies on it.

## Success Criteria

- [ ] The observed pair (`20127765279` / `f96x` / `00001230`) associates in the
      integration association pass.
- [ ] `comprobante` second pass refuses on 2-XML and 2-PDF ambiguity.
- [ ] `_extraer_serie_numero` parses `F96X` and still rejects non-SUNAT garbage.
- [ ] A NULL→non-null `DocumentoAsociadoId` transition produces a new
      `InboxEvent` candidate; #25 then merges idempotently.
- [ ] ADR 0017 amendment and BACKLOG #26 committed; no .NET files touched.
- [ ] Diff within the 800-line budget; single PR.

## Rough size estimate

~110–160 LOC for D's core + ~20–40 LOC for the serie fix + ~40–70 LOC for the
re-emit candidate query, plus tests. Comfortably within the 800-line review
budget as a single PR.
