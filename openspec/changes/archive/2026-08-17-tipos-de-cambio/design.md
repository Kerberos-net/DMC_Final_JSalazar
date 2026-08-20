# Design: Tipos de cambio (BACKLOG #4)

## Technical Approach

Two .NET projects under `SmartNet/tipos-de-cambio/`, replicating `SmartNet/catalogos/` exactly
(item #3): `SmartNet.TiposCambio.Core` holds the `TipoCambio` record, the port, the typed result
and the pure SBS>MANUAL rule; `SmartNet.TiposCambio.Infrastructure` holds the single
`SqlTipoCambioRepository` over `Microsoft.Data.SqlClient` 7.0.2. No DDL, no GRANT changes.
`SmartNet/worker/` is created here as the repo's first Python package: one single-run script that
scrapes SBS, inserts `Origen='SBS'` and stamps `fact.EstadoIntegracion`.

Column mapping note: spec.md writes `Tasa` as shorthand. The real table has `Compra` and `Venta`
(both `NOT NULL`, `DECIMAL(12,6)`). The record carries both; the accounting consumer (#8) reads
`Venta` — ADR 0018 pt. 1, a pasivo converts at venta.

## Architecture Decisions

### Decision 1 — the SBS>MANUAL priority lives in Core, not in the `SELECT`

| Option | Tradeoff | Decision |
|---|---|---|
| `SELECT TOP 1 … ORDER BY CASE Origen WHEN 'SBS' THEN 0 ELSE 1 END` | One roundtrip, but the rule that decides which rate gets frozen into a confirmed asiento becomes invisible to the pure suite | Rejected |
| `SELECT` both origins (max 2 rows, by PK) and let Core select | One extra row over the wire; rule is unit-tested with no DB | **Chosen** |

Rationale: identical to item #3 Decision 1 — ADR 0019 keeps accounting decisions out of SQL. The
PK `(Fecha, Origen)` bounds the result set at two rows, so the "cost" is one row. An unknown
`Origen` value (impossible under `CK_TipoCambio_Origen`, possible under a future schema edit) is
**ignored, never selected**: an unexpected string must not become a frozen rate.

### Decision 2 — absence is a closed record hierarchy, not `null` and not an exception

| Option | Tradeoff | Decision |
|---|---|---|
| `TipoCambio?` | Spec forbids it; `null` is not exhaustively handleable and #11 must not read a null field | Rejected |
| `record struct (bool Encontrado, TipoCambio? Valor)` | Still null-with-intent; both fields are independently wrong-able | Rejected |
| `Result<T,E>` from a NuGet package | Core must keep **zero** `PackageReference` (purity scan) | Rejected |
| Abstract record + `private protected` ctor + two nested sealed cases | No compiler exhaustiveness in C#, but the hierarchy is closed to other assemblies | **Chosen** |

Rationale: closing the hierarchy with `private protected` is the idiomatic C# discriminated union
without dependencies. #11 pattern-matches `SinTipoCambio` → 409; #8 matches `Vigente` → freeze.

### Decision 3 — the PK enforces duplicate MANUAL loads; the adapter only translates

Plain `INSERT`; catch `SqlException` 2627/2601 → `ResultadoCargaManual.YaExistia`; anything else
propagates. Rejected: `INSERT … WHERE NOT EXISTS`, which moves the guard out of the engine and
opens a TOCTOU window between check and insert. The Python side uses the same shape (catch
`IntegrityError`), which is also what makes two concurrent scrapes of the same date safe.

### Decision 4 — `CargarManualAsync` takes no `Origen` parameter

The adapter hardcodes `'MANUAL'`. ADR 0003 says .NET owns only the MANUAL rows; making the origin
un-passable enforces that partition in the signature instead of in a comment. Symmetrically the
Python repository function hardcodes `'SBS'`.

### Decision 5 — Python tooling: `pyproject.toml` + src layout, `requests` + `beautifulsoup4`, `pyodbc`, `pytest`, `ruff`

| Concern | Chosen | Rejected, and why |
|---|---|---|
| Packaging | single `pyproject.toml` (PEP 621, setuptools), src layout, `pip install -e .[dev]` | `requirements.txt` — a second source of truth for the same pins |
| HTTP | `requests` | `httpx`/`aiohttp` (async buys nothing for a single-run script) |
| Parsing | `beautifulsoup4` on the stdlib `html.parser` backend | Playwright/Selenium (the SBS page renders server-side; a browser download in CI is disproportionate); `pandas.read_html` (pulls pandas+lxml for one table) |
| Driver | `pyodbc` + ODBC Driver 18 | `pymssql` — easier install, but the deployment target already needs a Microsoft driver and `pyodbc` matches `Microsoft.Data.SqlClient`'s `Encrypt`/`TrustServerCertificate` semantics |
| Lint / tests | `ruff`, `pytest` | Already the declared project convention (CONVENTIONS.md, config.yaml `per_runtime_commands_expected.python`) |
| Money | `Decimal(str(...))` | `float` — forbidden outright (CONVENTIONS.md) |

Credentials come from `SMARTNET_WORKER_ODBC_CONNECTION` only, never a committed default — same
rule as `RunnerOptions.SMARTNET_DB_CONNECTION`. IO lives exclusively in the CLI entry point; the
parser receives HTML text and the repository receives a cursor, so everything else is unit-testable
without network or DB (ADR 0019 applied to Python).

### Decision 6 — `EstadoIntegracion` is written with `UPDATE` and a rowcount guard

`009_datos_base.sql` already seeds `Nombre='SBS'`. The scraper issues `UPDATE … WHERE
Nombre='SBS'` and **raises if `rowcount != 1`** rather than falling back to an `INSERT`: a missing
base row means the schema was not applied, and a silent insert would hide that. Success and the
rate insert commit in one transaction; a failure is logged in its own transaction after rollback,
incrementing `FallosSeguidos` and truncating `UltimoError` to 2000 chars.

### Decision 7 — a third CI job for Python, with its own SQL Server container

Proving `fact_worker`'s grants from Python needs a real `CREATE LOGIN usr_worker WITH PASSWORD`,
a server-scoped mutation. The existing `pruebas-de-base-de-datos` container is shared with a .NET
harness whose premise is that those logins do **not** exist (008's `SUSER_ID` guard, throwaway
`WITHOUT LOGIN` users). Rejected: folding Python into that job (couples two runtimes to one mutated
instance); rejected: local-only/env-gated with no CI run (the ADR 0003 partition would stay
unproven from the side that actually writes `Origen='SBS'`).

## Data Flow

    SBS (web)                              Operador (#11, futuro)
       │ requests.get (solo en el CLI)          │ CargarManualAsync
       ▼                                        ▼
    parse_tipo_cambio(html)  puro          SqlTipoCambioRepository  (fact_api)
       │ Decimal                                │ INSERT Origen='MANUAL'
       ▼                                        ▼
    INSERT Origen='SBS' (fact_worker) ──▶  fact.TipoCambio  ◀── PK (Fecha, Origen)
       │                                        │ SELECT ambos orígenes
       ▼                                        ▼
    fact.EstadoIntegracion            SeleccionDeTipoCambio.Seleccionar  (Core, puro)
      (Nombre='SBS', UPDATE)                    │
                                    ResultadoTipoCambio { Vigente │ SinTipoCambio }
                                                └──▶ #8 congela · #11 responde 409

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core/` | Create | `net10.0`, **zero** `PackageReference` |
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core.Tests/` | Create | xunit 2.9.3, Mono.Cecil 0.11.6, NetArchTest.Rules 1.3.2 |
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure/` | Create | `Microsoft.Data.SqlClient` 7.0.2, no `FrameworkReference` |
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure.Tests/` | Create | + `ProjectReference` a `SmartNet.Db.TestBootstrap` |
| `SmartNet/worker/pyproject.toml` | Create | Deps + `[tool.ruff]` + `[tool.pytest.ini_options]` (marker `integracion`, `externa`) |
| `SmartNet/worker/src/smartnet_worker/{config,sbs,tipo_cambio_repo,estado_integracion,cli_tipo_cambio}.py` | Create | Parser puro, repos por cursor, IO solo en el CLI |
| `SmartNet/worker/tests/` | Create | Unit + `fixtures/sbs_tipo_cambio.html` (página real guardada) + `integration/` |
| `SmartNet/worker/README.md` | Create | Instalación, variables de entorno, comandos — la convención que #5 reutiliza |
| `SmartNet/SmartNet.sln` | Modify | Carpeta `tipos-de-cambio` + 4 proyectos |
| `.github/workflows/ci.yml` | Modify | +Core.Tests y pytest unit en `verificaciones-estaticas`; +Infrastructure.Tests en `pruebas-de-base-de-datos`; job nuevo `pruebas-de-worker-python` |

## Interfaces / Contracts

```csharp
public enum OrigenTipoCambio { Sbs, Manual }

public sealed record TipoCambio(
    DateOnly Fecha, OrigenTipoCambio Origen, decimal Compra, decimal Venta, DateTime FechaConsulta);

public abstract record ResultadoTipoCambio
{
    private protected ResultadoTipoCambio() { }             // cierra la jerarquía
    public sealed record Vigente(TipoCambio Valor) : ResultadoTipoCambio;
    public sealed record SinTipoCambio(DateOnly Fecha) : ResultadoTipoCambio;   // ADR 0018 pt. 3
}

public static class SeleccionDeTipoCambio
{
    // SBS gana; MANUAL es el respaldo; origen desconocido o fecha distinta se descartan.
    public static ResultadoTipoCambio Seleccionar(DateOnly fecha, IReadOnlyList<TipoCambio> candidatas);
}

public enum ResultadoCargaManual { Cargada, YaExistia }
```

```python
@dataclass(frozen=True)
class TipoCambioSbs:
    fecha: date; compra: Decimal; venta: Decimal; fecha_consulta: datetime

def parse_tipo_cambio(html: str) -> TipoCambioSbs: ...        # puro; ParseoSbsError si no cuadra
def insertar_sbs(cursor, tc: TipoCambioSbs) -> bool: ...      # False = ya registrado (IntegrityError)
def registrar_exito(cursor, instante: datetime) -> None: ...  # instante como parámetro, nunca now()
def registrar_fallo(cursor, instante: datetime, error: str) -> None: ...
```

| Port | Operations | Consumer |
|---|---|---|
| `ITipoCambioRepository` | `ObtenerVigenteAsync(DateOnly fecha, CancellationToken)` → `ResultadoTipoCambio`; `CargarManualAsync(DateOnly fecha, decimal compra, decimal venta, DateTime fechaConsulta, long? cargadoPorUsuarioId, CancellationToken)` → `ResultadoCargaManual` | #8 (congela `Venta`), #11 (409 y carga manual) |

`FechaConsulta` se recibe **como parámetro**, nunca `SYSUTCDATETIME()`, igual que `RegistrarUsoAsync`
en item #3: sin eso las pruebas de #8 no son deterministas. `CargadoEn` sí queda al `DEFAULT` de la
tabla — es sello de auditoría de fila, no dato de dominio.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit (Core.Tests) | SBS gana con ambos; solo MANUAL; solo SBS; lista vacía → `SinTipoCambio`; fila de otra fecha descartada; `Origen` desconocido descartado | Listas en memoria, sin DB |
| Unit (purity) | `PurityScanTests` sobre `SmartNet.TiposCambio.Core.dll` | Copia literal de `SmartNet.Catalogos.Core.Tests`, **+ `System.Net.Http`** en la lista prohibida (spec.md lo exige aquí) |
| Integration (Infra.Tests) | Lookup con SBS, con ambos, sin filas; `CargarManualAsync` inserta y el segundo intento devuelve `YaExistia` por PK real | `TestDatabaseFixture` (patrón `SqlMotivoAtributoRepositoryTests`) |
| Integration (permisos) | El SQL literal del adaptador corre bajo `usr_api` **y** `usr_worker` (los GRANT de `007/008` son idénticos para ambos roles en esta tabla) | `PermissionSufficiencyTests` análogo, `ExecuteAsUserAsync` |
| Structural | El adaptador no menciona `dbo.` | `NoWriteToDboStructuralTests` análogo |
| Unit (pytest, sin red ni DB) | `parse_tipo_cambio` sobre la página real guardada → `Decimal` exactos; HTML mutilado → `ParseoSbsError`; el SQL emitido no contiene `dbo.`; éxito/fallo escriben las columnas correctas | Cursor falso que registra sentencia y parámetros |
| Integration (pytest, marker `integracion`) | `pyodbc` real como `usr_worker`: inserta, el duplicado devuelve `False`, `UPDATE` de `EstadoIntegracion` afecta exactamente 1 fila | Job `pruebas-de-worker-python`: contenedor propio + `CREATE LOGIN` efímero + `SmartNet.Db.Runner` |
| **Excluido de CI** | Scrape real contra `sbs.gob.pe` | Marker `externa`, deseleccionado por defecto. Un build rojo por una caída de la SBS no dice nada sobre nuestro cambio y enseña a ignorar CI. La rotura de maquetación ya tiene salida operativa declarada (carga manual, ADR 0018 pt. 3) y riesgo aceptado en TECH-DESIGN.md; el marker permite verificarla a mano tras un incidente |

## Threat Matrix

| Boundary | Applicability | Reason |
|---|---|---|
| Documentation-like paths | N/A | Este cambio no clasifica ni ejecuta archivos por nombre; `pyproject.toml` lo consume `pip`, no código nuestro (no se añade `requirements.txt`) |
| Git repository selection | N/A | Ningún componente invoca `git` |
| Commit state / Push state / PR commands | N/A | Sin automatización de VCS ni de PR |

La frontera nueva real es de **red + credenciales**, fuera de esta matriz, y su respuesta de diseño
es explícita: sin credencial por defecto en código (`SMARTNET_WORKER_ODBC_CONNECTION`), SQL
parametrizado en ambos runtimes, ningún `dbo.*` (probado estructuralmente en los dos lados), y
timeout explícito en `requests.get` para que un cuelgue de la SBS no deje el proceso vivo.

## Migration / Rollout

No migration required. Additive: dos proyectos .NET, un paquete Python, tres ediciones de wiring.
Revertir el commit los elimina; no hay DDL ni GRANT que deshacer.

## Open Questions — resueltas

- **Naming del resultado**: anidados, `ResultadoTipoCambio.Vigente` / `.SinTipoCambio`, como en
  Interfaces/Contracts arriba. Confirmado por el usuario.
- **Tercer job de CI** (Decisión 7): aceptado. `pruebas-de-worker-python` corre con su propio
  contenedor SQL Server y `CREATE LOGIN` efímero de `usr_worker`.
- **`Compra`**: se exige junto con `Venta` en la carga manual (ambas `NOT NULL` en el DDL de #1, sin
  tocar el esquema); el operador ingresa ambas cifras aunque hoy ninguna regla contable lea `Compra`.
- **Versión de Python**: `3.13`, fijada en `pyproject.toml` (`requires-python = ">=3.13"`). ODBC
  Driver 18 sigue siendo un supuesto de despliegue no confirmado por ADR 0012 — no bloquea este
  ítem, footnote para operación.
