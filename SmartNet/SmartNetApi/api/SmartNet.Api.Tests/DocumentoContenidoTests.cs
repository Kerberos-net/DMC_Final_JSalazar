using SmartNet.Api;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 3.1/3.2 (RED) — design.md Threat Matrix, pure/no-I/O per the Testing Strategy table
/// ("Unit | Path containment, MIME allow-list | xUnit / Vitest, no I/O"): <see cref="DocumentoContenido"/>
/// never touches the filesystem, so these run without a database or a real storage root.
/// </summary>
public sealed class DocumentoContenidoTests
{
    // --- Path traversal (threat matrix row 1) ---

    [Fact]
    public void ResolverRutaSegura_WithADotDotSegment_ReturnsNull()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "smartnet-docs-root");

        var resultado = DocumentoContenido.ResolverRutaSegura(raiz, "../../secretos/win.ini");

        Assert.Null(resultado);
    }

    [Fact]
    public void ResolverRutaSegura_WithAnAbsoluteEscapePath_ReturnsNull()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "smartnet-docs-root");

        var resultado = DocumentoContenido.ResolverRutaSegura(raiz, "..\\..\\Windows\\win.ini");

        Assert.Null(resultado);
    }

    [Fact]
    public void ResolverRutaSegura_WithAPlainRelativePath_ReturnsAPathUnderTheRoot()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "smartnet-docs-root");

        var resultado = DocumentoContenido.ResolverRutaSegura(raiz, "2026/08/factura-1.pdf");

        Assert.NotNull(resultado);
        Assert.StartsWith(Path.GetFullPath(raiz), resultado);
    }

    // --- MIME confusion / stored XSS (threat matrix row 2) ---

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void ContentTypeFor_AnAllowListedMime_IsEchoedVerbatim(string mime)
    {
        Assert.Equal(mime, DocumentoContenido.ContentTypeFor(mime));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/javascript")]
    [InlineData("")]
    public void ContentTypeFor_ANonAllowListedMime_FallsBackToOctetStream(string mime)
    {
        Assert.Equal("application/octet-stream", DocumentoContenido.ContentTypeFor(mime));
    }
}
