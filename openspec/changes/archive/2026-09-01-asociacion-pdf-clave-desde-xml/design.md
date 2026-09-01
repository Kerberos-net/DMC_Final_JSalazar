# Design: Asociación PDF ↔ XML por containment del nombre de archivo

## Technical Approach

Worker-only (Python). A **second bounded association pass**, physically separate from
`comprobante.asociar`, runs on the *residue* of the exact four-component pass: orphan XMLs with a
complete `ClaveComprobante` vs. orphan PDFs with `clave is None`. A new pure function verifies that
the XML's normalized RUC, serie and número each appear as **distinct delimited tokens** in the PDF's
stored `fact.DocumentoRecibido.NombreArchivo`. The XML is the sole authority; ambiguity refuses.
Two orthogonal riders: `_extraer_serie_numero` widened to SUNAT alphanumeric series, and a
**PDF-only** `InboxEvent` re-emit for late associations. Zero .NET changes, zero schema changes.

## Blocking Correction to the Spec Premise (verified)

`fact.DocumentoRecibido.NombreArchivo` is **NOT sanitized**. `documento_repo.insertar_documento`
(`documento_repo.py:150`) writes `a.nombre` — the raw Gmail attachment name, truncated only.
`gmail.sanitizar_nombre_archivo` is applied *exclusively* to the on-disk path stem inside
`gmail.ruta_relativa` (`gmail.py:210-213`). The spec delta and `pdf_texto.py`'s module docstring
both assert "sanitized"; both are wrong. This is corrected deliberately, not silently
(CLAUDE.md rule 1): the design tokenizes on `[^A-Za-z0-9]+`, which subsumes the sanitized alphabet
`-`/`_`/`.` **and** raw-name delimiters (space, parentheses, `#`, `+`), so the mechanism is correct
under either premise. The spec sentence must drop the word "sanitized".

## Architecture Decisions

### D1 — Separate second pass, not folded into `asociar`

| Option | Tradeoff | Decision |
|---|---|---|
| Fold containment into `asociar` | One entry point; but the exact 4-tuple bucket algorithm would gain a second, differently-shaped matching mode | Rejected |
| New `asociar_por_nombre_archivo(candidatos)`; caller filters residue | Exact path byte-untouched (regression guard, spec scenario "Exact four-component path is unchanged") | **Chosen** |

`_asociar_pendientes` computes the residue by excluding every id already paired exactly, then
concatenates both `Par` tuples and reuses the existing write loop unchanged.

### D2 — Tokenization and containment

Split on `[^A-Za-z0-9]+`. Per component, compare **token equality after normalization**, never
substring:

| Component | Token normalization | `85877-20127765279-fa-f96x-00001230.pdf` |
|---|---|---|
| RUC | identity (`clave.ruc_emisor` is already digits-only, 11 chars) | `20127765279` ✓ |
| serie | `normalizar_serie` (uppercase) | `f96x` → `F96X` ✓ |
| número | `normalizar_numero` (strip leading zeros) | `00001230` → `1230` ✓ |

Near-miss `12300` → `12300` ≠ `1230` ✗ (correct refusal). `tipo` is never required — it is the
component issuers mutilate (`fa`).

**The three matches must occupy three distinct token positions.** Reachable counter-example: printed
serie `001` + número `1` — the single token `001` satisfies serie (`001`) *and* número
(`normalizar_numero("001") == "1"`), so a filename carrying only RUC+serie would falsely claim a
número match. Implemented as a system-of-distinct-representatives check over three small index
lists. Rejected alternative (independent set membership) is cheaper but admits that false positive.

### D3 — Exclusivity scope: per-node, mirroring `asociar`

Emit `Par(x, p)` only when `deg(x) == 1` **and** `deg(p) == 1` over the whole residue graph.
Rejected: global kill-switch (any degree > 1 refuses every pair) — over-broad, and not what the
owner's "un huérfano viejo puede suprimir *una* asociación válida" describes. Per-node refusal is
the exact analogue of `asociar`'s per-bucket refusal.

### D4 — `Par` carries no key; `DatosExtraidos` is not backfilled

`asociar_documentos` writes only FKs by `DocumentoRecibidoId` (`procesamiento_repo.py:144-150`) —
nothing to add. The PDF's `fact.DatosExtraidos` identity columns stay NULL: that table is declared
immutable ("registro de trabajo", ADR 0017 §"Dónde vive la evidencia"), the worker has no UPDATE
path for it, and the authoritative values already reach .NET through the XML's `Factura` plus
#25's `DocumentoFactura` merge. Rejected: backfilling from the XML key (would fabricate a second
authority and require a new write path).

### D5 — Re-emit is restricted to `TipoDocumento = 'PDF'` (narrows the spec)

`asociar_documentos` writes the FK on **both** rows, so the XML's `Procesamiento` also transitions
NULL→non-null. Re-emitting the XML event would take
`PoliticaDeDocumentoAsociado.EsDocumentoAsociado` = **false** (`PoliticaDeDocumentoAsociado.cs:17`,
guarded by `EsDocumentoAsociado_EsFalso_CuandoEsXmlConDocumentoAsociadoId`) and fall through to
`PoliticaDePromocion.Decidir` → `PromoverAsync` → a **second `fact.Factura`**. There is no
uniqueness guard on invoice identity: `ExisteIdentidadPreviaAsync` only sets an indicator
(`PromocionBackgroundService.cs:102-105`). The XML event also gains nothing from the FK. Therefore
the candidate query filters `dr.TipoDocumento = 'PDF'`. **This narrows the `inbox-event-publishing`
delta, which is written type-agnostically — the spec needs this amendment.**

### D6 — Re-emit predicate and idempotency

`fact_worker` holds SELECT/INSERT only. `fact.InboxEvent` has `PK (InboxEventId)` identity and **no
UQ on `ProcesamientoId`** (`006_contratos.sql:74-91`), so a second row is legal; `EstadoConsumo`
defaults to `PENDIENTE`, so .NET picks it up. The existing `_INSERTAR_EVENTO` guard is left intact;
a **separate** statement carries a payload-aware guard.

```sql
-- candidate list (new, additive; disjoint from _LISTAR_NO_NOTIFICADOS by construction)
WHERE p.DocumentoAsociadoId IS NOT NULL
  AND dr.TipoDocumento = 'PDF'
  AND NOT EXISTS (SELECT 1 FROM fact.InboxEvent ie
                  WHERE ie.ProcesamientoId = p.ProcesamientoId
                    AND JSON_VALUE(ie.Payload, '$.documento.documentoAsociadoId') IS NOT NULL)
```

The insert repeats that same `NOT EXISTS` as its `WHERE` (atomic, anti-TOCTOU — the discipline
already used by `_INSERTAR_EVENTO` / `upsert_procesamiento`). **Idempotency proof:** the re-emitted
payload has a non-null `documentoAsociadoId`, so the very row just inserted satisfies the `EXISTS`
and removes the `Procesamiento` from the candidate set; a third row is impossible. `JSON_VALUE` in
lax mode returns NULL for both "key absent" and "value is null" — exactly the two "does not reflect
the association" cases. It is a read, so no new grant is needed (ADR 0003 boundary intact).

### D7 — Payload of the re-emit needs no new assembly

Reuse `payload_inbox.construir_payload` with `documento_asociado_id` now populated:
`advertenciasAsociacion` recomputes to `[]` (no `SIN_PAREJA`) automatically
(`payload_inbox.py:90`), `_VERSION` stays 1. The orphan PDF's `ComprobanteParaEvento` is
mostly-NULL, which `PoliticaDePromocion.Decidir` would `Descarta` — irrelevant, because
`EsDocumentoAsociado` fires **before** `Decidir` and is sufficiency-free
(`PromocionBackgroundService.cs:52-58`, `PoliticaDeDocumentoAsociado.cs:10-17`). Confirmed: **zero
.NET changes**.

### D8 — Widened serie regex

`r"\b([A-Za-z](?![A-Za-z]{3}\b)[A-Za-z0-9]{3}|\d{3})\s*-\s*(\d{1,20})\b"`. Accepts `F96X`, `F001`,
`E001`, `001`; rejects `ABCDE-123`, `AB-123`, `1234-123`. The negative lookahead rejects all-letter
tails, so prose collocations like `NOTA-123` / `FACT-123` do not become series — a deliberate
narrowing over the plain `[A-Za-z][A-Za-z0-9]{3}` alternative, at the cost of refusing a
hypothetical all-letter serie (`FAAA`). `normalizar_serie` already uppercases, so `f96x` → `F96X`.

### D9 — ADR 0017 amendment: `adrs/` only

`adrs/0017-…md` is "Revisión 2" (current). `adrs - v2/0017-…md` is the **pre-revision-2 historical
snapshot** (its Estado reads "Decisión nueva"), despite the folder name. Amend only `adrs/`;
editing the snapshot would rewrite history. Replacement for §"Asociación PDF ↔ XML" ¶"Recuperación":

> **Recuperación:** si no es posible extraer los datos del contenido del PDF, el nombre del archivo
> puede usarse como respaldo, siempre que la coincidencia sea **inequívoca**. Dos formas, ambas
> verificadas y ninguna inferida:
>
> 1. **Clave propia desde el nombre**, cuando el nombre respeta la convención SUNAT completa y
>    produce los cuatro componentes; se asocia entonces por la regla exacta del punto 3.
> 2. **Containment contra la clave autoritativa del XML** (revisión 3): cuando el PDF no produce
>    clave propia, un XML huérfano con clave completa puede reclamarlo si su **RUC de emisor, serie
>    y número** normalizados aparecen los tres como **tokens delimitados y distintos** del nombre de
>    archivo del PDF. El tipo de comprobante **no** se exige del nombre: es el componente que los
>    emisores mutilan. La comparación va del XML **hacia** el nombre —se verifica una clave que ya
>    existe, no se adivina una—, y la autoridad sigue siendo el XML.
>
> La exclusividad es **1:1 bilateral sobre todo el conjunto sin pareja**: si más de un XML califica
> para un PDF, o más de un PDF para un XML, **ninguno** se asocia. Un huérfano antiguo no
> relacionado puede así suprimir una asociación válida; se acepta el costo, porque el modo de fallo
> es "queda sin asociar", nunca "asociado al comprobante equivocado".

Plus: append to the Alternativas bullet "*(sigue descartada como mecanismo **único**; la revisión 3
la admite solo como verificación contra una clave XML ya existente)*", and add one Consecuencias
bullet noting the second bounded pass and the PDF-only re-emit.

## Data Flow

    cli_procesamiento._asociar_pendientes
      listar_huerfanos (now + dr.NombreArchivo)
        └─→ asociar((), huerfanos)              exact 4-tuple  ── pares_exactos
              └─→ residue = huerfanos − paired ids
                    └─→ asociar_por_nombre_archivo(residue)    ── pares_por_nombre
                          └─→ asociar_documentos(...)  FK on BOTH fact.Procesamiento rows

    cli_inbox.ejecutar
      listar_no_notificados          (unchanged)      ─→ insertar_evento
      listar_asociacion_no_notificada (new, PDF-only) ─→ insertar_evento_asociacion
                                                            └─→ 2nd fact.InboxEvent (PENDIENTE)
                                                                  └─→ .NET EsDocumentoAsociado
                                                                        → FusionarDocumentoAsync

## File Changes

| File | Action | Description |
|---|---|---|
| `…/smartnet_worker/comprobante.py` | Modify | `Documento.nombre_archivo: str \| None = None` (defaulted → all existing constructors keep compiling); `asociar_por_nombre_archivo`; private `_tokens`, `_nombre_confirma_clave`. Stays pure — no new imports beyond `re`/`collections` (ADR 0019). |
| `…/smartnet_worker/procesamiento_repo.py` | Modify | `_LISTAR_HUERFANOS` + `dr.NombreArchivo`; `listar_huerfanos` passes it through. |
| `…/smartnet_worker/cli_procesamiento.py` | Modify | `_asociar_pendientes`: residue + second pass. |
| `…/smartnet_worker/pdf_texto.py` | Modify | `_SERIE_NUMERO_RE` (D8); fix the "already sanitized" docstring claim. |
| `…/smartnet_worker/inbox_event_repo.py` | Modify | `_LISTAR_ASOCIACION_NO_NOTIFICADA`, `listar_asociacion_no_notificada`, `_INSERTAR_EVENTO_ASOCIACION`, `insertar_evento_asociacion`. Existing statements untouched. |
| `…/smartnet_worker/cli_inbox.py` | Modify | Second batch loop; reuse `_publicar_evento` with an injected insert fn. |
| `adrs/0017-frontera-del-motor-de-extraccion.md` | Modify | D9 amendment; Estado → "Revisión 3". |
| `BACKLOG.md` | Modify | New item #26. |
| `tests/unit/test_comprobante.py`, `test_pdf_texto.py`, `test_procesamiento_repo.py`, `test_cli_procesamiento.py`, `test_cli_inbox.py`, `test_inbox_event_repo.py` | Modify | See Testing Strategy. |
| `tests/integration/test_pyodbc_integracion.py` | Modify | Containment pass + re-emit candidate query against real SQL Server. |

## Interfaces / Contracts

```python
def asociar_por_nombre_archivo(candidatos: Sequence[Documento]) -> tuple[Par, ...]:
    """Segunda pasada acotada (ADR 0017 rev. 3). Candidatos XML: clave completa. Candidatos PDF:
    `clave is None` y `nombre_archivo` presente. Empareja solo con exclusividad 1:1 por nodo."""
```

## Testing Strategy — Strict TDD, RED first

| Spec scenario | File | Level |
|---|---|---|
| Unambiguous containment associates | `test_comprobante.py` | Unit (pure) |
| Tipo token absent/non-standard still associates | `test_comprobante.py` | Unit |
| >1 qualifying XML refuses | `test_comprobante.py` | Unit |
| >1 qualifying PDF refuses | `test_comprobante.py` | Unit |
| Near-miss token (`12300`, `01230`) | `test_comprobante.py` | Unit |
| XML with incomplete key not a candidate | `test_comprobante.py` | Unit |
| Distinct-token rule (serie `001` / número `1`) — design-derived | `test_comprobante.py` | Unit |
| Raw (unsanitized) name with spaces/parens tokenizes — design-derived | `test_comprobante.py` | Unit |
| Exact 4-component path unchanged (regression) | `test_cli_procesamiento.py` | Unit (fake cursor) |
| `F96X-00001230` parsed | `test_pdf_texto.py` | Unit |
| `ABCDE-`, `NOTA-123`, `1234-123` rejected | `test_pdf_texto.py` | Unit |
| `Documento` built with `nombre_archivo` | `test_procesamiento_repo.py` | Unit (fake cursor) |
| Second pass wired into `_asociar_pendientes` | `test_cli_procesamiento.py` | Unit |
| Late association → new event | `test_inbox_event_repo.py` + integration | Unit + Integration |
| Re-emit not repeated once reflected | integration | Integration |
| Association already in first event → no 2nd (regression) | integration | Integration |
| XML side never re-emits (D5) — design-derived | `test_inbox_event_repo.py` | Unit |
| Data-partition boundary respected | integration (runs as `usr_worker`) | Integration |

`pytest` only. No `dotnet test` — .NET is unchanged.

## Threat Matrix

| Boundary | Applicable | Behavior / RED test |
|---|---|---|
| Untrusted external input drives an accounting decision — the supplier controls the attachment filename | **Yes** | A crafted filename could only steal an association from a *specific* orphan XML whose RUC+serie+número it already knows; bilateral exclusivity makes the attack self-defeating (a second qualifying PDF refuses both). RED tests: ">1 qualifying PDF refuses", "near-miss", "distinct tokens". |
| SQL injection via filename | **N/A** | The filename never reaches SQL; it is read out, matched in Python, and the only writes are parameterized FK updates. |
| Routing / shell / subprocess / VCS-PR automation / executable classification | **N/A** | None introduced. |
| Process integration (worker → .NET) | **Yes** | A second `InboxEvent` row per `Procesamiento`. Contained by D5 (PDF-only) + D6 (idempotency proof) + #25's `UQ_DocumentoFactura_DocumentoRecibidoId`. |

## Migration / Rollout

No migration. No schema change. Additive and self-healing: existing orphan pairs associate on the
next `smartnet-procesamiento` run and deliver on the next `smartnet-inbox` run. Rollback = revert
the commit; already-written FKs stay valid (they are indistinguishable from exact-path FKs).

**Size forecast**: ~150 (D1-D4) + ~15 (D8) + ~60 (D5-D7) + ~40 (ADR/BACKLOG) + ~220 tests
≈ **485 lines**. Single PR, within the 800-line budget; above the 400-line review budget —
`single-pr` is the cached strategy, so no chaining.

## Open Questions

- [ ] Spec amendment required (2 items, both deliberate per CLAUDE.md rule 1): drop "sanitized" from
      the `extraccion-y-asociacion` delta; add the PDF-only restriction (D5) to the
      `inbox-event-publishing` delta.
- [ ] `ExisteIdentidadPreviaAsync` sets an indicator, not a constraint — D5 relies on avoiding the
      XML re-emit rather than on a .NET guard. A duplicate-`Factura` guard is a separate concern
      (candidate follow-up), not opened here.
