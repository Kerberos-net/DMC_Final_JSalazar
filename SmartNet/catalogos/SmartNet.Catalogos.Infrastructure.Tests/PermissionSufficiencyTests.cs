using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Analogous to <c>SmartNet.Auth.Infrastructure.Tests.PermissionSufficiencyTests</c>: replays the
/// exact SQL text each of the 5 external-catalog adapters issues through
/// <c>ExecuteAsUserAsync</c> against the real grants shipped in <c>008_usuarios_y_permisos.sql</c>,
/// not a mock, not an elevated connection.
///
/// <para>
/// DEVIATION FROM THE ORIGINAL REQUEST (documented, not silent): the request assumed
/// <c>usr_worker</c> is DENIED read access to the 5 <c>dbo.*</c> catalogs. Verified against the
/// real grants in <c>008_usuarios_y_permisos.sql</c> (lines 147-156): both <c>fact_api</c> AND
/// <c>fact_worker</c> receive <c>GRANT SELECT</c> on all 5 external catalogs — confirmed by
/// <c>SmartNet.Db.Runner.Tests.PermissionMatrixTests.BothUsers_CanSelect_FiveExternalDboTables_NeitherCanWrite</c>.
/// The write-denial the original request had in mind applies to WRITE statements (INSERT/UPDATE/
/// DELETE against dbo.*), which none of these 5 read-only adapters issues in the first place —
/// covered separately by <see cref="NoWriteToDboStructuralTests"/>. So this suite asserts BOTH
/// <c>usr_api</c> AND <c>usr_worker</c> can execute every SELECT these adapters issue (per CLAUDE.md
/// rule 1: the real grant is the source of truth, not silently adjusted to match a wrong assumption).
/// </para>
/// </summary>
public sealed class PermissionSufficiencyTests : IAsyncLifetime
{
    private const string UsrApi = "usr_api";
    private const string UsrWorker = "usr_worker";

    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        await _db.SeedDocumentoIdentidadAsync("06", "RUC");
        await _db.SeedProveedorAsync("P00001", "Proveedor Permsuff", coddocide: "06", rucpro: "20999999999");
        await _db.SeedCuentaContableAsync("631111", "Fletes traslado de mercaderia");
        await _db.SeedOrigenAsync("01", "Compras");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<TestDatabaseFixture> MigratedDatabase()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            var exitCode = db.RunMigrations();
            Assert.Equal(0, exitCode);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------------------------
    // SqlCuentaContableRepository -- exact SQL text from SqlCuentaContableRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_CuentaContableObtenerAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente
                FROM dbo.CuentaContable
                WHERE cuenta = @cuenta;
                """;
            command.Parameters.AddWithValue("@cuenta", "631111");
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
    // SqlMotivoRepository -- exact SQL text from SqlMotivoRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_MotivoObtenerAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT codigo, motivo, cuenta
                FROM dbo.Motivo
                WHERE codigo = @codigo;
                """;
            command.Parameters.AddWithValue("@codigo", 22);
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
    // SqlProveedorRepository -- exact SQL text from SqlProveedorRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_ProveedorObtenerPorCodigoAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT codpro, proveedor, coddocide, rucpro
                FROM dbo.Proveedor
                WHERE codpro = @codpro;
                """;
            command.Parameters.AddWithValue("@codpro", "P00001");
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

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_ProveedorBuscarPorRucAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT codpro, proveedor, coddocide, rucpro
                FROM dbo.Proveedor
                WHERE rucpro = @rucpro;
                """;
            command.Parameters.AddWithValue("@rucpro", "20999999999");
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
    // SqlOrigenRepository -- exact SQL text from SqlOrigenRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_OrigenListarAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT codigo, origen FROM dbo.Origen;";
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
    // SqlDocumentoIdentidadRepository -- exact SQL text from SqlDocumentoIdentidadRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task CanExecute_DocumentoIdentidadListarAsyncSelect(string user)
    {
        var rowsRead = await _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT coddocide, nomdocide FROM dbo.DocumentoIdentidad;";
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
    // Negative coverage: both users remain denied any WRITE against the 5 external catalogs —
    // this is the actual denial 008_usuarios_y_permisos.sql enforces (object-level GRANT SELECT
    // only, no INSERT/UPDATE/DELETE for either principal on any dbo.* table).
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task BothUsers_AreDenied_WriteAccess_ToCuentaContable(string user)
    {
        var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE dbo.CuentaContable SET descripcion = descripcion WHERE 1 = 0;";
            return await command.ExecuteNonQueryAsync();
        }));

        Assert.NotNull(exception);
    }
}
