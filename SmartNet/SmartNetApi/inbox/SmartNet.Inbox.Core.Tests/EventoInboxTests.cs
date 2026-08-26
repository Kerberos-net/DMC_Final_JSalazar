namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// spec.md "Pure promotion decision" / design.md Interfaces/Contracts JSON example — record shape
/// for the parsed <c>InboxEvent.Payload</c> (parsing itself happens in Infrastructure, D9; Core
/// only holds the already-parsed value).
/// </summary>
public class EventoInboxTests
{
    [Fact]
    public void EvidenciaCampo_HoldsCampoValorFuente()
    {
        var evidencia = new EvidenciaCampo("total", "1180.00", "XML");

        Assert.Equal("total", evidencia.Campo);
        Assert.Equal("1180.00", evidencia.Valor);
        Assert.Equal("XML", evidencia.Fuente);
    }

    [Fact]
    public void ComprobanteExtraido_AllowsEveryFieldNull_ExceptWhenSupplied()
    {
        var vacio = new ComprobanteExtraido(null, null, null, null, null, null, null);
        var completo = new ComprobanteExtraido(
            TipoComprobante: "01",
            Numero: "F001-123",
            RucProveedor: "20100000001",
            NombreProveedor: "Acme SAC",
            Monto: 1180.00m,
            Moneda: "PEN",
            FechaEmision: new DateOnly(2026, 8, 10));

        Assert.Null(vacio.TipoComprobante);
        Assert.Equal("01", completo.TipoComprobante);
        Assert.Equal(1180.00m, completo.Monto);
        Assert.Equal(new DateOnly(2026, 8, 10), completo.FechaEmision);
    }

    [Fact]
    public void EventoInbox_HoldsDocumentoComprobanteEvidenciaAndListas()
    {
        var comprobante = new ComprobanteExtraido("01", "F001-123", "20100000001", "Acme SAC",
            1180.00m, "PEN", new DateOnly(2026, 8, 10));
        var evidencia = new[] { new EvidenciaCampo("total", "1180.00", "XML") };

        var evento = new EventoInbox(
            Version: 1,
            EstadoProcesamiento: "COMPLETADO",
            DocumentoRecibidoId: 8,
            TipoDocumento: "XML",
            DocumentoAsociadoId: 9,
            NombreArchivo: "factura.xml",
            MimeType: "application/xml",
            RutaRelativa: "2026/08/factura.xml",
            TamanoBytes: 2048,
            Comprobante: comprobante,
            Evidencia: evidencia,
            AfectacionMixta: false,
            CamposNoExtraidos: new[] { "igv" },
            AdvertenciasAsociacion: Array.Empty<string>());

        Assert.Equal(1, evento.Version);
        Assert.Equal("COMPLETADO", evento.EstadoProcesamiento);
        Assert.Equal(8, evento.DocumentoRecibidoId);
        Assert.Equal("factura.xml", evento.NombreArchivo);
        Assert.Equal("application/xml", evento.MimeType);
        Assert.Equal("2026/08/factura.xml", evento.RutaRelativa);
        Assert.Equal(2048, evento.TamanoBytes);
        Assert.Same(comprobante, evento.Comprobante);
        Assert.Single(evento.Evidencia);
        Assert.False(evento.AfectacionMixta);
        Assert.Equal(new[] { "igv" }, evento.CamposNoExtraidos);
        Assert.Empty(evento.AdvertenciasAsociacion);
    }

    [Fact]
    public void EventoInbox_AllowsNullComprobante_ForFailedProcesamiento()
    {
        var evento = new EventoInbox(
            Version: 1,
            EstadoProcesamiento: "ERROR",
            DocumentoRecibidoId: 8,
            TipoDocumento: "PDF",
            DocumentoAsociadoId: null,
            NombreArchivo: "factura.pdf",
            MimeType: "application/pdf",
            RutaRelativa: "2026/08/factura.pdf",
            TamanoBytes: 4096,
            Comprobante: null,
            Evidencia: Array.Empty<EvidenciaCampo>(),
            AfectacionMixta: null,
            CamposNoExtraidos: Array.Empty<string>(),
            AdvertenciasAsociacion: new[] { "SIN_PAREJA" });

        Assert.Null(evento.Comprobante);
        Assert.Null(evento.AfectacionMixta);
        Assert.Single(evento.AdvertenciasAsociacion);
    }
}
