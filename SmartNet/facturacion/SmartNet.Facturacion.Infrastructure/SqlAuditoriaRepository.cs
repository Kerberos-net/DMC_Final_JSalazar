using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D7 — adaptador SQL read-only de <see cref="IAuditoriaRepository"/>. Une tres orígenes de
/// <c>EntidadId</c> (FACTURA directo, ASIENTO vía <c>fact.AsientoContable.FacturaId</c> incluyendo
/// ANULADO, ADJUNTO vía <c>fact.AdjuntoManual.FacturaId</c>) en una sola consulta parametrizada,
/// sin transacción propia (misma forma que <see cref="SqlEstadoIntegracionRepository"/>: abre y
/// cierra su propia <see cref="SqlConnection"/>, nunca comparte la de <see cref="SqlUnidadDeTrabajo"/>).
/// </summary>
public sealed class SqlAuditoriaRepository : IAuditoriaRepository
{
    private readonly string _connectionString;

    public SqlAuditoriaRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<EntradaAuditoria>> ListarPorFacturaAsync(long facturaId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.EntidadTipo, a.EntidadId, a.Accion, a.Campo, a.ValorOriginal, a.ValorNuevo,
                   a.Motivo, a.UsuarioId, a.OcurridoEn
            FROM fact.AuditoriaCorreccion a
            WHERE (a.EntidadTipo = 'FACTURA' AND a.EntidadId = @facturaId)
               OR (a.EntidadTipo = 'ASIENTO' AND a.EntidadId IN (
                       SELECT AsientoContableId FROM fact.AsientoContable WHERE FacturaId = @facturaId))
               OR (a.EntidadTipo = 'ADJUNTO' AND a.EntidadId IN (
                       SELECT AdjuntoManualId FROM fact.AdjuntoManual WHERE FacturaId = @facturaId))
            ORDER BY a.OcurridoEn DESC, a.AuditoriaCorreccionId DESC;
            """;
        command.Parameters.AddWithValue("@facturaId", facturaId);

        var resultado = new List<EntradaAuditoria>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new EntradaAuditoria(
                EntidadTipo: reader.GetString(0).TrimEnd(),
                EntidadId: reader.GetInt64(1),
                Accion: reader.GetString(2).TrimEnd(),
                Campo: reader.IsDBNull(3) ? null : reader.GetString(3),
                ValorOriginal: reader.IsDBNull(4) ? null : reader.GetString(4),
                ValorNuevo: reader.IsDBNull(5) ? null : reader.GetString(5),
                Motivo: reader.IsDBNull(6) ? null : reader.GetString(6),
                UsuarioId: reader.GetInt64(7),
                OcurridoEn: new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc))));
        }

        return resultado;
    }
}
