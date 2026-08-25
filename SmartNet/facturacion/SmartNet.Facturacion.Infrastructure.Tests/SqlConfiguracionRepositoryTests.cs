using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md 5.3 — design D6: <c>SqlConfiguracionRepository</c> adapter over
/// <c>fact.Configuracion</c> (007_publicacion.sql:24-40, seeded by 009/013/020). GET by section,
/// UPDATE-only (unknown key -&gt; <see cref="ResultadoActualizacionConfiguracion.NoEncontrado"/>,
/// NEVER an INSERT), invalid value rejected without touching the prior value.
/// </summary>
public sealed class SqlConfiguracionRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ListarAsync_WithNoSeccion_ReturnsEntriesFromMultipleSections()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(null, CancellationToken.None);

        Assert.Contains(resultado, e => e.Seccion == "TELEGRAM" && e.Clave == "DESTINO_CHAT_ID");
        Assert.Contains(resultado, e => e.Seccion == "CORREO" && e.Clave == "DESTINATARIOS");
    }

    [Fact]
    public async Task ListarAsync_WithASeccion_ReturnsOnlyThatSectionsEntries()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync("TELEGRAM", CancellationToken.None);

        Assert.NotEmpty(resultado);
        Assert.All(resultado, e => Assert.Equal("TELEGRAM", e.Seccion));
    }

    [Fact]
    public async Task ListarAsync_ProjectsTipoValorValorPorDefectoAndDescripcion()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync("TELEGRAM", CancellationToken.None);

        var entrada = Assert.Single(resultado, e => e.Clave == "DESTINO_CHAT_ID");
        Assert.Equal("TEXTO", entrada.Tipo);
        Assert.Null(entrada.Valor);
        Assert.False(string.IsNullOrWhiteSpace(entrada.Descripcion));
    }

    [Fact]
    public async Task ActualizarAsync_WithAValidValue_UpdatesTheRow_AndStampsAudit()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_configuracion_test', '$argon2id$v=19$hash');");
        var usuarioId = await _db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_configuracion_test';");
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);

        var resultado = await sut.ActualizarAsync(
            "TELEGRAM", "DESTINO_CHAT_ID", "-100200300", actualizadoPorUsuarioId: usuarioId, CancellationToken.None);

        Assert.IsType<ResultadoActualizacionConfiguracion.Actualizado>(resultado);
        var valor = await _db.ExecuteScalarAsync<string>(
            "SELECT Valor FROM fact.Configuracion WHERE Seccion = 'TELEGRAM' AND Clave = 'DESTINO_CHAT_ID';");
        Assert.Equal("-100200300", valor!.TrimEnd());
        var actualizadoPor = await _db.ExecuteScalarAsync<long?>(
            "SELECT ActualizadoPorUsuarioId FROM fact.Configuracion WHERE Seccion = 'TELEGRAM' AND Clave = 'DESTINO_CHAT_ID';");
        Assert.Equal(usuarioId, actualizadoPor);
    }

    [Fact]
    public async Task ActualizarAsync_WithAnUnknownKey_ReturnsNoEncontrado_AndInsertsNothing()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);

        var resultado = await sut.ActualizarAsync("NO_EXISTE", "TAMPOCO", "x", actualizadoPorUsuarioId: null, CancellationToken.None);

        Assert.IsType<ResultadoActualizacionConfiguracion.NoEncontrado>(resultado);
        var cantidad = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.Configuracion WHERE Seccion = 'NO_EXISTE';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task ActualizarAsync_WithAnInvalidValueForTheDeclaredTipo_ReturnsValorInvalido_AndRetainsThePriorValue()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);
        // INGESTA.FRECUENCIA_SONDEO_MINUTOS is Tipo=ENTERO (009_datos_base.sql).
        await sut.ActualizarAsync("INGESTA", "FRECUENCIA_SONDEO_MINUTOS", "5", actualizadoPorUsuarioId: null, CancellationToken.None);

        var resultado = await sut.ActualizarAsync(
            "INGESTA", "FRECUENCIA_SONDEO_MINUTOS", "no-es-numero", actualizadoPorUsuarioId: null, CancellationToken.None);

        Assert.IsType<ResultadoActualizacionConfiguracion.ValorInvalido>(resultado);
        var valor = await _db.ExecuteScalarAsync<string>(
            "SELECT Valor FROM fact.Configuracion WHERE Seccion = 'INGESTA' AND Clave = 'FRECUENCIA_SONDEO_MINUTOS';");
        Assert.Equal("5", valor!.TrimEnd());
    }

    [Fact]
    public async Task ActualizarAsync_WithNullValue_IsAlwaysValid_AndClearsTheStoredValue()
    {
        var sut = new SqlConfiguracionRepository(_db.ConnectionString);
        await sut.ActualizarAsync("TELEGRAM", "DESTINO_CHAT_ID", "-1", actualizadoPorUsuarioId: null, CancellationToken.None);

        var resultado = await sut.ActualizarAsync("TELEGRAM", "DESTINO_CHAT_ID", null, actualizadoPorUsuarioId: null, CancellationToken.None);

        Assert.IsType<ResultadoActualizacionConfiguracion.Actualizado>(resultado);
        var valor = await _db.ExecuteScalarAsync<string>(
            "SELECT Valor FROM fact.Configuracion WHERE Seccion = 'TELEGRAM' AND Clave = 'DESTINO_CHAT_ID';");
        Assert.Null(valor);
    }
}
