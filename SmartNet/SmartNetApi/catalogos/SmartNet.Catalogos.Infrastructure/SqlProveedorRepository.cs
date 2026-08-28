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
