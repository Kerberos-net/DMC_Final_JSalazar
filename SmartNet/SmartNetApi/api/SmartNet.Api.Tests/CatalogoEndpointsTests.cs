using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// BACKLOG #18 PR8 — <c>CatalogoEndpoints</c> (api-catalogos-proveedores spec): authenticated,
/// read-only <c>GET /api/catalogos/proveedores?q=&amp;pagina=</c> over <c>dbo.Proveedor</c> for the
/// SPA proveedor picker. Real database via <see cref="SmartNetApiFactory"/>.
/// </summary>
public sealed class CatalogoEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private async Task SeedProveedorAsync(string codpro, string nombre, string? ruc = null) =>
        await Db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Proveedor (codpro, proveedor, coddocide, rucpro) " +
            $"VALUES ('{codpro}', N'{nombre}', NULL, {(ruc is null ? "NULL" : $"'{ruc}'")});");

    private static async Task<JsonElement> LeerCuerpoAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Get_MatchesByNameFragment_OrderedByNombre_WithCodigoNombreRuc()
    {
        await SeedProveedorAsync("P00A01", "ACME PERU SAC", "20100000001");
        await SeedProveedorAsync("P00A02", "ACME ANDINA EIRL", "20100000002");
        await SeedProveedorAsync("P00A03", "OTRO PROVEEDOR");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores?q=ACME");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        var resultados = cuerpo.GetProperty("resultados");
        Assert.Equal(2, resultados.GetArrayLength());
        Assert.Equal("ACME ANDINA EIRL", resultados[0].GetProperty("nombre").GetString());
        Assert.Equal("P00A02", resultados[0].GetProperty("codigo").GetString());
        Assert.Equal("20100000002", resultados[0].GetProperty("ruc").GetString());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_MatchesByRuc()
    {
        await SeedProveedorAsync("P00B01", "COMERCIAL DELTA", "20555555555");
        await SeedProveedorAsync("P00B02", "COMERCIAL GAMMA", "20111111111");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores?q=20555555555");

        var cuerpo = await LeerCuerpoAsync(response);
        var resultados = cuerpo.GetProperty("resultados");
        Assert.Equal(1, resultados.GetArrayLength());
        Assert.Equal("P00B01", resultados[0].GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Get_PagesResults_SecondPage_AndPastEnd()
    {
        for (var i = 0; i < 23; i++)
        {
            await SeedProveedorAsync($"P0C{i:D3}", $"PAGINADO {i:D3}");
        }
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var primera = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=1"));
        Assert.Equal(20, primera.GetProperty("resultados").GetArrayLength());
        Assert.True(primera.GetProperty("hayMas").GetBoolean());

        var segunda = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=2"));
        Assert.Equal(3, segunda.GetProperty("resultados").GetArrayLength());
        Assert.False(segunda.GetProperty("hayMas").GetBoolean());

        var pasada = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=5"));
        Assert.Equal(0, pasada.GetProperty("resultados").GetArrayLength());
        Assert.False(pasada.GetProperty("hayMas").GetBoolean());
    }

    [Theory]
    [InlineData("/api/catalogos/proveedores")]
    [InlineData("/api/catalogos/proveedores?q=")]
    [InlineData("/api/catalogos/proveedores?q=a")]
    public async Task Get_MissingOrShortQuery_Returns200_Empty(string url)
    {
        await SeedProveedorAsync("P00D01", "ALGUN PROVEEDOR");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        Assert.Equal(0, cuerpo.GetProperty("resultados").GetArrayLength());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_NoMatch_Returns200_Empty()
    {
        await SeedProveedorAsync("P00E01", "PROVEEDOR REAL");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=ZZZNOEXISTE"));

        Assert.Equal(0, cuerpo.GetProperty("resultados").GetArrayLength());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_ExcludesP00000_EvenWhenItMatchesTextually()
    {
        await SeedProveedorAsync("P00000", "VARIOS");
        await SeedProveedorAsync("P00F01", "VARIOS HERMANOS SAC", "20222222222");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=VARIOS"));

        var codigos = cuerpo.GetProperty("resultados").EnumerateArray()
            .Select(r => r.GetProperty("codigo").GetString())
            .ToArray();
        Assert.Equal(new[] { "P00F01" }, codigos);
    }

    [Fact]
    public async Task Get_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/proveedores?q=ACME");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- BACKLOG #22 PR2: plan contable (api spec req 4, 6, 8) ----

    private async Task SeedCuentaAsync(string cuenta, string descripcion, byte? nivel) =>
        await Db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.CuentaContable (cuenta, descripcion, nivel, ctarefleja, ctapuente) " +
            $"VALUES ('{cuenta}', N'{descripcion}', {(nivel is null ? "NULL" : nivel.Value.ToString())}, NULL, NULL);");

    private static int ContarFilasHoja(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, isEditable: false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        return sheetData.Elements<Row>().Count();
    }

    [Fact]
    public async Task PlanContable_Returns200_Unpaged_CamelCase_OrderedByCuenta_EsHojaImputableIffNivelNull()
    {
        await SeedCuentaAsync("40", "Tributos por pagar", nivel: 1);
        await SeedCuentaAsync("101", "Caja MN", nivel: null);
        await SeedCuentaAsync("10", "Efectivo y equivalentes", nivel: 2);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/plan-contable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        var items = cuerpo.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(new[] { "10", "101", "40" },
            items.EnumerateArray().Select(i => i.GetProperty("cuenta").GetString()).ToArray());
        Assert.Equal("Efectivo y equivalentes", items[0].GetProperty("descripcion").GetString());
        Assert.Equal(2, items[0].GetProperty("nivel").GetInt32());
        Assert.False(items[0].GetProperty("esHojaImputable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("nivel").ValueKind);
        Assert.True(items[1].GetProperty("esHojaImputable").GetBoolean());
    }

    [Fact]
    public async Task PlanContable_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/plan-contable");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlanContableExportacion_Returns200_XlsxHeaders_WorkbookRows_HonorsQ()
    {
        await SeedCuentaAsync("631111", "Fletes traslado de mercaderia", nivel: null);
        await SeedCuentaAsync("656111", "Utiles de escritorio", nivel: null);
        await SeedCuentaAsync("403", "Proveedores", nivel: 3);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/plan-contable/exportacion?q=flete");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains(".xlsx", disposition);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        Assert.Equal(2, ContarFilasHoja(bytes)); // 1 header + 1 filtered row
    }

    [Fact]
    public async Task PlanContableExportacion_WithoutACookie_Returns401_AndNoFile()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/plan-contable/exportacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PlanContableExportacion_HostileQuery_FilenameStaysConstantForm()
    {
        await SeedCuentaAsync("10", "Caja", nivel: 2);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/plan-contable/exportacion?q=../..%0d%0aX:1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.DoesNotContain("X:1", disposition);
        Assert.DoesNotContain('\r', disposition);
        Assert.DoesNotContain('\n', disposition);
        Assert.Matches(new Regex(@"filename=plan-contable-\d{4}-\d{2}-\d{2}\.xlsx"), disposition);
    }

    // ---- BACKLOG #22 PR5: proveedores catalogo mode (api spec req 1, 2, 3, 6, 8) ----

    private async Task SeedCatalogoProveedoresAsync(int cantidad, string prefijo)
    {
        for (var i = 0; i < cantidad; i++)
        {
            await SeedProveedorAsync($"C{i:D5}", $"{prefijo} {i:D3}", $"20{i:D9}");
        }
    }

    [Fact]
    public async Task Proveedores_CatalogoMode_ListsEveryProveedorInclP00000_WithPaginaBandejaEnvelope()
    {
        await SeedProveedorAsync("P00000", "VARIOS");
        await SeedCatalogoProveedoresAsync(2, "CATALOGO");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(
            await client.GetAsync("/api/catalogos/proveedores?modo=catalogo"));

        var items = cuerpo.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Contains("P00000", items.EnumerateArray().Select(i => i.GetProperty("codigo").GetString()));
        Assert.Equal(1, cuerpo.GetProperty("pagina").GetInt32());
        Assert.Equal(20, cuerpo.GetProperty("tamanioPagina").GetInt32());
        Assert.Equal(3, cuerpo.GetProperty("totalRegistros").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("totalPaginas").GetInt32());
        Assert.Equal("VARIOS", items[2].GetProperty("nombre").GetString());
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("ruc").ValueKind);
    }

    [Fact]
    public async Task Proveedores_CatalogoMode_PaginationEnvelopeIsAccurate()
    {
        await SeedCatalogoProveedoresAsync(45, "PAG");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(
            await client.GetAsync("/api/catalogos/proveedores?modo=catalogo&q=PAG&pagina=2&tamanio=20"));

        var items = cuerpo.GetProperty("items");
        Assert.Equal(20, items.GetArrayLength());
        Assert.Equal("PAG 020", items[0].GetProperty("nombre").GetString());
        Assert.Equal(2, cuerpo.GetProperty("pagina").GetInt32());
        Assert.Equal(45, cuerpo.GetProperty("totalRegistros").GetInt32());
        Assert.Equal(3, cuerpo.GetProperty("totalPaginas").GetInt32());
    }

    [Fact]
    public async Task Proveedores_CatalogoMode_TextFilter_MatchesNameRucOrCode()
    {
        await SeedProveedorAsync("C00001", "ACME PERU", "20100000001");
        await SeedProveedorAsync("C00002", "OTRO", "20999999999");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(
            await client.GetAsync("/api/catalogos/proveedores?modo=catalogo&q=ACME"));

        Assert.Equal(new[] { "C00001" },
            cuerpo.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).ToArray());
    }

    [Theory]
    [InlineData("codigo", "asc", new[] { "AAA 000", "AAA 001", "AAA 002" })]
    [InlineData("codigo", "desc", new[] { "AAA 002", "AAA 001", "AAA 000" })]
    [InlineData("proveedor", "desc", new[] { "AAA 002", "AAA 001", "AAA 000" })]
    [InlineData("ruc", "desc", new[] { "AAA 002", "AAA 001", "AAA 000" })]
    public async Task Proveedores_CatalogoMode_ServerSort(string orden, string direccion, string[] esperado)
    {
        await SeedCatalogoProveedoresAsync(3, "AAA");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync(
            $"/api/catalogos/proveedores?modo=catalogo&q=AAA&orden={orden}&direccion={direccion}"));

        Assert.Equal(esperado,
            cuerpo.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nombre").GetString()).ToArray());
    }

    [Theory]
    [InlineData("/api/catalogos/proveedores?modo=desconocido")]
    [InlineData("/api/catalogos/proveedores?modo=catalogo&orden=nombre")]
    [InlineData("/api/catalogos/proveedores?modo=catalogo&direccion=arriba")]
    [InlineData("/api/catalogos/proveedores?modo=catalogo&tamanio=7")]
    public async Task Proveedores_CatalogoMode_BadRequest_OnUnknownParams(string url)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Proveedores_CatalogoMode_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/proveedores?modo=catalogo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // REGRESSION — BACKLOG #18 picker contract is byte-frozen: modo absent or `picker` keeps the
    // `{resultados,hayMas}` shape, still excludes P00000, still empty for a too-short `q`.
    [Theory]
    [InlineData("/api/catalogos/proveedores?q=VARIOS")]
    [InlineData("/api/catalogos/proveedores?modo=picker&q=VARIOS")]
    public async Task Proveedores_PickerMode_Unchanged_ExcludesP00000_KeepsResultadosShape(string url)
    {
        await SeedProveedorAsync("P00000", "VARIOS");
        await SeedProveedorAsync("P00F01", "VARIOS HERMANOS SAC", "20222222222");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync(url));

        Assert.Equal(new[] { "P00F01" },
            cuerpo.GetProperty("resultados").EnumerateArray().Select(r => r.GetProperty("codigo").GetString()).ToArray());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
        Assert.False(cuerpo.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Proveedores_PickerMode_ShortQuery_StillEmpty_EvenWithSortParams()
    {
        await SeedProveedorAsync("P00D01", "ALGUN PROVEEDOR");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(
            await client.GetAsync("/api/catalogos/proveedores?q=a&orden=ruc&direccion=desc"));

        Assert.Equal(0, cuerpo.GetProperty("resultados").GetArrayLength());
    }

    [Fact]
    public async Task ProveedoresExportacion_Returns200_XlsxHeaders_WorkbookRows_HonorsQAndSort()
    {
        await SeedProveedorAsync("C00001", "EXPORT ACME", "20100000001");
        await SeedProveedorAsync("C00002", "EXPORT ACME DOS", "20100000002");
        await SeedProveedorAsync("C00003", "OTRA COSA", "20100000003");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(
            "/api/catalogos/proveedores/exportacion?q=EXPORT%20ACME&orden=codigo&direccion=asc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains(".xlsx", disposition);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(3, ContarFilasHoja(bytes)); // 1 header + 2 filtered rows
    }

    [Fact]
    public async Task ProveedoresExportacion_BadRequest_OnUnknownSort()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores/exportacion?orden=nombre");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProveedoresExportacion_WithoutACookie_Returns401_AndNoFile()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/proveedores/exportacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProveedoresExportacion_HostileQuery_FilenameStaysConstantForm()
    {
        await SeedProveedorAsync("C00001", "CUALQUIERA");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores/exportacion?q=../..%0d%0aX:1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.DoesNotContain("X:1", disposition);
        Assert.DoesNotContain('\r', disposition);
        Assert.DoesNotContain('\n', disposition);
        Assert.Matches(new Regex(@"filename=proveedores-\d{4}-\d{2}-\d{2}\.xlsx"), disposition);
    }
}
