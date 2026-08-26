namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// spec.md "Sufficient data promotes to Factura" (6 indicator flags per spec.md's older prose) —
/// design D5 (confirmed, Open Questions): only 5 are actually computed here
/// (EsProveedorGenerico, PosibleDuplicado, TieneCamposNoExtraidos, FechaEnDomingo,
/// AfectacionMixta 3-state); EsReferenciaExterna is never touched — it keeps
/// fact.Factura's own DDL default 0/false, because DatosExtraidos (#6) has no reference-nota
/// columns to derive it from.
/// </summary>
public class CalculoDeIndicadoresTests
{
    private static EventoInbox EventoCon(
        DateOnly? fechaEmision, bool? afectacionMixta, IReadOnlyList<string>? camposNoExtraidos = null) =>
        new(
            1, "COMPLETADO", 8, "XML", 9, "factura.xml", "application/xml", "2026/08/factura.xml", 2048,
            new ComprobanteExtraido("01", "F001-123", "20100000001", "Acme SAC", 1180.00m, "PEN", fechaEmision),
            Array.Empty<EvidenciaCampo>(),
            afectacionMixta,
            camposNoExtraidos ?? Array.Empty<string>(),
            Array.Empty<string>());

    [Fact]
    public void Calcular_EsProveedorGenerico_ReflejaProveedorNoResuelto()
    {
        var evento = EventoCon(new DateOnly(2026, 8, 10), false);

        var noResuelto = CalculoDeIndicadores.Calcular(evento, proveedorResuelto: false, existeIdentidadPrevia: false);
        var resuelto = CalculoDeIndicadores.Calcular(evento, proveedorResuelto: true, existeIdentidadPrevia: false);

        Assert.True(noResuelto.EsProveedorGenerico);
        Assert.False(resuelto.EsProveedorGenerico);
    }

    [Fact]
    public void Calcular_PosibleDuplicado_ReflejaIdentidadPrevia()
    {
        var evento = EventoCon(new DateOnly(2026, 8, 10), false);

        var duplicado = CalculoDeIndicadores.Calcular(evento, proveedorResuelto: true, existeIdentidadPrevia: true);

        Assert.True(duplicado.PosibleDuplicado);
    }

    [Fact]
    public void Calcular_TieneCamposNoExtraidos_ReflejaListaDelPayload()
    {
        var conFaltantes = EventoCon(new DateOnly(2026, 8, 10), false, new[] { "igv" });
        var sinFaltantes = EventoCon(new DateOnly(2026, 8, 10), false, Array.Empty<string>());

        Assert.True(CalculoDeIndicadores.Calcular(conFaltantes, true, false).TieneCamposNoExtraidos);
        Assert.False(CalculoDeIndicadores.Calcular(sinFaltantes, true, false).TieneCamposNoExtraidos);
    }

    [Theory]
    [InlineData(2026, 8, 9, true)]  // domingo
    [InlineData(2026, 8, 10, false)] // lunes
    public void Calcular_FechaEnDomingo_DerivaSoloDeFechaEmision_NuncaDelReloj(int y, int m, int d, bool esperado)
    {
        var evento = EventoCon(new DateOnly(y, m, d), false);

        var indicadores = CalculoDeIndicadores.Calcular(evento, true, false);

        Assert.Equal(esperado, indicadores.FechaEnDomingo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Calcular_AfectacionMixta_EsPaseDirectoDeTresEstados(bool? valor)
    {
        var evento = EventoCon(new DateOnly(2026, 8, 10), valor);

        var indicadores = CalculoDeIndicadores.Calcular(evento, true, false);

        Assert.Equal(valor, indicadores.AfectacionMixta);
    }
}
