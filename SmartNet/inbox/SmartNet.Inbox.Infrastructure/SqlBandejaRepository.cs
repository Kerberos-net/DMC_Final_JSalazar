using Microsoft.Data.SqlClient;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// SQL adapter backing <c>GET /api/bandeja?estado=&amp;orden=</c> (design D6, reuse ADR 0008's
/// contract, #7-shaped) for <see cref="IBandejaRepository"/>. <c>BandejaEndpoints.cs</c> (Phase 4)
/// is a thin delegator over this repository -- never a second query surface.
/// </summary>
public sealed class SqlBandejaRepository : IBandejaRepository
{
    private readonly string _connectionString;

    public SqlBandejaRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<BandejaItem>> ListarAsync(string? estado, string orden, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // `orden` only chooses ASC/DESC on a fixed column -- never string-concatenated as an
        // identifier, so there is no injection surface here despite the interpolation below.
        var direction = string.Equals(orden, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        command.CommandText =
            $"""
             SELECT ie.InboxEventId, ie.EstadoConsumo, ie.CreadoEn, ie.FacturaId, ie.MotivoDescarte,
                    f.EsProveedorGenerico, f.PosibleDuplicado, f.TieneCamposNoExtraidos, f.FechaEnDomingo, f.AfectacionMixta
             FROM fact.InboxEvent ie
             LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
             WHERE (@estado IS NULL OR ie.EstadoConsumo = @estado)
             ORDER BY ie.CreadoEn {direction};
             """;
        command.Parameters.AddWithValue("@estado", (object?)estado ?? DBNull.Value);

        var items = new List<BandejaItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            IndicadoresFactura? indicadores = reader.IsDBNull(5)
                ? null
                : new IndicadoresFactura(
                    EsProveedorGenerico: reader.GetBoolean(5),
                    PosibleDuplicado: reader.GetBoolean(6),
                    TieneCamposNoExtraidos: reader.GetBoolean(7),
                    FechaEnDomingo: reader.GetBoolean(8),
                    AfectacionMixta: reader.IsDBNull(9) ? null : reader.GetBoolean(9));

            items.Add(new BandejaItem(
                InboxEventId: reader.GetInt64(0),
                EstadoConsumo: reader.GetString(1),
                CreadoEn: reader.GetDateTime(2),
                FacturaId: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                Indicadores: indicadores,
                MotivoDescarte: reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return items;
    }
}
