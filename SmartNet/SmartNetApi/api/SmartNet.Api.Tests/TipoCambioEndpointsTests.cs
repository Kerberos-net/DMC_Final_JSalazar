using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 4 (PR 4), task 4.1 — <c>TipoCambioEndpoints</c> (spec.md
/// <c>tipos-de-cambio</c>: "POST /api/tipos-cambio exposes carga-manual over HTTP with
/// problem+json errors") against the real database via <see cref="SmartNetApiFactory"/>.
/// </summary>
public sealed class TipoCambioEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    [Fact]
    public async Task Post_ForAnUncoveredDate_Returns201_AndInsertsAManualRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 15), 3.85m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-15' AND Origen = 'MANUAL';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Post_ForADateThatAlreadyHasAManualRow_Returns409_AndDoesNotOverwrite()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);
        await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 16), 3.85m));

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 16), 3.90m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var tasa = await Db.ExecuteScalarAsync<decimal>(
            "SELECT Venta FROM fact.TipoCambio WHERE Fecha = '2026-08-16' AND Origen = 'MANUAL';");
        Assert.Equal(3.85m, tasa);
    }

    [Fact]
    public async Task Post_WithAMissingTasa_Returns400_AndInsertsNoRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 17), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-17';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Post_WithANonPositiveTasa_Returns400_AndInsertsNoRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 18), 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-18';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Post_WhenASbsRowAlreadyExistsForTheDate_ManualLoadStillSucceedsIndependently()
    {
        await Db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
            VALUES ('2026-08-19', 'SBS', 3.80, 3.82, SYSUTCDATETIME());
            """);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 19), 3.85m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-19';");
        Assert.Equal(2, cantidad);
    }

    [Fact]
    public async Task Post_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 20), 3.85m));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- BACKLOG #22 PR7: tipo de cambio history (catalog-queries-api spec req 5, 6, 7, 8) ----

    private async Task SeedTipoCambioAsync(string fecha, string origen, decimal compra, decimal venta) =>
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta) VALUES " +
            $"('{fecha}', '{origen}', {compra.ToString(CultureInfo.InvariantCulture)}, " +
            $"{venta.ToString(CultureInfo.InvariantCulture)}, '{fecha}T08:00:00');");

    private static async Task<JsonElement> LeerCuerpoAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private static int ContarFilasHoja(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, isEditable: false);
        var sheetData = doc.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
        return sheetData.Elements<Row>().Count();
    }

    [Fact]
    public async Task GetHistorico_Returns200_BothOrigins_CamelCase_OrigenAsString_OrderedByFechaThenOrigen()
    {
        await SeedTipoCambioAsync("2026-08-14", "SBS", 3.799m, 3.802m);
        await SeedTipoCambioAsync("2026-08-14", "MANUAL", 3.700m, 3.750m);
        await SeedTipoCambioAsync("2026-08-15", "SBS", 3.805m, 3.808m);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/tipos-cambio?desde=2026-08-14&hasta=2026-08-15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        var items = cuerpo.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("2026-08-14", items[0].GetProperty("fecha").GetString());
        Assert.Equal("MANUAL", items[0].GetProperty("origen").GetString());
        Assert.Equal("2026-08-14", items[1].GetProperty("fecha").GetString());
        Assert.Equal("SBS", items[1].GetProperty("origen").GetString());
        Assert.Equal("2026-08-15", items[2].GetProperty("fecha").GetString());
        Assert.Equal(3.700, items[0].GetProperty("compra").GetDouble(), 3);
        Assert.Equal(3.750, items[0].GetProperty("venta").GetDouble(), 3);
        Assert.Equal(JsonValueKind.String, items[0].GetProperty("fechaConsulta").ValueKind);
    }

    [Fact]
    public async Task GetHistorico_ExcludesRowsOutsideTheInclusiveRange()
    {
        await SeedTipoCambioAsync("2026-08-13", "SBS", 3.79m, 3.80m);
        await SeedTipoCambioAsync("2026-08-14", "SBS", 3.79m, 3.80m);
        await SeedTipoCambioAsync("2026-08-16", "SBS", 3.79m, 3.80m);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(
            await client.GetAsync("/api/tipos-cambio?desde=2026-08-14&hasta=2026-08-15"));

        Assert.Equal(new[] { "2026-08-14" },
            cuerpo.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("fecha").GetString()).ToArray());
    }

    [Theory]
    [InlineData("/api/tipos-cambio?hasta=2026-08-15")]
    [InlineData("/api/tipos-cambio?desde=2026-08-15")]
    [InlineData("/api/tipos-cambio?desde=noesfecha&hasta=2026-08-15")]
    [InlineData("/api/tipos-cambio?desde=2026-08-15&hasta=2026-08-10")]
    [InlineData("/api/tipos-cambio?desde=2025-01-01&hasta=2026-01-02")]
    public async Task GetHistorico_Returns400_OnBadRange(string url)
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorico_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/tipos-cambio?desde=2026-08-14&hasta=2026-08-15");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistoricoExportacion_Returns200_XlsxHeaders_WorkbookRows()
    {
        await SeedTipoCambioAsync("2026-08-14", "SBS", 3.799m, 3.802m);
        await SeedTipoCambioAsync("2026-08-14", "MANUAL", 3.700m, 3.750m);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/tipos-cambio/exportacion?desde=2026-08-14&hasta=2026-08-15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains(".xlsx", disposition);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(3, ContarFilasHoja(bytes)); // 1 header + 2 rows
    }

    [Fact]
    public async Task GetHistoricoExportacion_Returns400_OnBadRange()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/tipos-cambio/exportacion?desde=2026-08-15");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistoricoExportacion_WithoutACookie_Returns401_AndNoFile()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/tipos-cambio/exportacion?desde=2026-08-14&hasta=2026-08-15");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetHistoricoExportacion_HostileExtraParam_FilenameStaysConstantForm()
    {
        await SeedTipoCambioAsync("2026-08-14", "SBS", 3.799m, 3.802m);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(
            "/api/tipos-cambio/exportacion?desde=2026-08-14&hasta=2026-08-15&q=../..%0d%0aX:1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.DoesNotContain("X:1", disposition);
        Assert.DoesNotContain('\r', disposition);
        Assert.DoesNotContain('\n', disposition);
        Assert.Matches(new Regex(@"filename=tipos-cambio-\d{4}-\d{2}-\d{2}\.xlsx"), disposition);
    }
}
