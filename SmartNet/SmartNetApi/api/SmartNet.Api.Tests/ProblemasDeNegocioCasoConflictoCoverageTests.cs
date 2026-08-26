using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 1.5/1.6 — pure unit test (no DB, no HTTP round-trip), same pattern as
/// <see cref="ProblemasDeNegocioInvarianteCoverageTests"/> but for <see cref="CasoConflicto"/>: every
/// value maps to 409 through <c>ProblemasDeNegocio.Map</c>, and the switch fails LOUDLY if a case is
/// unhandled. Specifically asserts OQ5/ADR 0020 decisión 5:
/// <see cref="CasoConflicto.FacturaDescartada"/> -&gt; <c>.../factura-descartada</c>.
/// </summary>
public class ProblemasDeNegocioCasoConflictoCoverageTests
{
    public static IEnumerable<object[]> TodosLosValores() =>
        Enum.GetValues<CasoConflicto>().Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(TodosLosValores))]
    public async Task Map_HandlesEveryCasoConflictoValue_With409(CasoConflicto caso)
    {
        var resultado = new ResultadoComando.Conflicto(caso, "detalle de prueba");

        var httpResult = ProblemasDeNegocio.Map(resultado);
        var (status, _) = await EjecutarYObtenerRespuestaAsync(httpResult);

        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task Map_FacturaDescartada_UsaElTipoFacturaDescartada()
    {
        var resultado = new ResultadoComando.Conflicto(
            CasoConflicto.FacturaDescartada, "La factura fue descartada; no puede validarse.");

        var httpResult = ProblemasDeNegocio.Map(resultado);
        var (status, body) = await EjecutarYObtenerRespuestaAsync(httpResult);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        using var documento = JsonDocument.Parse(body);
        Assert.EndsWith("factura-descartada", documento.RootElement.GetProperty("type").GetString());
        Assert.Equal("Factura descartada", documento.RootElement.GetProperty("title").GetString());
    }

    private static async Task<(int Status, string Body)> EjecutarYObtenerRespuestaAsync(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
