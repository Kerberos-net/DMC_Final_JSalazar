using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D7 — adaptador SQL de <see cref="ICommandQueueRepository"/>: un <c>INSERT
/// fact.CommandQueue</c> autocontenido, fuera de la transacción de escritura del comando que lo
/// origina (encolar es "fire and forget" hacia Python — nunca participa del rollback del comando
/// de negocio que lo disparó, design D7).
/// </summary>
public sealed class SqlCommandQueueRepository : ICommandQueueRepository
{
    private readonly string _connectionString;

    public SqlCommandQueueRepository(string connectionString) => _connectionString = connectionString;

    public async Task EncolarAsync(string tipo, long? referencia, string payload, Guid correlationId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fact.CommandQueue (Tipo, Referencia, Payload, CorrelationId)
            VALUES (@tipo, @referencia, @payload, @correlationId);
            """;
        command.Parameters.AddWithValue("@tipo", tipo);
        command.Parameters.AddWithValue("@referencia", (object?)referencia ?? DBNull.Value);
        command.Parameters.AddWithValue("@payload", payload);
        command.Parameters.AddWithValue("@correlationId", correlationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
