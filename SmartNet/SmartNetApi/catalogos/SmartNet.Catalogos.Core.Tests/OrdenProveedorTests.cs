namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// BACKLOG #22 PR5 — design D7: <c>OrdenProveedor</c> is the pure whitelist the proveedores
/// catalogo-mode endpoint validates the <c>orden</c> parameter against, same shape as
/// <c>EstadoDerivadoBandeja</c>. The SQL adapter maps a valid key to a compile-time constant
/// column; user text never reaches the query as an identifier.
/// </summary>
public class OrdenProveedorTests
{
    [Fact]
    public void Valores_AreExactlyProveedorRucCodigo()
    {
        Assert.Equal(
            new[] { "codigo", "proveedor", "ruc" },
            OrdenProveedor.Valores.OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("proveedor")]
    [InlineData("ruc")]
    [InlineData("codigo")]
    public void EsValido_IsTrue_ForEachWhitelistedKey(string clave) =>
        Assert.True(OrdenProveedor.EsValido(clave));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PROVEEDOR")]
    [InlineData("nombre")]
    [InlineData("codpro; DROP TABLE dbo.Proveedor")]
    public void EsValido_IsFalse_ForAnythingElse(string? clave) =>
        Assert.False(OrdenProveedor.EsValido(clave));
}
