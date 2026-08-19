namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Task 3.2 -- <see cref="PayloadInboxParser"/> against `payload_inbox.py`'s exact JSON shape
/// (design.md Interfaces/Contracts, worker/src/smartnet_worker/payload_inbox.py). Pure -- no DB.
/// </summary>
public sealed class PayloadInboxParserTests
{
    private const string CompletoJson =
        """
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": 8, "tipoDocumento": "XML", "documentoAsociadoId": 9},
         "comprobante": {"tipoComprobante": "01", "numero": "F001-123", "rucProveedor": "20100000001",
                         "nombreProveedor": "Acme SAC", "monto": "1180.00", "moneda": "PEN",
                         "fechaEmision": "2026-08-10"},
         "evidencia": [{"campo": "total", "valor": "1180.00", "fuente": "XML"}],
         "afectacionMixta": false, "camposNoExtraidos": ["igv"], "advertenciasAsociacion": ["SIN_PAREJA"]}
        """;

    [Fact]
    public void Parse_ReturnsEventoInbox_WithEveryFieldFromTheJsonExample()
    {
        var evento = PayloadInboxParser.Parse(CompletoJson);

        Assert.Equal(1, evento.Version);
        Assert.Equal("COMPLETADO", evento.EstadoProcesamiento);
        Assert.Equal(8, evento.DocumentoRecibidoId);
        Assert.Equal("XML", evento.TipoDocumento);
        Assert.Equal(9, evento.DocumentoAsociadoId);
        Assert.NotNull(evento.Comprobante);
        Assert.Equal("01", evento.Comprobante!.TipoComprobante);
        Assert.Equal("F001-123", evento.Comprobante.Numero);
        Assert.Equal("20100000001", evento.Comprobante.RucProveedor);
        Assert.Equal("Acme SAC", evento.Comprobante.NombreProveedor);
        Assert.Equal(1180.00m, evento.Comprobante.Monto);
        Assert.Equal("PEN", evento.Comprobante.Moneda);
        Assert.Equal(new DateOnly(2026, 8, 10), evento.Comprobante.FechaEmision);
        Assert.Single(evento.Evidencia);
        Assert.Equal("total", evento.Evidencia[0].Campo);
        Assert.Equal("1180.00", evento.Evidencia[0].Valor);
        Assert.Equal("XML", evento.Evidencia[0].Fuente);
        Assert.False(evento.AfectacionMixta);
        Assert.Equal(new[] { "igv" }, evento.CamposNoExtraidos);
        Assert.Equal(new[] { "SIN_PAREJA" }, evento.AdvertenciasAsociacion);
    }

    [Fact]
    public void Parse_ReturnsNullComprobante_ForAFailedProcessingPayload()
    {
        const string erroJson =
            """
            {"version": 1, "estadoProcesamiento": "ERROR",
             "documento": {"documentoRecibidoId": 3, "tipoDocumento": "PDF", "documentoAsociadoId": null},
             "comprobante": null,
             "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": ["SIN_PAREJA"]}
            """;

        var evento = PayloadInboxParser.Parse(erroJson);

        Assert.Equal("ERROR", evento.EstadoProcesamiento);
        Assert.Null(evento.Comprobante);
        Assert.Null(evento.DocumentoAsociadoId);
        Assert.Empty(evento.Evidencia);
        Assert.Null(evento.AfectacionMixta);
    }

    [Fact]
    public void Parse_TreatsMontoAsAStringInJson_NeverALossyJsonNumber()
    {
        const string json =
            """
            {"version": 1, "estadoProcesamiento": "COMPLETADO",
             "documento": {"documentoRecibidoId": 1, "tipoDocumento": "XML", "documentoAsociadoId": null},
             "comprobante": {"tipoComprobante": "01", "numero": null, "rucProveedor": null,
                             "nombreProveedor": null, "monto": "0.10", "moneda": "PEN", "fechaEmision": "2026-08-16"},
             "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": ["SIN_PAREJA"]}
            """;

        var evento = PayloadInboxParser.Parse(json);

        Assert.Equal(0.10m, evento.Comprobante!.Monto);
    }
}
