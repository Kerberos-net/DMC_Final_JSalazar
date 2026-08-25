using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure;

/// <summary>
/// design D6 — adaptador SQL de <see cref="IConfiguracionRepository"/> sobre
/// <c>fact.Configuracion</c> (007_publicacion.sql:24-40). <see cref="ActualizarAsync"/> lee la fila
/// (para conocer su <c>Tipo</c> y decidir 404 vs 422 ANTES de escribir), valida con
/// <see cref="ValorDeConfiguracion.Validar"/> (ADR 0019 — la regla en sí es pura, este adaptador
/// solo la invoca) y solo entonces hace el UPDATE — nunca un INSERT (spec.md "unknown key is 404,
/// never a silent INSERT").
/// </summary>
public sealed class SqlConfiguracionRepository : IConfiguracionRepository
{
    private readonly string _connectionString;

    public SqlConfiguracionRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<ConfiguracionEntrada>> ListarAsync(string? seccion, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(seccion)
            ? """
              SELECT Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion
              FROM fact.Configuracion
              ORDER BY Seccion, Clave;
              """
            : """
              SELECT Seccion, Clave, Tipo, Valor, ValorPorDefecto, Descripcion
              FROM fact.Configuracion
              WHERE Seccion = @seccion
              ORDER BY Seccion, Clave;
              """;
        if (!string.IsNullOrWhiteSpace(seccion))
        {
            command.Parameters.AddWithValue("@seccion", seccion);
        }

        var resultado = new List<ConfiguracionEntrada>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new ConfiguracionEntrada(
                Seccion: reader.GetString(0).TrimEnd(),
                Clave: reader.GetString(1).TrimEnd(),
                Tipo: reader.GetString(2).TrimEnd(),
                Valor: reader.IsDBNull(3) ? null : reader.GetString(3),
                ValorPorDefecto: reader.IsDBNull(4) ? null : reader.GetString(4),
                Descripcion: reader.GetString(5)));
        }

        return resultado;
    }

    public async Task<ResultadoActualizacionConfiguracion> ActualizarAsync(
        string seccion, string clave, string? valor, long? actualizadoPorUsuarioId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var tipo = await LeerTipoAsync(connection, seccion, clave, ct);
        if (tipo is null)
        {
            return new ResultadoActualizacionConfiguracion.NoEncontrado();
        }

        if (!ValorDeConfiguracion.Validar(tipo, valor))
        {
            return new ResultadoActualizacionConfiguracion.ValorInvalido();
        }

        await using var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE fact.Configuracion
            SET Valor = @valor, ActualizadoPorUsuarioId = @usuarioId, ActualizadoEn = SYSUTCDATETIME()
            WHERE Seccion = @seccion AND Clave = @clave;
            """;
        update.Parameters.AddWithValue("@valor", (object?)valor ?? DBNull.Value);
        update.Parameters.AddWithValue("@usuarioId", (object?)actualizadoPorUsuarioId ?? DBNull.Value);
        update.Parameters.AddWithValue("@seccion", seccion);
        update.Parameters.AddWithValue("@clave", clave);
        await update.ExecuteNonQueryAsync(ct);

        return new ResultadoActualizacionConfiguracion.Actualizado();
    }

    private static async Task<string?> LeerTipoAsync(SqlConnection connection, string seccion, string clave, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Tipo FROM fact.Configuracion WHERE Seccion = @seccion AND Clave = @clave;";
        command.Parameters.AddWithValue("@seccion", seccion);
        command.Parameters.AddWithValue("@clave", clave);

        var resultado = await command.ExecuteScalarAsync(ct);
        return resultado is string tipo ? tipo.TrimEnd() : null;
    }
}
