using Microsoft.Data.SqlClient;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// SQL adapter over the read/consume side of <c>fact.InboxEvent</c> for
/// <see cref="IEventoInboxRepository"/> (design.md data flow). SELECT-only: the actual
/// <c>PROMOVIDO</c>/<c>DESCARTADO</c> transition is <see cref="SqlPromocionRepository"/>'s job
/// (design D2/D9), inside its own transaction. Never queries <c>fact.Procesamiento</c> or any
/// other worker-private table (ADR 0003, spec.md "Consumer never touches Procesamiento").
/// </summary>
public sealed class SqlEventoInboxRepository : IEventoInboxRepository
{
    private readonly string _connectionString;

    public SqlEventoInboxRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<EventoInboxPendiente>> ListarPendientesAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT InboxEventId, ProcesamientoId, Payload
            FROM fact.InboxEvent
            WHERE EstadoConsumo = 'PENDIENTE';
            """;

        var pendientes = new List<EventoInboxPendiente>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            pendientes.Add(new EventoInboxPendiente(
                InboxEventId: reader.GetInt64(0),
                ProcesamientoId: reader.GetInt64(1),
                PayloadJson: reader.GetString(2)));
        }

        return pendientes;
    }
}
