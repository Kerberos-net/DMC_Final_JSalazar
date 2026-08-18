using Microsoft.Data.SqlClient;
using SmartNet.TiposCambio.Core;

namespace SmartNet.TiposCambio.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.TipoCambio</c> for <see cref="ITipoCambioRepository"/> (design.md
/// Interfaces/Contracts). Decision 1: <see cref="ObtenerVigenteAsync"/> SELECTs both origin rows
/// by PK <c>(Fecha, Origen)</c> (max 2 rows) and delegates the SBS&gt;MANUAL priority to
/// <see cref="SeleccionDeTipoCambio.Seleccionar"/> -- no ORDER BY/CASE priority logic here.
/// Decision 4: <see cref="CargarManualAsync"/> hardcodes <c>Origen='MANUAL'</c>, no
/// <see cref="OrigenTipoCambio"/> parameter.
/// </summary>
public sealed class SqlTipoCambioRepository : ITipoCambioRepository
{
    private readonly string _connectionString;

    public SqlTipoCambioRepository(string connectionString) => _connectionString = connectionString;

    public async Task<ResultadoTipoCambio> ObtenerVigenteAsync(DateOnly fecha, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Fecha, Origen, Compra, Venta, FechaConsulta
            FROM fact.TipoCambio
            WHERE Fecha = @fecha AND Origen IN ('SBS', 'MANUAL');
            """;
        command.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));

        var candidatas = new List<TipoCambio>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                candidatas.Add(Map(reader));
            }
        }

        return SeleccionDeTipoCambio.Seleccionar(fecha, candidatas);
    }

    public async Task<ResultadoCargaManual> CargarManualAsync(
        DateOnly fecha,
        decimal compra,
        decimal venta,
        DateTime fechaConsulta,
        long? cargadoPorUsuarioId,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta, CargadoPorUsuarioId)
            VALUES (@fecha, 'MANUAL', @compra, @venta, @fechaConsulta, @cargadoPorUsuarioId);
            """;
        command.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@compra", compra);
        command.Parameters.AddWithValue("@venta", venta);
        command.Parameters.AddWithValue("@fechaConsulta", fechaConsulta);
        command.Parameters.AddWithValue("@cargadoPorUsuarioId", (object?)cargadoPorUsuarioId ?? DBNull.Value);

        try
        {
            await command.ExecuteNonQueryAsync(ct);
            return ResultadoCargaManual.Cargada;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return ResultadoCargaManual.YaExistia;
        }
    }

    private static TipoCambio Map(SqlDataReader reader)
    {
        var origenTexto = reader.GetString(1).TrimEnd();
        var origen = origenTexto switch
        {
            "SBS" => OrigenTipoCambio.Sbs,
            "MANUAL" => OrigenTipoCambio.Manual,
            _ => (OrigenTipoCambio)(-1), // unknown Origen -- SeleccionDeTipoCambio discards it, never selects it
        };

        return new TipoCambio(
            Fecha: DateOnly.FromDateTime(reader.GetDateTime(0)),
            Origen: origen,
            Compra: reader.GetDecimal(2),
            Venta: reader.GetDecimal(3),
            FechaConsulta: reader.GetDateTime(4));
    }
}
