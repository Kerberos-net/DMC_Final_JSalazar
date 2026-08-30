using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.Proveedor</c> for <see cref="IProveedorRepository"/> (design.md
/// Interfaces/Contracts). Read-only — ADR 0003 external catalog. <see cref="BuscarPorRucAsync"/>
/// returns a list, never a single row: <c>rucpro</c> is non-unique (<c>IX_Proveedor_Ruc</c> is a
/// non-unique index, not a key).
/// </summary>
public sealed class SqlProveedorRepository : IProveedorRepository
{
    /// <summary>Fixed server-side page size for <see cref="BuscarAsync"/> (api-catalogos-proveedores
    /// "fixed server-side page size"). <c>LIKE</c> over ~6600 <c>dbo.Proveedor</c> rows is accepted
    /// without a name index (ADR 0003 — a <c>dbo.*</c> index is out of scope, flagged decision).</summary>
    public const int TamanoPagina = 20;

    /// <summary>Minimum trimmed length before a query runs — a shorter term returns an empty page
    /// and issues no scan (api-catalogos-proveedores "Empty, short, and no-match queries").</summary>
    private const int LongitudMinima = 2;

    private readonly string _connectionString;

    public SqlProveedorRepository(string connectionString) => _connectionString = connectionString;

    public async Task<BusquedaProveedores> BuscarAsync(string consulta, int pagina, CancellationToken ct)
    {
        var termino = (consulta ?? string.Empty).Trim();
        if (termino.Length < LongitudMinima)
        {
            return new BusquedaProveedores(Array.Empty<Proveedor>(), HayMas: false);
        }

        var paginaNormalizada = pagina < 1 ? 1 : pagina;
        var salto = (paginaNormalizada - 1) * TamanoPagina;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // Fetch one extra row so `HayMas` is known without a second COUNT round-trip.
        command.CommandText =
            """
            SELECT codpro, proveedor, coddocide, rucpro
            FROM dbo.Proveedor
            WHERE (proveedor LIKE @patron OR rucpro LIKE @patron)
              AND codpro <> 'P00000'
            ORDER BY proveedor
            OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY;
            """;
        // Escape LIKE metacharacters so a user who types `%`, `_` or `[` searches for them
        // literally instead of as wildcards. Bracket-escaping needs no ESCAPE clause. The query
        // is already parameterised, so this is a correctness fix, not an injection one.
        var patron = "%" + termino
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]") + "%";
        command.Parameters.AddWithValue("@patron", patron);
        command.Parameters.AddWithValue("@salto", salto);
        command.Parameters.AddWithValue("@tamano", TamanoPagina + 1);

        var filas = new List<Proveedor>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(Map(reader));
        }

        var hayMas = filas.Count > TamanoPagina;
        return new BusquedaProveedores(
            hayMas ? filas.GetRange(0, TamanoPagina) : filas,
            hayMas);
    }

    // BACKLOG #22 PR5 (catalog-queries-api spec req 1-3, design D6/D7). Blank `@patron` (DBNull)
    // lists everything; P00000 is NOT filtered out here (that exclusion is a picker-only rule).
    private const string FiltroCatalogo =
        "(@patron IS NULL OR proveedor LIKE @patron OR rucpro LIKE @patron OR codpro LIKE @patron)";

    // design D7: the sort key maps to a COMPILE-TIME CONSTANT column and a constant ASC/DESC — the
    // user's `orden`/`direccion` text is never concatenated as an identifier. Every ordering appends
    // `, codpro ASC`: `proveedor` repeats and `rucpro` is non-unique AND nullable, so without a
    // unique tiebreak OFFSET/FETCH would drop or duplicate rows across a page boundary. `rucpro`
    // NULLs sort first ASC (accepted, asserted).
    private static string OrdenSql(string orden, string direccion)
    {
        var columna = orden switch
        {
            "ruc" => "rucpro",
            "codigo" => "codpro",
            _ => "proveedor",
        };
        var direccionSql = string.Equals(direccion, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        // `codpro` is the unique tiebreak; SQL Server rejects a column named twice in ORDER BY, so
        // only append it when the primary key column is something else.
        var desempate = columna == "codpro" ? string.Empty : ", codpro ASC";
        return $"{columna} {direccionSql}{desempate}";
    }

    private static object PatronCatalogo(string? consulta)
    {
        var termino = (consulta ?? string.Empty).Trim();
        if (termino.Length == 0)
        {
            return DBNull.Value;
        }

        return "%" + termino
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]") + "%";
    }

    public async Task<PaginaProveedores> ListarCatalogoAsync(
        string? consulta, string orden, string direccion, int pagina, int tamanio, CancellationToken ct)
    {
        var paginaNormalizada = pagina < 1 ? 1 : pagina;
        var tamanioNormalizado = tamanio < 1 ? 1 : tamanio;
        var salto = (paginaNormalizada - 1) * tamanioNormalizado;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        // `CAST(COUNT(*) OVER() AS INT)` gives the whole filtered count in the SAME pass that scans
        // and sorts (OFFSET/FETCH applies logically after window functions) — zero extra scans, the
        // pattern SqlBandejaRepository.ListarConConexionAsync already uses. The only edge that needs
        // a fallback COUNT(*) is an out-of-range page: no rows come back, so no window value does.
        command.CommandText =
            $"""
             SELECT codpro, proveedor, coddocide, rucpro, CAST(COUNT(*) OVER() AS INT) AS TotalRegistros
             FROM dbo.Proveedor
             WHERE {FiltroCatalogo}
             ORDER BY {OrdenSql(orden, direccion)}
             OFFSET @salto ROWS FETCH NEXT @tamano ROWS ONLY;

             IF @nroPagina > 1 AND NOT EXISTS (
                 SELECT 1 FROM dbo.Proveedor
                 WHERE {FiltroCatalogo}
                 ORDER BY codpro
                 OFFSET @salto ROWS FETCH NEXT 1 ROWS ONLY)
                 SELECT COUNT(*) AS TotalRegistros FROM dbo.Proveedor WHERE {FiltroCatalogo};
             """;
        command.Parameters.AddWithValue("@patron", PatronCatalogo(consulta));
        command.Parameters.AddWithValue("@salto", salto);
        command.Parameters.AddWithValue("@tamano", tamanioNormalizado);
        command.Parameters.AddWithValue("@nroPagina", paginaNormalizada);

        var filas = new List<Proveedor>();
        var totalRegistros = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totalRegistros = reader.GetInt32(4);
            filas.Add(Map(reader));
        }

        if (filas.Count == 0)
        {
            var huboFallback = await reader.NextResultAsync(ct);
            totalRegistros = huboFallback && await reader.ReadAsync(ct) ? reader.GetInt32(0) : 0;
        }

        var totalPaginas = totalRegistros == 0
            ? 0
            : (int)Math.Ceiling(totalRegistros / (double)tamanioNormalizado);

        return new PaginaProveedores(filas, paginaNormalizada, tamanioNormalizado, totalRegistros, totalPaginas);
    }

    public async Task<IReadOnlyList<Proveedor>> ListarCatalogoCompletoAsync(
        string? consulta, string orden, string direccion, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT codpro, proveedor, coddocide, rucpro
             FROM dbo.Proveedor
             WHERE {FiltroCatalogo}
             ORDER BY {OrdenSql(orden, direccion)};
             """;
        command.Parameters.AddWithValue("@patron", PatronCatalogo(consulta));

        var filas = new List<Proveedor>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(Map(reader));
        }

        return filas;
    }

    public async Task<Proveedor?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT codpro, proveedor, coddocide, rucpro
            FROM dbo.Proveedor
            WHERE codpro = @codpro;
            """;
        command.Parameters.AddWithValue("@codpro", codigo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<IReadOnlyList<Proveedor>> BuscarPorRucAsync(string ruc, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT codpro, proveedor, coddocide, rucpro
            FROM dbo.Proveedor
            WHERE rucpro = @rucpro;
            """;
        command.Parameters.AddWithValue("@rucpro", ruc);

        var resultado = new List<Proveedor>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    private static Proveedor Map(SqlDataReader reader) =>
        new(
            Codigo: reader.GetString(0).TrimEnd(),
            Nombre: reader.GetString(1),
            CodigoTipoDocumento: reader.IsDBNull(2) ? null : reader.GetString(2),
            Ruc: reader.IsDBNull(3) ? null : reader.GetString(3));
}
