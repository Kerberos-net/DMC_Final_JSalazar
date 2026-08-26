using SmartNet.Db.TestBootstrap;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Task 3.10 -- replays the exact SQL text this project's adapters issue through
/// <c>ExecuteAsUserAsync</c> against the real grants in <c>008_usuarios_y_permisos.sql</c> (lines
/// 50/55/113-115): <c>fact_api</c> gets SELECT/UPDATE on <c>fact.InboxEvent</c> and
/// SELECT/INSERT/UPDATE on <c>fact.Factura</c>/<c>fact.FacturaExtraccion</c>; <c>fact_worker</c> is
/// DENYd all four verbs on <c>fact.Factura</c>/<c>fact.FacturaExtraccion</c>; <c>fact_api</c> is
/// DENYd all four verbs on <c>fact.Procesamiento</c> (spec.md "Consumer never touches Procesamiento").
/// </summary>
public sealed class PermissionSufficiencyTests : IAsyncLifetime
{
    private const string UsrApi = "usr_api";
    private const string UsrWorker = "usr_worker";

    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task UsrApi_CanSelectAndUpdate_InboxEvent()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");

        var rowsRead = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM fact.InboxEvent WHERE EstadoConsumo = 'PENDIENTE';";
            return (int)(await command.ExecuteScalarAsync())!;
        });
        Assert.Equal(1, rowsRead);

        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'x' WHERE InboxEventId = {inboxEventId};";
            return await command.ExecuteNonQueryAsync();
        });
        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public async Task UsrApi_CanInsert_FacturaAndFacturaExtraccion()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();

        var facturaId = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 INSERT INTO fact.Factura (ProcesamientoId, ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision)
                 OUTPUT INSERTED.FacturaId
                 VALUES ({procesamientoId}, 'P00000', '01', 100.00, 'PEN', '2026-08-09');
                 """;
            return (long)(await command.ExecuteScalarAsync())!;
        });

        var extraccionRows = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO fact.FacturaExtraccion (FacturaId, CampoNombre, ValorExtraido, Fuente) " +
                $"VALUES ({facturaId}, 'total', '100.00', 'XML');";
            return await command.ExecuteNonQueryAsync();
        });
        Assert.Equal(1, extraccionRows);
    }

    /// <summary>BACKLOG #12 task 2.3 -- replays the exact literal INSERT
    /// <see cref="SqlPromocionRepository"/> issues for the schema-016 projection row, as
    /// <c>usr_api</c>, with no <c>SELECT</c> against <c>fact.DocumentoRecibido</c> anywhere in this
    /// session (ADR 0003 §Privadas symmetry: the row is built purely from the InboxEvent payload,
    /// never read back from Python's table).</summary>
    [Fact]
    public async Task UsrApi_CanInsert_DocumentoFactura_WithoutEverSelectingDocumentoRecibido()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();

        var facturaId = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 INSERT INTO fact.Factura (ProcesamientoId, ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision)
                 OUTPUT INSERTED.FacturaId
                 VALUES ({procesamientoId}, 'P00000', '01', 100.00, 'PEN', '2026-08-09');
                 """;
            return (long)(await command.ExecuteScalarAsync())!;
        });

        var documentoRows = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 INSERT INTO fact.DocumentoFactura (FacturaId, DocumentoRecibidoId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes)
                 VALUES ({facturaId}, 999, 'f.pdf', 'application/pdf', '/f.pdf', 10);
                 """;
            return await command.ExecuteNonQueryAsync();
        });
        Assert.Equal(1, documentoRows);
    }

    [Fact]
    public async Task UsrApi_CanSelect_DboProveedor()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO dbo.Proveedor (codpro, proveedor, rucpro) VALUES ('P00123', 'Acme SAC', '20100000001');");

        var rows = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.Proveedor WHERE rucpro = '20100000001';";
            return (int)(await command.ExecuteScalarAsync())!;
        });
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task UsrApi_IsDenied_SelectOnProcesamiento()
    {
        await _db.InsertarProcesamientoAsync();

        var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM fact.Procesamiento;";
            return await command.ExecuteScalarAsync();
        }));

        Assert.NotNull(exception);
    }

    [Theory]
    [InlineData("SELECT")]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public async Task UsrWorker_IsDenied_EveryVerb_OnFactura(string verb)
    {
        var sql = verb switch
        {
            "SELECT" => "SELECT COUNT(*) FROM fact.Factura;",
            "INSERT" => "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) " +
                        "VALUES ('P00000', '01', 1, 'PEN', '2026-08-09');",
            "UPDATE" => "UPDATE fact.Factura SET Estado = 'VALIDADA' WHERE 1 = 0;",
            "DELETE" => "DELETE FROM fact.Factura WHERE 1 = 0;",
            _ => throw new InvalidOperationException(),
        };

        var exception = await Record.ExceptionAsync(() => _db.ExecuteAsUserAsync(UsrWorker, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        }));

        Assert.NotNull(exception);
    }
}
