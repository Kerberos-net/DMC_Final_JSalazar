using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// api-facturas delta (BACKLOG #18 PR5, tasks.md 5.1) — <see cref="ValidacionDeCorreccion"/> is a
/// pure guard (ADR 0019: no DB / HTTP / clock) over the two fields PR5 makes PATCH-editable.
/// <c>null</c> on a field means "untouched" and is never rejected; only a present value is checked.
/// </summary>
public class ValidacionDeCorreccionTests
{
    [Fact]
    public void Validar_WhenNeitherFieldIsTouched_ReturnsNull()
    {
        Assert.Null(ValidacionDeCorreccion.Validar(new CorreccionFactura(RucProveedor: "20999999999")));
    }

    [Fact]
    public void Validar_WhenBothNewFieldsAreValid_ReturnsNull()
    {
        Assert.Null(ValidacionDeCorreccion.Validar(
            new CorreccionFactura(TipoComprobante: "03", Numero: "B001-123")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_WhenNumeroIsBlank_ReturnsCorreccionInvalida(string numero)
    {
        var resultado = ValidacionDeCorreccion.Validar(new CorreccionFactura(Numero: numero));

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
    }

    [Fact]
    public void Validar_WhenNumeroExceedsTwentyChars_ReturnsCorreccionInvalida()
    {
        var resultado = ValidacionDeCorreccion.Validar(new CorreccionFactura(Numero: new string('F', 21)));

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
    }

    [Fact]
    public void Validar_WhenNumeroIsExactlyTwentyChars_ReturnsNull()
    {
        Assert.Null(ValidacionDeCorreccion.Validar(new CorreccionFactura(Numero: new string('F', 20))));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("Factura")]
    public void Validar_WhenTipoComprobanteIsOutsideTheAcceptedSet_ReturnsCorreccionInvalida(string tipo)
    {
        var resultado = ValidacionDeCorreccion.Validar(new CorreccionFactura(TipoComprobante: tipo));

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("03")]
    [InlineData("07")]
    public void Validar_WhenTipoComprobanteIsInTheAcceptedSet_ReturnsNull(string tipo)
    {
        Assert.Null(ValidacionDeCorreccion.Validar(new CorreccionFactura(TipoComprobante: tipo)));
    }
}
