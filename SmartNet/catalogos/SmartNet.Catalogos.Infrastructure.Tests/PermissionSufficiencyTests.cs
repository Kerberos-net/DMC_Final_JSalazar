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

    // ---------------------------------------------------------------------------------------
    // Task 3.11/3.12 -- the 3 satellite adapters (fact.ProveedorAtributo/MotivoAtributo/
    // SugerenciaCuenta). 008_usuarios_y_permisos.sql grants fact_api SELECT/INSERT/UPDATE (no
    // DELETE) on all 3 and DENYs fact_worker everything -- unlike the 5 read-only external
    // catalogs above, here usr_api and usr_worker are NOT expected to behave the same.
    // Replays the exact SQL text each satellite adapter issues (design.md's single
    // UPDATE .. IF @@ROWCOUNT = 0 INSERT .. shape for the two upserts/RegistrarUsoAsync).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UsrApi_CanExecute_ProveedorAtributoGuardarAsyncUpsert()
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.ProveedorAtributo
                SET EsRelacionada = @esRelacionada
                WHERE ProveedorCodigo = @proveedorCodigo;

                IF @@ROWCOUNT = 0
                    INSERT INTO fact.ProveedorAtributo (ProveedorCodigo, EsRelacionada)
                    VALUES (@proveedorCodigo, @esRelacionada);
                """;
            command.Parameters.AddWithValue("@proveedorCodigo", "P00001");
            command.Parameters.AddWithValue("@esRelacionada", true);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.True(rowsAffected >= 1);
    }

    [Fact]
    public async Task UsrApi_CanExecute_ProveedorAtributoObtenerAsyncSelect()
    {
        var rowsRead = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT ProveedorCodigo, EsRelacionada
                FROM fact.ProveedorAtributo
                WHERE ProveedorCodigo = @proveedorCodigo;
                """;
            command.Parameters.AddWithValue("@proveedorCodigo", "P00001");
            await using var reader = await command.ExecuteReaderAsync();
            var read = 0;
            while (await reader.ReadAsync())
            {
                read++;
            }
            return read;
        });

        Assert.True(rowsRead >= 0);
    }

    [Fact]
    public async Task UsrWorker_IsDenied_ProveedorAtributoWriteAndReadAccess()
    {
        var writeException = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrWorker, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO fact.ProveedorAtributo (ProveedorCodigo, EsRelacionada) VALUES ('P99999', 0);";
            return await command.ExecuteNonQueryAsync();
        }));
        Assert.NotNull(writeException);

        var readException = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrWorker, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ProveedorCodigo FROM fact.ProveedorAtributo;";
            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }));
        Assert.NotNull(readException);
    }

    [Fact]
    public async Task UsrApi_CanExecute_MotivoAtributoGuardarAsyncUpsert()
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.MotivoAtributo
                SET Activo = @activo, OrigenLibro = @origenLibro
                WHERE Motivo = @motivo;

                IF @@ROWCOUNT = 0
                    INSERT INTO fact.MotivoAtributo (Motivo, Activo, OrigenLibro)
                    VALUES (@motivo, @activo, @origenLibro);
                """;
            command.Parameters.AddWithValue("@motivo", 22);
            command.Parameters.AddWithValue("@activo", true);
            command.Parameters.AddWithValue("@origenLibro", "02");
            return await command.ExecuteNonQueryAsync();
        });

        Assert.True(rowsAffected >= 1);
    }

    [Fact]
    public async Task UsrWorker_IsDenied_MotivoAtributoWriteAccess()
    {
        var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrWorker, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO fact.MotivoAtributo (Motivo, Activo, OrigenLibro) VALUES (999, 1, '02');";
            return await command.ExecuteNonQueryAsync();
        }));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task UsrApi_CanExecute_SugerenciaCuentaRegistrarUsoAsyncUpsert()
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.SugerenciaCuenta
                SET Veces = Veces + 1, UltimoUso = @instante
                WHERE ProveedorCodigo = @proveedorCodigo AND Motivo = @motivo AND CuentaCodigo = @cuentaCodigo;

                IF @@ROWCOUNT = 0
                    INSERT INTO fact.SugerenciaCuenta (ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso)
                    VALUES (@proveedorCodigo, @motivo, @cuentaCodigo, 1, @instante);
                """;
            command.Parameters.AddWithValue("@proveedorCodigo", "P00001");
            command.Parameters.AddWithValue("@motivo", 22);
            command.Parameters.AddWithValue("@cuentaCodigo", "631111");
            command.Parameters.AddWithValue("@instante", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            return await command.ExecuteNonQueryAsync();
        });

        Assert.True(rowsAffected >= 1);
    }

    [Fact]
    public async Task UsrApi_CanExecute_SugerenciaCuentaListarPorProveedorYMotivoAsyncSelect()
    {
        var rowsRead = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso
                FROM fact.SugerenciaCuenta
                WHERE ProveedorCodigo = @proveedorCodigo AND Motivo = @motivo;
                """;
            command.Parameters.AddWithValue("@proveedorCodigo", "P00001");
            command.Parameters.AddWithValue("@motivo", 22);
            await using var reader = await command.ExecuteReaderAsync();
            var read = 0;
            while (await reader.ReadAsync())
            {
                read++;
            }
            return read;
        });

        Assert.True(rowsRead >= 0);
    }

    [Fact]
    public async Task UsrWorker_IsDenied_SugerenciaCuentaWriteAndReadAccess()
    {
        var writeException = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrWorker, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO fact.SugerenciaCuenta (ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso) " +
                "VALUES ('P99999', 999, '999999', 1, SYSUTCDATETIME());";
            return await command.ExecuteNonQueryAsync();
        }));
        Assert.NotNull(writeException);
    }

    // Neither fact_api nor fact_worker is granted DELETE on any of the 3 satellites
    // (008_usuarios_y_permisos.sql) -- confirms the "never DELETE" design constraint at the
    // permission layer, not just by the adapters' method shapes (NoWriteToDboStructuralTests only
    // covers the 5 external dbo.* catalogs).
    [Theory]
    [InlineData(UsrApi)]
    [InlineData(UsrWorker)]
    public async Task BothUsers_AreDenied_DeleteAccess_ToAllThreeSatellites(string user)
    {
        foreach (var table in new[] { "fact.ProveedorAtributo", "fact.MotivoAtributo", "fact.SugerenciaCuenta" })
        {
            var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(user, async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM {table} WHERE 1 = 0;";
                return await command.ExecuteNonQueryAsync();
            }));

            Assert.True(exception is not null, $"Expected {user} to be denied DELETE on {table}.");
        }
    }
}
