using SmartNet.Db.TestBootstrap;

namespace SmartNet.TiposCambio.Infrastructure.Tests;

/// <summary>
/// Tasks 2.8/2.9 -- analog of item #3's <c>PermissionSufficiencyTests</c>: replays the exact SQL
/// text <see cref="SqlTipoCambioRepository"/> issues through <c>ExecuteAsUserAsync</c> against the
/// real grants in <c>008_usuarios_y_permisos.sql</c> (lines 126-127): both <c>fact_api</c> AND
/// <c>fact_worker</c> receive identical <c>GRANT SELECT, INSERT, UPDATE</c> on
/// <c>fact.TipoCambio</c> — "ambos leen, ambos runtimes escriben segun su origen". Neither role is
/// granted <c>DELETE</c>.
/// </summary>
public sealed class PermissionSufficiencyTests : IAsyncLifetime
{
    private const string UsrApi = "usr_api";
    private const string UsrWorker = "usr_worker";

    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await SqlTipoCambioRepositoryTests.MigratedDatabase();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ---------------------------------------------------------------------------------------
    // ObtenerVigenteAsync's exact SELECT text.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_ObtenerVigenteAsyncSelect(string user)
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta) " +
            "VALUES ('2026-08-14', 'SBS', 3.799, 3.802, '2026-08-14T08:00:00');");

        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT Fecha, Origen, Compra, Venta, FechaConsulta
                FROM fact.TipoCambio
                WHERE Fecha = @fecha AND Origen IN ('SBS', 'MANUAL');
                """;
            command.Parameters.AddWithValue("@fecha", new DateTime(2026, 8, 14));
            await using var reader = await command.ExecuteReaderAsync();
            var read = 0;
            while (await reader.ReadAsync())
            {
                read++;
            }
            return read;
        });

        Assert.Equal(1, rowsRead);
    }

    // ---------------------------------------------------------------------------------------
    // CargarManualAsync's exact INSERT text.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_CargarManualAsyncInsert(string user)
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta, CargadoPorUsuarioId)
                VALUES (@fecha, 'MANUAL', @compra, @venta, @fechaConsulta, @cargadoPorUsuarioId);
                """;
            command.Parameters.AddWithValue("@fecha", new DateTime(2026, 8, 15));
            command.Parameters.AddWithValue("@compra", 3.80m);
            command.Parameters.AddWithValue("@venta", 3.85m);
            command.Parameters.AddWithValue("@fechaConsulta", new DateTime(2026, 8, 15, 9, 0, 0));
            command.Parameters.AddWithValue("@cargadoPorUsuarioId", DBNull.Value);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);

        // Clean up so the next Theory case (the other user) can insert the same PK again.
        await _db.ExecuteNonQueryAsync("DELETE FROM fact.TipoCambio WHERE Fecha = '2026-08-15' AND Origen = 'MANUAL';");
    }

    // ---------------------------------------------------------------------------------------
    // Neither role is granted DELETE on fact.TipoCambio (008_usuarios_y_permisos.sql).
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task BothUsers_AreDenied_DeleteAccess_ToTipoCambio(string user)
    {
        var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM fact.TipoCambio WHERE 1 = 0;";
            return await command.ExecuteNonQueryAsync();
        }));

        Assert.NotNull(exception);
    }
}
