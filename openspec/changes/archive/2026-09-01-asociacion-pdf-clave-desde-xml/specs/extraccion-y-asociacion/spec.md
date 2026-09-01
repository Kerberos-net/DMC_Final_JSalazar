# Delta for Extracción y asociación

## ADDED Requirements

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

- GIVEN one orphan XML with complete key RUC `20127765279`, serie `F96X`, número `1230`, and one
  orphan PDF with `clave is None` and stored `NombreArchivo` containing `20127765279`, `f96x`, `00001230`
  as three distinct delimited tokens
- WHEN the second association pass runs
- THEN the pair is associated using the XML's key, and the FK is written on both `fact.Procesamiento` rows

#### Scenario: Tipo token absent or non-standard still associates — NEW

- GIVEN a qualifying XML/PDF pair whose filename tipo token is missing or non-standard (`fa`)
- WHEN the second association pass runs
- THEN association still succeeds because RUC, serie, and número are all present as tokens

#### Scenario: More than one qualifying XML refuses association — NEW

- GIVEN a PDF whose stored `NombreArchivo` contains the RUC, serie, and número tokens of two distinct orphan XMLs
- WHEN the second association pass runs
- THEN neither XML is associated to that PDF; it stays orphan for manual review

#### Scenario: More than one qualifying PDF refuses association — NEW

- GIVEN two orphan PDFs whose stored `NombreArchivo` values both contain a single orphan XML's RUC, serie, and número tokens
- WHEN the second association pass runs
- THEN that XML is not associated; both PDFs stay orphan for manual review

#### Scenario: Near-miss token does not match — NEW

- GIVEN an XML número `1230` and a PDF filename whose only numeric token is `12300`
- WHEN the second association pass runs
- THEN no containment match is found (token equality after normalization, not substring)

#### Scenario: One token satisfying two normalizers is not two matches — NEW

- GIVEN an XML with serie `001` and número `1`, and a PDF filename whose only relevant token is `001`
  (which normalizes to both serie `001` and número `1`)
- WHEN the second association pass runs
- THEN no containment match is found, because RUC, serie, and número must each match a DISTINCT token position

#### Scenario: XML with an incomplete key is not a candidate — NEW

- GIVEN an orphan XML missing serie or número
- WHEN the second association pass runs
- THEN it is never offered as a containment candidate for any PDF

#### Scenario: Exact four-component path is unchanged — NEW (regression guard)

- GIVEN a PDF that produces its own complete four-component key matching an XML
- WHEN association runs
- THEN it associates via the existing exact-match path and never enters the containment pass

### Requirement: `_extraer_serie_numero` accepts alphanumeric SUNAT series

PDF key extraction MUST parse SUNAT-shaped alphanumeric series (for example `F96X`), not only
`letter + exactly 3 digits`. Non-SUNAT-shaped garbage MUST still be rejected.

#### Scenario: Alphanumeric serie is parsed — NEW

- GIVEN PDF text containing `F96X-00001230`
- WHEN the key is extracted
- THEN serie `F96X` and número `1230` are parsed

#### Scenario: Garbage is still rejected — NEW

- GIVEN PDF text containing `ABCDE-` with no valid número
- WHEN the key is extracted
- THEN no serie/número is produced
