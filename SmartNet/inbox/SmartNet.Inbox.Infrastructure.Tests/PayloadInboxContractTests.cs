namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// ADR 0019 level-2 contract test (tasks 4.4-4.6): <see cref="PayloadInboxParser"/> against the
/// SAME fixture `SmartNet/worker/tests/fixtures/inbox_event_payload.golden.json` that
/// `test_payload_inbox_contract.py` asserts `payload_inbox.construir_payload` produces byte-for-
/// byte. Proves .NET reads exactly what Python writes -- not two independently self-asserted
/// shapes (unlike <see cref="PayloadInboxParserTests"/>'s inline literals, task 3.2).
/// </summary>
public sealed class PayloadInboxContractTests
{
    [Fact]
    public void Parse_ReturnsEventoInbox_MatchingTheSharedGoldenFixtureExactly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "inbox_event_payload.golden.json");
        var json = File.ReadAllText(path);

        var evento = PayloadInboxParser.Parse(json);

        Assert.Equal(1, evento.Version);
        Assert.Equal("COMPLETADO", evento.EstadoProcesamiento);
        Assert.Equal(8, evento.DocumentoRecibidoId);
        Assert.Equal("XML", evento.TipoDocumento);
        Assert.Equal(9, evento.DocumentoAsociadoId);
        Assert.Equal("factura.xml", evento.NombreArchivo);
        Assert.Equal("application/xml", evento.MimeType);
        Assert.Equal("2026/08/factura.xml", evento.RutaRelativa);
        Assert.Equal(2048, evento.TamanoBytes);

        Assert.NotNull(evento.Comprobante);
        Assert.Equal("01", evento.Comprobante!.TipoComprobante);
        Assert.Equal("F001-123", evento.Comprobante.Numero);
        Assert.Equal("20100000001", evento.Comprobante.RucProveedor);
        Assert.Equal("Acme SAC", evento.Comprobante.NombreProveedor);
        Assert.Equal(1180.00m, evento.Comprobante.Monto);
        Assert.Equal("PEN", evento.Comprobante.Moneda);
        Assert.Equal(new DateOnly(2026, 8, 10), evento.Comprobante.FechaEmision);

        Assert.Equal(
            new[] { "tipoComprobante", "numero", "ruc", "nombreProveedor", "total", "moneda", "fechaEmision" },
            evento.Evidencia.Select(e => e.Campo));
        Assert.All(evento.Evidencia, e => Assert.Equal("XML", e.Fuente));

        Assert.False(evento.AfectacionMixta);
        Assert.Empty(evento.CamposNoExtraidos);
        Assert.Empty(evento.AdvertenciasAsociacion);
    }
}
