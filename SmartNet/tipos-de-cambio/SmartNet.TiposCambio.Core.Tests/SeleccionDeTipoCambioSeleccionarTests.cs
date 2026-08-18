namespace SmartNet.TiposCambio.Core.Tests;

/// <summary>
/// spec.md capability "nucleo-dominio-tipos-cambio" / design.md Decision 1: SBS>MANUAL priority
/// lives in Core, not in the SELECT. In-memory candidate lists, no DB — pure function (tasks.md
/// 1.8). Threat Matrix's "red + credenciales" boundary is out of scope here (design.md).
/// </summary>
public class SeleccionDeTipoCambioSeleccionarTests
{
    private static readonly DateOnly Fecha = new(2026, 8, 14);
    private static readonly DateTime FechaConsulta = new(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);

    private static TipoCambio Fila(OrigenTipoCambio origen, DateOnly? fecha = null) =>
        new(fecha ?? Fecha, origen, 3.799m, 3.802m, FechaConsulta);

    [Fact]
    public void SbsWins_WhenBothOriginsPresentForTheSameFecha()
    {
        var sbs = Fila(OrigenTipoCambio.Sbs);
        var manual = Fila(OrigenTipoCambio.Manual);

        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, [sbs, manual]);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(OrigenTipoCambio.Sbs, vigente.Valor.Origen);
    }

    [Fact]
    public void ManualIsUsed_WhenSbsDidNotPublishForThatFecha()
    {
        var manual = Fila(OrigenTipoCambio.Manual);

        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, [manual]);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(OrigenTipoCambio.Manual, vigente.Valor.Origen);
    }

    [Fact]
    public void EmptyList_ReturnsSinTipoCambioForTheQueriedFecha()
    {
        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, []);

        var sinTipoCambio = Assert.IsType<ResultadoTipoCambio.SinTipoCambio>(resultado);
        Assert.Equal(Fecha, sinTipoCambio.Fecha);
    }

    [Fact]
    public void RowForADifferentFecha_IsDiscarded()
    {
        var otraFecha = Fila(OrigenTipoCambio.Sbs, fecha: new DateOnly(2026, 8, 13));

        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, [otraFecha]);

        var sinTipoCambio = Assert.IsType<ResultadoTipoCambio.SinTipoCambio>(resultado);
        Assert.Equal(Fecha, sinTipoCambio.Fecha);
    }

    [Fact]
    public void UnknownOrigenValue_IsDiscardedNeverSelected()
    {
        var origenDesconocido = Fila((OrigenTipoCambio)99);

        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, [origenDesconocido]);

        Assert.IsType<ResultadoTipoCambio.SinTipoCambio>(resultado);
    }

    [Fact]
    public void UnknownOrigenValue_DoesNotSuppressAValidManualRow()
    {
        var origenDesconocido = Fila((OrigenTipoCambio)99);
        var manual = Fila(OrigenTipoCambio.Manual);

        var resultado = SeleccionDeTipoCambio.Seleccionar(Fecha, [origenDesconocido, manual]);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(OrigenTipoCambio.Manual, vigente.Valor.Origen);
    }
}
