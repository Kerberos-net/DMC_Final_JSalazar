using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// api-facturas delta (BACKLOG #18 PR5) + BACKLOG #19 (design D1/D2, tasks 3.4) —
/// <see cref="ValidacionDeCorreccion"/> is a pure guard (ADR 0019: no DB / HTTP / clock) over the
/// PATCH-editable fields. <c>null</c> on a field means "untouched" and is never rejected; guards
/// evaluate the MERGED value (loaded record + correction).
/// </summary>
public class ValidacionDeCorreccionTests
{
    private static FacturaPersistida Original(
        string estado = FacturaPersistida.PendienteValidacion,
        string tipoComprobante = "01",
        string? afectacion = "GRAVADA",
        decimal totalOrig = 1180.00m) => new(
        FacturaId: 1,
        Estado: estado,
        ProveedorCodigo: "P00001",
        RucProveedor: "20999999999",
        TipoComprobante: tipoComprobante,
        Numero: "F001-1",
        TotalOrig: totalOrig,
        Moneda: "PEN",
        FechaEmision: new DateOnly(2026, 8, 10),
        Motivo: 22,
        Afectacion: afectacion,
        Version: new byte[] { 1 });

    private static ResultadoComando? Validar(CorreccionFactura cambios, FacturaPersistida? original = null) =>
        ValidacionDeCorreccion.Validar(original ?? Original(), cambios);

    [Fact]
    public void Validar_WhenNeitherFieldIsTouched_ReturnsNull()
    {
        Assert.Null(Validar(new CorreccionFactura(RucProveedor: "20999999999")));
    }

    [Fact]
    public void Validar_WhenBothNewFieldsAreValid_ReturnsNull()
    {
        Assert.Null(Validar(new CorreccionFactura(TipoComprobante: "03", Numero: "B001-123")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_WhenNumeroIsBlank_ReturnsCorreccionInvalida(string numero)
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(Validar(new CorreccionFactura(Numero: numero)));
    }

    [Fact]
    public void Validar_WhenNumeroExceedsTwentyChars_ReturnsCorreccionInvalida()
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(Validar(new CorreccionFactura(Numero: new string('F', 21))));
    }

    [Fact]
    public void Validar_WhenNumeroIsExactlyTwentyChars_ReturnsNull()
    {
        Assert.Null(Validar(new CorreccionFactura(Numero: new string('F', 20))));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("Factura")]
    public void Validar_WhenTipoComprobanteIsOutsideTheAcceptedSet_ReturnsCorreccionInvalida(string tipo)
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(Validar(new CorreccionFactura(TipoComprobante: tipo)));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("03")]
    [InlineData("07")]
    public void Validar_WhenTipoComprobanteIsInTheAcceptedSet_ReturnsNull(string tipo)
    {
        Assert.Null(Validar(new CorreccionFactura(TipoComprobante: tipo)));
    }

    // ---- BACKLOG #19 (design D1) — atomic base/IGV pair ------------------------------------------

    [Fact]
    public void Validar_WhenBasePairIsComplete_ReturnsNull()
    {
        Assert.Null(Validar(new CorreccionFactura(BaseImponible: 1000.00m, Igv: 180.00m)));
    }

    [Theory]
    [InlineData(1000.00, null)]
    [InlineData(null, 180.00)]
    public void Validar_WhenOnlyOneHalfOfThePairIsSent_ReturnsCorreccionInvalida(double? baseImp, double? igv)
    {
        var cambios = new CorreccionFactura(
            BaseImponible: baseImp is null ? null : (decimal)baseImp.Value,
            Igv: igv is null ? null : (decimal)igv.Value);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(Validar(cambios));
    }

    [Fact]
    public void Validar_WhenPairIsSentWithTotalOrig_ReturnsCorreccionInvalida()
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: 1000m, Igv: 180m, TotalOrig: 1180m)));
    }

    [Fact]
    public void Validar_WhenBaseImponibleIsNegative_ReturnsCorreccionInvalida()
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: -1m, Igv: 180m)));
    }

    [Fact]
    public void Validar_WhenIgvIsNegative_ReturnsCorreccionInvalida()
    {
        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: 1000m, Igv: -1m)));
    }

    // ---- BACKLOG #19 (design D2) — state gate --------------------------------------------------

    [Theory]
    [InlineData(FacturaPersistida.Validada)]
    [InlineData(FacturaPersistida.Descartada)]
    public void Validar_WhenContableFieldEditedOnNonPendingFactura_ReturnsCorreccionInvalida(string estado)
    {
        var original = Original(estado: estado);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(Glosa: "nota"), original));
        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: 1000m, Igv: 180m), original));
    }

    [Fact]
    public void Validar_WhenNumeroEditedOnValidatedFactura_ReturnsNull()
    {
        // numero/tipo keep the audited-correction post-validation behavior (not gated).
        Assert.Null(Validar(new CorreccionFactura(Numero: "F001-2"), Original(estado: FacturaPersistida.Validada)));
    }

    [Fact]
    public void Validar_WhenGlosaEditedOnPendingFactura_ReturnsNull()
    {
        Assert.Null(Validar(new CorreccionFactura(Glosa: "detalle de la compra")));
    }

    // ---- BACKLOG #19 (design D1 owner-decisions a/b) — non-zero IGV guard ---------------------

    [Fact]
    public void Validar_WhenBoletaCarriesNonZeroIgv_ReturnsCorreccionInvalida()
    {
        var original = Original(tipoComprobante: "03", afectacion: "GRAVADA");

        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: 1000m, Igv: 180m), original));
    }

    [Theory]
    [InlineData("EXONERADA")]
    [InlineData("INAFECTA")]
    public void Validar_WhenNonNcNoGravadaCarriesNonZeroIgv_ReturnsCorreccionInvalida(string afectacion)
    {
        var original = Original(tipoComprobante: "01", afectacion: afectacion);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(
            Validar(new CorreccionFactura(BaseImponible: 1000m, Igv: 180m), original));
    }

    [Fact]
    public void Validar_WhenBoletaCarriesZeroIgv_ReturnsNull()
    {
        var original = Original(tipoComprobante: "03");

        Assert.Null(Validar(new CorreccionFactura(BaseImponible: 1180m, Igv: 0m), original));
    }

    [Fact]
    public void Validar_WhenNotaCreditoConReferenciaInternaCarriesNonZeroIgv_ReturnsNull()
    {
        // owner-decision (b): NC 07 follows §6 TC-inheritance; the non-zero IGV guard does NOT fire.
        var original = Original(tipoComprobante: "07", afectacion: "GRAVADA");

        Assert.Null(Validar(new CorreccionFactura(BaseImponible: 200m, Igv: 36m), original));
    }

    [Fact]
    public void Validar_WhenNoGravadaButRetaggedAsNotaCreditoInSamePatch_ReturnsNull()
    {
        // merged tipoComprobante = "07" wins over the loaded "01".
        var original = Original(tipoComprobante: "01", afectacion: "INAFECTA");

        Assert.Null(Validar(
            new CorreccionFactura(TipoComprobante: "07", BaseImponible: 200m, Igv: 36m), original));
    }
}
