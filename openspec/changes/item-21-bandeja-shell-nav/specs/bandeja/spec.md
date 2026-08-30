# Delta for bandeja

## ADDED Requirements

### Requirement: Bandeja rows carry comprobante identification fields

Each `GET /api/bandeja` row MUST include the proveedor display name (resolved via
a `dbo.Proveedor` read), the `TipoComprobante` code (the raw code only — no
display-name mapping is performed server-side), `Numero`, `TotalOrig`, `Moneda`,
and `FechaEmision`. For rows with `origen: "INCIDENCIA"` (no backing
`fact.Factura`), every one of these fields MUST be null; the endpoint MUST NOT
fail or omit the row.

#### Scenario: FACTURA row includes comprobante fields

- GIVEN a `fact.Factura`-derived row whose proveedor exists in `dbo.Proveedor`
- WHEN `GET /api/bandeja` returns that row
- THEN it carries a non-null proveedor display name, `TipoComprobante` code,
  `Numero`, `TotalOrig`, `Moneda`, and `FechaEmision`

#### Scenario: INCIDENCIA row nulls the comprobante fields

- GIVEN a `fact.ProcesamientoError`-derived row with no `fact.Factura`
- WHEN `GET /api/bandeja` returns that row as `origen: "INCIDENCIA"`
- THEN proveedor name, `TipoComprobante`, `Numero`, `TotalOrig`, `Moneda`, and
  `FechaEmision` are all null and the row is still present

#### Scenario: TipoComprobante is returned as a code

- GIVEN a factura with `TipoComprobante` "01"
- WHEN its bandeja row is returned
- THEN the field value is the string "01", not "Factura" or any display label

### Requirement: Bandeja exposes an estado aggregate over a wider predicate than the default list view

The endpoint MUST expose a per-estado aggregate (`resumen`) whose counts are
GLOBAL — computed over every bandeja-eligible row regardless of the request's
`estado`, `desde`, `hasta`, `proveedor`, `pagina`, or `orden` parameters. The
aggregate MUST be computed over a predicate WIDER than the default-view predicate
(`FiltroWhere`): it MUST include PROMOVIDO and DESCARTADO rows that the default
list view excludes, so that the "Validadas" count is not structurally zero.

The aggregate buckets MUST be mutually exclusive and MUST use the SAME
first-match-wins precedence as the derived Estado chip:

1. DESCARTADO -> `descartadas`
2. else errores > 0 -> `conError`
3. else indicadores present and (esProveedorGenerico or posibleDuplicado) -> `alertas`
4. else PROMOVIDO -> `validadas`
5. else PENDIENTE -> `pendientes`

Every eligible row MUST fall into exactly one bucket; the five bucket counts MUST
sum to the total eligible row count. A row that has BOTH an error and an alert
indicator MUST be counted in `conError` only.

The transport shape of the aggregate (a `resumen` sibling field on the existing
`PaginaBandeja<T>` envelope versus a separate endpoint) is owned by `sdd-design`;
this requirement holds regardless of which is chosen.

#### Scenario: Aggregate is independent of active filters and pagination

- GIVEN rows exist across every estado
- WHEN `GET /api/bandeja?estado=PENDIENTE&proveedor=P001&pagina=2` is called
- THEN the aggregate counts reflect ALL eligible rows, not only the filtered page

#### Scenario: Promoted rows are counted in "validadas"

- GIVEN a fully promoted factura with no open error and no alert indicators
- WHEN the aggregate is computed
- THEN that row increments `validadas` even though the default list view
  (no `estado` filter) would not return it

#### Scenario: Buckets partition the set

- GIVEN N bandeja-eligible rows
- WHEN the aggregate is computed
- THEN `descartadas + conError + alertas + validadas + pendientes === N`

#### Scenario: Error-and-alert row counts once, as error

- GIVEN a row with `errores.length > 0` AND `esProveedorGenerico === true`
- WHEN the aggregate is computed
- THEN it increments `conError` and does not increment `alertas`
