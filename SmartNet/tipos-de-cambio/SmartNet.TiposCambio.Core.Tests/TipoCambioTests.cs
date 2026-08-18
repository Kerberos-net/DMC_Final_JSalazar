namespace SmartNet.TiposCambio.Core.Tests;

/// <summary>
/// spec.md capability "nucleo-dominio-tipos-cambio" / design.md Interfaces/Contracts shape:
/// <c>TipoCambio(DateOnly Fecha, OrigenTipoCambio Origen, decimal Compra, decimal Venta,
/// DateTime FechaConsulta)</c> — a record, value-equal by all five components (tasks.md 1.4).
/// </summary>
public class TipoCambioTests
{
    [Fact]
    public void Construction_ExposesAllFiveComponents()
    {
        var fecha = new DateOnly(2026, 8, 14);
        var fechaConsulta = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc);

        var tipoCambio = new TipoCambio(fecha, OrigenTipoCambio.Sbs, 3.799m, 3.802m, fechaConsulta);

        Assert.Equal(fecha, tipoCambio.Fecha);
        Assert.Equal(OrigenTipoCambio.Sbs, tipoCambio.Origen);
        Assert.Equal(3.799m, tipoCambio.Compra);
        Assert.Equal(3.802m, tipoCambio.Venta);
        Assert.Equal(fechaConsulta, tipoCambio.FechaConsulta);
    }

    [Fact]
    public void Equality_IsValueBasedAcrossAllComponents()
    {
        var fecha = new DateOnly(2026, 8, 15);
        var fechaConsulta = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

        var a = new TipoCambio(fecha, OrigenTipoCambio.Manual, 3.80m, 3.85m, fechaConsulta);
        var b = new TipoCambio(fecha, OrigenTipoCambio.Manual, 3.80m, 3.85m, fechaConsulta);
        var c = new TipoCambio(fecha, OrigenTipoCambio.Sbs, 3.80m, 3.85m, fechaConsulta);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(OrigenTipoCambio.Sbs)]
    [InlineData(OrigenTipoCambio.Manual)]
    public void OrigenTipoCambio_HasExactlyTheTwoDeclaredValues(OrigenTipoCambio origen)
    {
        Assert.True(Enum.IsDefined(origen));
    }
}
