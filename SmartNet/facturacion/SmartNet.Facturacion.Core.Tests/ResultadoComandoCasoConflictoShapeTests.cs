using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.3/1.4 — design D4: "CasoConflicto enum in Core, one value per ADR 0008 row
/// (duplicado, domingo, sin tipo de cambio, P00000, fecha de corte, NC con referencia interna
/// irresoluble, asiento ya confirmado, afectacion mixta, afectacion no verificada)" -- nine cases,
/// no more, no fewer. And design's <c>ResultadoComando</c> shape (Interfaces/Contracts,
/// <see cref="ResultadoEscritura"/> plus the Core-level union every Servicio* command returns).
/// </summary>
public class ResultadoComandoCasoConflictoShapeTests
{
    [Fact]
    public void CasoConflicto_HasExactlyTheNineAdr0008Rows()
    {
        var valores = Enum.GetNames<CasoConflicto>();

        Assert.Equal(9, valores.Length);
        Assert.Contains(nameof(CasoConflicto.DuplicadoNoResuelto), valores);
        Assert.Contains(nameof(CasoConflicto.ComprobanteEmitidoDomingo), valores);
        Assert.Contains(nameof(CasoConflicto.SinTipoCambio), valores);
        Assert.Contains(nameof(CasoConflicto.ProveedorGenericoNoResuelto), valores);
        Assert.Contains(nameof(CasoConflicto.FechaAnteriorAlCorte), valores);
        Assert.Contains(nameof(CasoConflicto.NotaCreditoReferenciaIrresoluble), valores);
        Assert.Contains(nameof(CasoConflicto.AsientoYaConfirmado), valores);
        Assert.Contains(nameof(CasoConflicto.AfectacionMixta), valores);
        Assert.Contains(nameof(CasoConflicto.AfectacionNoVerificada), valores);
    }

    [Fact]
    public void ResultadoComando_Aplicado_IsAResultadoComando()
    {
        ResultadoComando resultado = new ResultadoComando.Aplicado();

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
    }

    [Fact]
    public void ResultadoComando_VersionEnConflicto_IsAResultadoComando()
    {
        ResultadoComando resultado = new ResultadoComando.VersionEnConflicto();

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
    }

    [Fact]
    public void ResultadoComando_NoEncontrado_IsAResultadoComando()
    {
        ResultadoComando resultado = new ResultadoComando.NoEncontrado();

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
    }

    [Fact]
    public void ResultadoComando_Conflicto_CarriesTheCasoConflictoAndADetail()
    {
        ResultadoComando resultado = new ResultadoComando.Conflicto(
            CasoConflicto.ComprobanteEmitidoDomingo, "El comprobante fue emitido un domingo.");

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.ComprobanteEmitidoDomingo, conflicto.Caso);
        Assert.Equal("El comprobante fue emitido un domingo.", conflicto.Detalle);
    }

    [Fact]
    public void ResultadoComando_InvariantesIncumplidas_CarriesAllFailures_NotJustTheFirst()
    {
        var fallos = new List<InvarianteIncumplida>
        {
            new(InvarianteContable.SumaDebeIgualHaber, 100m, 90m, "descuadre"),
            new(InvarianteContable.LineaSinCuenta, null, null, "sin cuenta"),
        };

        ResultadoComando resultado = new ResultadoComando.InvariantesIncumplidas(fallos);

        var incumplidas = Assert.IsType<ResultadoComando.InvariantesIncumplidas>(resultado);
        Assert.Equal(2, incumplidas.Fallos.Count);
    }
}
