# Design: Catálogos y Satélites (BACKLOG #3)

## Technical Approach

Two new projects under `SmartNet/catalogos/`, replicating `SmartNet/auth/` exactly (item #2):
`SmartNet.Catalogos.Core` holds domain records, repository ports and the pure prefix resolution;
`SmartNet.Catalogos.Infrastructure` holds `Sql*Repository` adapters over `Microsoft.Data.SqlClient`.
No DDL. The 5 `dbo.*` ports are read-only; the 3 `fact.*` satellite ports are SELECT/INSERT/UPDATE
only — `008_usuarios_y_permisos.sql` grants `fact_api` no `DELETE`, so a `Eliminar*` method would be
unusable by construction.

## Architecture Decisions

### Decision 1 — `ResolverCandidatas` receives the whole chart and filters leaves itself

| Option | Tradeoff | Decision |
|---|---|---|
| Caller passes pre-filtered leaves | Leaf rule leaks into the SQL adapter (`WHERE nivel IS NULL`) | Rejected |
| Function receives the full flat chart, filters leaves | One linear pass over 1650 rows per call | **Chosen** |

Rationale: "solo las de 6 dígitos son imputables, `nivel` viene vacío" is REGLAS.md §2 accounting
content, not a query concern. Pre-filtering would duplicate the rule in SQL — the ADR 0019 boundary
this item exists to hold — and a mis-filtered input would be invisible to the pure test suite.
Matching is ordinal `StartsWith`, the exact semantics of `LIKE prefijo + '%'` (REGLAS.md §3).

### Decision 2 — nobody validates account existence at satellite write time

| Option | Tradeoff | Decision |
|---|---|---|
| `EXISTS` guard inside the satellite write adapter | Enforces a **weaker** rule than the real one and cannot survive a later external delete | Rejected |
| Pure candidacy check before write + candidacy filter on read | Requires the caller to call it; this item ships the function, #9 ships the call site | **Chosen** |

Rationale: the real invariant is not "the account exists in `dbo.CuentaContable`" but "the account is
a candidate of that motive" (REGLAS.md §3) — an `EXISTS` guard would happily accept `401`, a
hierarchy node. It is also TOCTOU across systems: the accounting system may delete the account after
the write, and no write-time check can prevent that (ADR 0003: no FK, deliberate). The durable guard
is therefore **read-side**: consumers resolve stored `SugerenciaCuenta` codes against
`ResolverCandidatas` and discard non-candidates. Adapters stay storage ports; the rule stays in Core.

### Decision 3 — the shared `TestDatabaseFixture` is not modified

`CreateExternalDboCatalogsAsync()` creates the 5 `dbo.*` tables empty and only `dbo.Motivo` gets
seeded rows. This item seeds `CuentaContable`/`Proveedor`/`Origen`/`DocumentoIdentidad` through the
already-public `ExecuteNonQueryAsync`, from a helper local to `SmartNet.Catalogos.Infrastructure.Tests`.
Keeps item #3 additive and leaves item #1's harness stable for the six projects already using it.

## Data Flow

    dbo.Motivo.cuenta ──┐
    (prefijos "4011,4017,…")
                        ├──→ ResolverCandidatas (Core, puro) ──→ candidatas ordenadas
    dbo.CuentaContable ─┘                                              │
    (1650 filas, nivel NULL = 907 hojas)                               ↓
                                                          #8/#9 eligen y confirman
                                                                       │
    fact.SugerenciaCuenta ←── RegistrarUsoAsync(instante) ─────────────┘

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/catalogos/SmartNet.Catalogos.Core/` | Create | `net10.0`, **zero** `PackageReference` |
| `SmartNet/catalogos/SmartNet.Catalogos.Core.Tests/` | Create | xunit, Mono.Cecil 0.11.6, NetArchTest.Rules 1.3.2 |
| `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure/` | Create | `Microsoft.Data.SqlClient` 7.0.2, no `FrameworkReference` |
| `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests/` | Create | + `ProjectReference` a `SmartNet.Db.TestBootstrap` |
| `SmartNet/SmartNet.sln` | Modify | Carpeta `catalogos` + 4 proyectos |
| `.github/workflows/ci.yml` | Modify | `Core.Tests` → `verificaciones-estaticas`; `Infrastructure.Tests` → `pruebas-de-base-de-datos` |

## Interfaces / Contracts

```csharp
public sealed record CuentaContable(string Cuenta, string Descripcion, byte? Nivel,
    string? CtaReflejaCodigo, string? CtaPuenteCodigo)
{
    public bool EsHojaImputable => Nivel is null;   // REGLAS.md §2
}

public static class ResolucionDePrefijos
{
    // Split por coma, trim, descarta vacíos, deduplica (ordinal). null/"" → lista vacía.
    public static IReadOnlyList<string> ParsearPrefijos(string? prefijosDeclarados);

    // Hojas cuyo código empieza por algún prefijo. Deduplicadas por código y ordenadas
    // ascendente ordinal: REGLAS.md §3 escalón 3 ("la primera candidata") exige orden determinista.
    public static IReadOnlyList<CuentaContable> ResolverCandidatas(
        string? prefijosDeclarados, IReadOnlyList<CuentaContable> planDeCuentas);

    public static bool EsCandidata(string cuentaCodigo, string? prefijosDeclarados,
        IReadOnlyList<CuentaContable> planDeCuentas);
}
```

Ports (all `async`, all take `CancellationToken`):

| Port | Operations | Consumer |
|---|---|---|
| `ICuentaContableRepository` | `ListarPlanCompletoAsync`, `ObtenerAsync(cuenta)` | #8 (incl. `ctarefleja`/`ctapuente`, solo lectura) |
| `IMotivoRepository` | `ObtenerAsync(codigo)`, `ListarAsync` | #7, #8 |
| `IProveedorRepository` | `ObtenerPorCodigoAsync`, `BuscarPorRucAsync` → lista (`rucpro` no es único; usa `IX_Proveedor_Ruc`) | #6, #7 |
| `IOrigenRepository` / `IDocumentoIdentidadRepository` | `ListarAsync` (13 / 6 filas) | #7, #12 |
| `IProveedorAtributoRepository` | `ObtenerAsync`, `GuardarAsync` (upsert) | #8 (`EsRelacionada`, REGLAS.md §4) |
| `IMotivoAtributoRepository` | `ObtenerAsync`, `ListarAsync`, `GuardarAsync` (upsert) | #8, #12 (activo + origen `02` se filtra en Core, no en SQL) |
| `ISugerenciaCuentaRepository` | `ListarPorProveedorYMotivoAsync`, `ListarPorMotivoAsync`, `ListarPorProveedorAsync`, `RegistrarUsoAsync(…, DateTimeOffset instante)` | #9 (escalones 1/2/3 y sugerencia de motivo) |

`RegistrarUsoAsync` es una sola sentencia (`UPDATE … SET Veces = Veces + 1, UltimoUso = @instante;
IF @@ROWCOUNT = 0 INSERT …`) y recibe el instante **como parámetro** — nunca `SYSUTCDATETIME()` —
para que #9 se pruebe determinista. La siembra histórica de #9 es N llamadas a esta misma operación;
no se inventa API masiva sin consumidor.

## Testing Strategy

| Layer | What to Test | Approach |
|---|---|---|
| Unit (Core.Tests) | Reglas de `ResolverCandidatas`: hoja vs nodo, multi-prefijo, prefijos solapados, dedup, orden determinista, comparación ordinal, `cuenta` NULL | Plan en memoria, sin DB |
| Unit (golden) | Los 5 ejemplos de REGLAS.md §3: motivo 22→1, 48→6, 6→20, 70→34, 8→22 candidatas | `SmartNet/db/fixtures/data/CuentaContable.csv` (1650 filas reales, `\|`-delimitado, sin cabecera) como recurso enlazado del proyecto de test — **puro, sin DB** |
| Unit (purity) | `PurityScanTests` sobre `SmartNet.Catalogos.Core.dll` | Copia literal del de `SmartNet.Auth.Core.Tests` (5 hechos: 3 NetArchTest + refs de ensamblado + IL scan de `DateTime.Now/UtcNow`) |
| Integration | Cada `Sql*Repository` contra `fact_test_<guid>` | `TestDatabaseFixture` (patrón `SqlUsuarioRepositoryTests`), seed local de las 4 tablas `dbo` restantes |
| Integration | `RegistrarUsoAsync` inserta la primera vez e incrementa la segunda | Misma fixture; assert `Veces`/`UltimoUso` |
| Integration (permisos) | Los GRANT de `fact_api` bastan; `usr_worker` sigue denegado | `PermissionSufficiencyTests` análogo: SQL literal de cada adaptador bajo `ExecuteAsUserAsync("usr_api"/"usr_worker")` |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. Only parameterized SQL over an existing schema.

## Migration / Rollout

No migration required. Purely additive application code; reverting removes both projects.

## Open Questions

- [ ] `dbo.Motivo.cuenta` es NULLable: un motivo sin prefijos resuelve a **cero** candidatas sin
      lanzar excepción. El rechazo ("sin motivo no hay cuenta de cargo", REGLAS.md §3) es de #8.
- [ ] REGLAS.md §2 lista 4 catálogos; ADR 0003 Rev.5 lista 5. Asumido *doc drift* (proposal.md); la
      spec lo declara, este diseño construye los 5.
