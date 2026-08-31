using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// BACKLOG #23 — <c>RegistroCompraEndpoints</c> (registro-compra-api spec req 1/2/3/5). Authenticated,
/// read-only <c>GET /api/registro-compra</c> (listado por período), <c>/{asientoId}</c> (detalle de
/// líneas) and <c>/export</c> (.xlsx del período) over <c>fact.AsientoContable + fact.Factura</c>.
/// Real database via <see cref="SmartNetApiFactory"/>, real session cookie —
/// <c>CatalogoEndpointsTests</c> style, per the integration-spa-api harness doctrine (never an
/// in-memory repo, never an injected principal).
/// </summary>
public sealed class RegistroCompraEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<JsonElement> LeerCuerpoAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private Task SeedProveedorAsync(string codpro, string nombre) =>
        Db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Proveedor (codpro, proveedor, coddocide, rucpro) VALUES ('{codpro}', N'{nombre}', NULL, NULL);");

    /// <summary>A VALIDADA factura + CONFIRMADO asiento that qualifies for the register.</summary>
    private async Task<long> SeedAsientoValidoAsync(
        string fechaContable = "2026-08-10",
        string? numeroAsiento = "02-2026-08-000001",
        decimal? basePEN = 100.00m,
        decimal? igvPEN = 18.00m,
        decimal? netoPEN = 118.00m,
        string origenLibro = "02",
        bool conLineas = true)
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: "VALIDADA");
        var lineas = conLineas
            ? new (short, string, string, decimal, decimal, string?, string?)[]
            {
                (1, "PRINCIPAL", "D", 100.00m, 0m, "639915", "Otros servicios"),
                (2, "PRINCIPAL", "D", 18.00m, 0m, "401111", "IGV"),
                (3, "PRINCIPAL", "H", 0m, 118.00m, "421001", "Facturas por pagar"),
            }
            : null;
        return await Db.InsertarAsientoConfirmadoAsync(
            facturaId, fechaContable: fechaContable, numeroAsiento: numeroAsiento,
            basePEN: basePEN, igvPEN: igvPEN, netoPEN: netoPEN, origenLibro: origenLibro, lineas: lineas);
    }

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ---- 3.1 auth + camelCase ----

    [Theory]
    [InlineData("/api/registro-compra?periodo=2026-08")]
    [InlineData("/api/registro-compra/1")]
    [InlineData("/api/registro-compra/export?periodo=2026-08")]
    public async Task WithoutACookie_Returns401(string url)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listado_FieldsAreCamelCase()
    {
        await SeedAsientoValidoAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));

        Assert.True(cuerpo.TryGetProperty("items", out var items));
        Assert.True(cuerpo.TryGetProperty("totalRegistros", out _));
        Assert.True(cuerpo.TryGetProperty("tamanioPagina", out _));
        var fila = items[0];
        foreach (var campo in new[]
        {
            "asientoContableId", "numeroComprobante", "numeroAsiento", "origenLibro",
            "proveedorCodigo", "proveedorNombre", "glosa", "fechaContable",
            "tipoCambioVenta", "basePEN", "igvPEN", "netoPEN",
        })
        {
            Assert.True(fila.TryGetProperty(campo, out _), $"missing camelCase field {campo}");
        }
    }

    // ---- 3.2 period filter + row predicate + proveedorNombre + origenLibro ----

    [Fact]
    public async Task Listado_PeriodIncludesFirstAndLastDay_ExcludesAdjacentMonthEdges()
    {
        await SeedAsientoValidoAsync(fechaContable: "2026-08-01", numeroAsiento: "A-01", conLineas: false);
        await SeedAsientoValidoAsync(fechaContable: "2026-08-31", numeroAsiento: "A-31", conLineas: false);
        await SeedAsientoValidoAsync(fechaContable: "2026-07-31", numeroAsiento: "A-J31", conLineas: false);
        await SeedAsientoValidoAsync(fechaContable: "2026-09-01", numeroAsiento: "A-S01", conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));

        var numeros = cuerpo.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("numeroAsiento").GetString()).ToArray();
        Assert.Equal(new[] { "A-01", "A-31" }, numeros);
        Assert.Equal(2, cuerpo.GetProperty("totalRegistros").GetInt32());
    }

    [Theory]
    [InlineData("PENDIENTE_VALIDACION", "CONFIRMADO")]
    [InlineData("DESCARTADA", "CONFIRMADO")]
    [InlineData("VALIDADA", "ANULADO")]
    public async Task Listado_ExcludesRowsOutsideTheRegister(string facturaEstado, string asientoEstado)
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: facturaEstado);
        await Db.InsertarAsientoConfirmadoAsync(facturaId, fechaContable: "2026-08-10", estado: asientoEstado);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));

        Assert.Equal(0, cuerpo.GetProperty("totalRegistros").GetInt32());
    }

    [Fact]
    public async Task Listado_ProveedorNombre_NullWhenCodeAbsentFromDboProveedor_OtherwiseTheName()
    {
        await SeedAsientoValidoAsync(numeroAsiento: "SIN-NOMBRE", conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var sinNombre = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));
        Assert.Equal(JsonValueKind.Null, sinNombre.GetProperty("items")[0].GetProperty("proveedorNombre").ValueKind);
        Assert.Equal("P00123", sinNombre.GetProperty("items")[0].GetProperty("proveedorCodigo").GetString());

        await SeedProveedorAsync("P00123", "PROVEEDOR CONOCIDO SAC");
        var conNombre = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));
        Assert.Equal("PROVEEDOR CONOCIDO SAC",
            conNombre.GetProperty("items")[0].GetProperty("proveedorNombre").GetString());
    }

    [Fact]
    public async Task Listado_OrigenLibro_EchoedVerbatim_NotHardCoded()
    {
        await SeedAsientoValidoAsync(origenLibro: "07", conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));

        Assert.Equal("07", cuerpo.GetProperty("items")[0].GetProperty("origenLibro").GetString());
    }

    // ---- 3.3 envelope + pagination + empty period ----

    [Fact]
    public async Task Listado_PaginationEnvelope_TotalRegistrosViaCountOver_StableOrderAcrossPages()
    {
        for (var i = 1; i <= 25; i++)
        {
            await SeedAsientoValidoAsync(
                fechaContable: "2026-08-10", numeroAsiento: $"02-2026-08-{i:D6}", conLineas: false);
        }
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var p1 = await LeerCuerpoAsync(
            await client.GetAsync("/api/registro-compra?periodo=2026-08&pagina=1&tamanioPagina=20"));
        var p2 = await LeerCuerpoAsync(
            await client.GetAsync("/api/registro-compra?periodo=2026-08&pagina=2&tamanioPagina=20"));

        Assert.Equal(20, p1.GetProperty("items").GetArrayLength());
        Assert.Equal(5, p2.GetProperty("items").GetArrayLength());
        Assert.Equal(25, p1.GetProperty("totalRegistros").GetInt32());
        Assert.Equal(2, p1.GetProperty("totalPaginas").GetInt32());

        var ids1 = p1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("asientoContableId").GetInt64());
        var ids2 = p2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("asientoContableId").GetInt64());
        Assert.Empty(ids1.Intersect(ids2));
    }

    [Fact]
    public async Task Listado_EmptyPeriod_Returns200_EmptyItems_TotalZero_Not404()
    {
        await SeedAsientoValidoAsync(fechaContable: "2026-08-10", conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/registro-compra?periodo=2026-05");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        Assert.Equal(0, cuerpo.GetProperty("items").GetArrayLength());
        Assert.Equal(0, cuerpo.GetProperty("totalRegistros").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("totalPaginas").GetInt32());
    }

    // ---- 3.4 malformed periodo + tamanioPagina allow-list ----

    [Theory]
    [InlineData("/api/registro-compra")]
    [InlineData("/api/registro-compra?periodo=")]
    [InlineData("/api/registro-compra?periodo=2026-13")]
    [InlineData("/api/registro-compra?periodo=agosto")]
    [InlineData("/api/registro-compra?periodo=2026-8")]
    [InlineData("/api/registro-compra?periodo=2026-08-01")]
    public async Task Listado_MalformedOrMissingPeriodo_Returns400(string url)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public async Task Listado_TamanioPagina_AllowList_Accepted(int tamanio)
    {
        await SeedAsientoValidoAsync(conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/registro-compra?periodo=2026-08&tamanioPagina={tamanio}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tamanio, (await LeerCuerpoAsync(response)).GetProperty("tamanioPagina").GetInt32());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(0)]
    public async Task Listado_TamanioPagina_OutOfAllowList_Returns400(int tamanio)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/registro-compra?periodo=2026-08&tamanioPagina={tamanio}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listado_TamanioPagina_DefaultsTo20()
    {
        await SeedAsientoValidoAsync(conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/registro-compra?periodo=2026-08"));

        Assert.Equal(20, cuerpo.GetProperty("tamanioPagina").GetInt32());
    }

    // ---- 3.5 detalle ----

    [Fact]
    public async Task Detalle_HappyPath_ReturnsCabeceraAndLinesOrderedByOrden()
    {
        var asientoId = await SeedAsientoValidoAsync(conLineas: true);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/registro-compra/{asientoId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        Assert.Equal(asientoId, cuerpo.GetProperty("cabecera").GetProperty("asientoContableId").GetInt64());
        var lineas = cuerpo.GetProperty("lineas");
        Assert.Equal(3, lineas.GetArrayLength());
        Assert.Equal(new[] { 1, 2, 3 },
            lineas.EnumerateArray().Select(l => l.GetProperty("orden").GetInt32()).ToArray());
        Assert.Equal("639915", lineas[0].GetProperty("cuentaCodigo").GetString());
    }

    [Fact]
    public async Task Detalle_QualifyingAsientoWithNoLines_Returns200_EmptyLineas()
    {
        var asientoId = await SeedAsientoValidoAsync(conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/registro-compra/{asientoId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await LeerCuerpoAsync(response)).GetProperty("lineas").GetArrayLength());
    }

    [Fact]
    public async Task Detalle_Nonexistent_Returns404()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/registro-compra/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("PENDIENTE_VALIDACION", "CONFIRMADO")]
    [InlineData("DESCARTADA", "CONFIRMADO")]
    [InlineData("VALIDADA", "ANULADO")]
    public async Task Detalle_AsientoOutsideTheRegister_Returns404_NoSideChannel(string facturaEstado, string asientoEstado)
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: facturaEstado);
        var asientoId = await Db.InsertarAsientoConfirmadoAsync(
            facturaId, fechaContable: "2026-08-10", estado: asientoEstado);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/registro-compra/{asientoId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- 3.6 export ----

    [Fact]
    public async Task Export_Returns200_XlsxContentTypeAndAttachment()
    {
        await SeedAsientoValidoAsync(conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/registro-compra/export?periodo=2026-08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(XlsxMime, response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains("registro-compra-2026-08.xlsx", disposition);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/api/registro-compra/export")]
    [InlineData("/api/registro-compra/export?periodo=2026-13")]
    [InlineData("/api/registro-compra/export?periodo=agosto")]
    public async Task Export_MalformedOrMissingPeriodo_Returns400(string url)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Export_FilenameInjectionAttempt_Returns400()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/registro-compra/export?periodo=2026-08%0D%0AX");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Export_FilenameIsReconstructedFromParsedInts()
    {
        await SeedAsientoValidoAsync(fechaContable: "2026-08-10", conLineas: false);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/registro-compra/export?periodo=2026-08");
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();

        Assert.DoesNotContain('\r', disposition);
        Assert.DoesNotContain('\n', disposition);
        Assert.Matches(new Regex(@"filename=registro-compra-\d{4}-\d{2}\.xlsx"), disposition);
    }
}
