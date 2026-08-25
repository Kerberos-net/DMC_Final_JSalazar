using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D7 — adaptador SQL read-only de <see cref="IEstadoIntegracionRepository"/> sobre
/// <c>fact.EstadoIntegracion</c>. Nunca escribe: ambos runtimes escriben esa tabla por fila
/// (ADR 0003 "Publicación con múltiples orígenes"); #11 solo la lee para derivar la "pill".
/// </summary>
public sealed class SqlEstadoIntegracionRepository : IEstadoIntegracionRepository
{
    private readonly string _connectionString;

    public SqlEstadoIntegracionRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<EstadoIntegracion>> ListarAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Nombre, UltimoIntento, UltimoExito, UltimoError, FallosSeguidos
            FROM fact.EstadoIntegracion
            ORDER BY Nombre;
            """;

        var resultado = new List<EstadoIntegracion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new EstadoIntegracion(
                Nombre: reader.GetString(0).TrimEnd(),
                UltimaEjecucion: reader.IsDBNull(1) ? null : AsUtcOffset(reader.GetDateTime(1)),
                UltimoExito: reader.IsDBNull(2) ? null : AsUtcOffset(reader.GetDateTime(2)),
                UltimoError: reader.IsDBNull(3) ? null : reader.GetString(3),
                FallosConsecutivos: reader.GetInt32(4)));
        }

        return resultado;
    }

    // fact.EstadoIntegracion se escribe con SYSUTCDATETIME() (design.md) -- DATETIME2 no lleva
    // offset, así que se reconstruye como UTC explícito en lugar de asumir la zona local del host.
    private static DateTimeOffset AsUtcOffset(DateTime valor) =>
        new(DateTime.SpecifyKind(valor, DateTimeKind.Utc));
}
