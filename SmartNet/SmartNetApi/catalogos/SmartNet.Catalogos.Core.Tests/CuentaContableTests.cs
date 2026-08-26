namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// spec.md capability "resolucion-de-prefijos" / design.md Interfaces-Contracts:
/// <c>CuentaContable(Cuenta, Descripcion, Nivel, CtaReflejaCodigo, CtaPuenteCodigo)</c> with
/// <c>EsHojaImputable => Nivel is null</c> (REGLAS.md §2: "Solo las de 6 dígitos son imputables,
/// nivel viene vacío"). tasks.md 1.4/1.5.
/// </summary>
public class CuentaContableTests
{
    [Fact]
    public void Construction_ExposesAllFiveFieldsByPosition()
    {
        var cuenta = new CuentaContable("631111", "FLETE TRASLADO DE MERCADERIA", null, "946311", "791111");

        Assert.Equal("631111", cuenta.Cuenta);
        Assert.Equal("FLETE TRASLADO DE MERCADERIA", cuenta.Descripcion);
        Assert.Null(cuenta.Nivel);
        Assert.Equal("946311", cuenta.CtaReflejaCodigo);
        Assert.Equal("791111", cuenta.CtaPuenteCodigo);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        var a = new CuentaContable("631111", "FLETE", null, null, null);
        var b = new CuentaContable("631111", "FLETE", null, null, null);
        var c = new CuentaContable("631112", "FLETE", null, null, null);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void EsHojaImputable_IsTrueWhenNivelIsNull()
    {
        var hoja = new CuentaContable("631111", "FLETE", null, null, null);

        Assert.True(hoja.EsHojaImputable);
    }

    [Fact]
    public void EsHojaImputable_IsFalseWhenNivelIsPopulated()
    {
        var nodo = new CuentaContable("01", "BIENES Y VALORES ENTREGADOS", 2, null, null);

        Assert.False(nodo.EsHojaImputable);
    }
}
