using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 2.5 — pure unit test (no DB, no HTTP round-trip): every <see cref="InvarianteContable"/>
/// value maps to a status code through <c>ProblemasDeNegocio.Map</c>, and the enum-coverage switch
/// fails LOUDLY (an unhandled exception, not a silent default) the day someone adds an eighth
/// <see cref="InvarianteContable"/> value without updating the map (design D3).
/// </summary>
public class ProblemasDeNegocioInvarianteCoverageTests
{
    public static IEnumerable<object[]> TodosLosValores() =>
        Enum.GetValues<InvarianteContable>().Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(TodosLosValores))]
    public async Task Map_HandlesEveryInvarianteContableValue_WithoutThrowing(InvarianteContable invariante)
    {
        var fallo = new InvarianteIncumplida(invariante, ImporteEsperado: 100m, ImporteReal: 90m, Detalle: "detalle de prueba");
        var resultado = new ResultadoComando.InvariantesIncumplidas(new[] { fallo });

        var httpResult = ProblemasDeNegocio.Map(resultado);
        var status = await EjecutarYObtenerStatusAsync(httpResult);

        // D3: Global 3 (FechaAnteriorAlCorte) y Global 4 (ProveedorVarios) -> 409; el resto -> 422.
        var estadoEsperado = invariante is InvarianteContable.FechaAnteriorAlCorte or InvarianteContable.ProveedorVarios
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status422UnprocessableEntity;
        Assert.Equal(estadoEsperado, status);
    }

    [Fact]
    public void Map_WithAplicado_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => ProblemasDeNegocio.Map(new ResultadoComando.Aplicado()));
    }

    [Fact]
    public async Task Map_WithTwoOrMoreFallos_Returns422_WithAnAsientoInvalidoType()
    {
        var fallos = new[]
        {
            new InvarianteIncumplida(InvarianteContable.SumaDebeIgualHaber, 100m, 80m, "no cuadra"),
            new InvarianteIncumplida(InvarianteContable.LineaSinCuenta, null, null, "sin cuenta"),
        };
        var resultado = new ResultadoComando.InvariantesIncumplidas(fallos);

        var httpResult = ProblemasDeNegocio.Map(resultado);
        var status = await EjecutarYObtenerStatusAsync(httpResult);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
    }

    private static async Task<int> EjecutarYObtenerStatusAsync(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }
}
