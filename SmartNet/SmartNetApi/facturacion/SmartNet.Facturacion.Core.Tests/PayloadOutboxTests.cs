using System.Text.Json.Nodes;
using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.1 — golden-fixture tests for the 5 event payloads <see cref="PayloadOutbox"/> produces
/// (design.md D2/D9: FACTURA_VALIDADA and DOCUMENTACION_ACTUALIZADA are RETROFITTED onto this same
/// envelope). Exercises ONLY <c>ConstruirAsync</c>/<c>Serializar</c> against a
/// <see cref="FakeUnidadDeTrabajo"/> — no DB (ADR 0019). Every JSON literal below is transcribed from
/// design.md's Interfaces/Contracts envelope, field by field, not from the implementation.
/// </summary>
public sealed class PayloadOutboxTests
{
    private static FakeUnidadDeTrabajo NuevaUnidadConAsientoYFactura()
    {
        var uow = new FakeUnidadDeTrabajo
        {
            FacturaACargar = new FacturaPersistida(
                FacturaId: 100, Estado: FacturaPersistida.Validada, ProveedorCodigo: "P00234",
                RucProveedor: "20100000001", TipoComprobante: "01", Numero: "F001-123",
                TotalOrig: 118.00m, Moneda: "PEN", FechaEmision: new DateOnly(2026, 8, 10),
                Motivo: null, Afectacion: "GRAVADA", Version: new byte[] { 1 },
                EsProveedorGenerico: false, PosibleDuplicado: false, TieneCamposNoExtraidos: false,
                AfectacionMixta: false),
            AsientoVigenteId = 5,
            AsientoACargar = new AsientoPersistido(
                AsientoContableId: 5, FacturaId: 100, Estado: AsientoPersistido.Confirmado,
                NumeroAsiento: "02-2026-08-000007", Version: new byte[] { 2 },
                Asiento: new AsientoContable(
                    ProveedorCodigo: "P00234", FechaContable: new DateOnly(2026, 8, 10),
                    MotivoDescripcion: null, TipoCambioVenta: null,
                    BasePEN: 100.00m, IgvPEN: 18.00m, NetoPEN: 118.00m,
                    AfectacionCongelada: Afectacion.Gravada, Comprobante: TipoComprobante.Factura,
                    Lineas: Array.Empty<LineaAsiento>()),
                Hechos: HechosDeConflicto.Ninguno),
            LineasACargar = new[]
            {
                new LineaPersistida(30, new LineaAsiento(
                    Orden: 1, Bloque: Bloque.Principal, Tipo: TipoLinea.D,
                    Debe: 118.00m, Haber: 0m, CuentaCodigo: "601111",
                    CuentaDescripcion: null, CtaReflejaCodigo: null, CtaPuenteCodigo: null)),
            },
            DocumentosFacturaACargar = new[]
            {
                new DocumentoFacturaPersistido(
                    DocumentoFacturaId: 9, FacturaId: 100, NombreArchivo: "factura.xml",
                    MimeType: "application/xml", RutaRelativa: "2026/08/factura.xml",
                    TamanoBytes: 2048, CreadoEn: DateTimeOffset.UtcNow),
            },
            AdjuntosDeFacturaACargar = new[]
            {
                new AdjuntoManual(
                    AdjuntoManualId: 700, FacturaId: 100, NombreArchivo: "f.pdf",
                    RutaRelativa: "2026/08/f.pdf", MimeType: "application/pdf",
                    TamanoBytes: 1024, SubidoPorUsuarioId: 1, SubidoEn: DateTimeOffset.UtcNow, EliminadoEn: null),
            },
        };

        return uow;
    }

    private static void AssertGolden(string actualJson, string expectedJson)
    {
        var actual = JsonNode.Parse(actualJson);
        var expected = JsonNode.Parse(expectedJson);
        Assert.True(
            JsonNode.DeepEquals(actual, expected),
            $"Expected:\n{expected!.ToJsonString()}\n\nActual:\n{actual!.ToJsonString()}");
    }

    [Fact]
    public async Task ConstruirAsync_FacturaValidada_ProduceElEnvelopeAutosuficiente()
    {
        var uow = NuevaUnidadConAsientoYFactura();

        var json = await PayloadOutbox.ConstruirAsync(uow, "FACTURA_VALIDADA", 100, asientoContableId: null, CancellationToken.None);

        const string esperado =
            """
            {
              "version": 1, "evento": "FACTURA_VALIDADA", "facturaId": 100,
              "factura": {
                "estado": "VALIDADA", "proveedorCodigo": "P00234", "rucProveedor": "20100000001",
                "tipoComprobante": "01", "numero": "F001-123", "totalOrig": 118.00, "moneda": "PEN",
                "fechaEmision": "2026-08-10", "motivo": null, "afectacion": "GRAVADA",
                "afectacionMixta": false, "esProveedorGenerico": false, "posibleDuplicado": false,
                "tieneCamposNoExtraidos": false
              },
              "asiento": {
                "asientoContableId": 5, "numeroAsiento": "02-2026-08-000007", "estado": "CONFIRMADO",
                "fechaContable": "2026-08-10",
                "lineas": [
                  { "lineaId": 30, "bloque": "Principal", "tipo": "D", "debe": 118.00, "haber": 0.00, "cuentaCodigo": "601111" }
                ]
              },
              "documentos": [
                { "origen": "INGESTA", "id": 9, "nombreArchivo": "factura.xml", "rutaRelativa": "2026/08/factura.xml", "mimeType": "application/xml" },
                { "origen": "ADJUNTO", "id": 700, "nombreArchivo": "f.pdf", "rutaRelativa": "2026/08/f.pdf", "mimeType": "application/pdf" }
              ]
            }
            """;

        AssertGolden(json, esperado);
    }

    [Fact]
    public async Task ConstruirAsync_FacturaCorregida_UsaElMismoSobreQueFacturaValidada()
    {
        var uow = NuevaUnidadConAsientoYFactura();

        var json = await PayloadOutbox.ConstruirAsync(uow, "FACTURA_CORREGIDA", 100, asientoContableId: null, CancellationToken.None);

        var envelope = JsonNode.Parse(json)!;
        Assert.Equal("FACTURA_CORREGIDA", envelope["evento"]!.GetValue<string>());
        Assert.Equal(100, envelope["facturaId"]!.GetValue<long>());
        Assert.Equal("VALIDADA", envelope["factura"]!["estado"]!.GetValue<string>());
        Assert.Equal(2, envelope["documentos"]!.AsArray().Count);
    }

    [Fact]
    public async Task ConstruirAsync_AsientoCorregido_ResuelveAsientoVigente()
    {
        var uow = NuevaUnidadConAsientoYFactura();

        var json = await PayloadOutbox.ConstruirAsync(uow, "ASIENTO_CORREGIDO", 100, asientoContableId: null, CancellationToken.None);

        var envelope = JsonNode.Parse(json)!;
        Assert.Equal("ASIENTO_CORREGIDO", envelope["evento"]!.GetValue<string>());
        Assert.Equal(5, envelope["asiento"]!["asientoContableId"]!.GetValue<long>());
    }

    [Fact]
    public async Task ConstruirAsync_AsientoAnulado_UsaElIdExplicito_NoElVigente()
    {
        // design D2: ObtenerAsientoVigenteIdAsync EXCLUYE ANULADO -- después de anular, "vigente"
        // ya no apunta al asiento anulado. ASIENTO_ANULADO debe pasar el id EXPLÍCITO para no
        // perder su propio asiento.
        var uow = NuevaUnidadConAsientoYFactura();
        uow.AsientoVigenteId = null; // el asiento ya no es "vigente" tras la anulación
        uow.AsientoACargar = uow.AsientoACargar! with { Estado = "ANULADO" };

        var json = await PayloadOutbox.ConstruirAsync(uow, "ASIENTO_ANULADO", 100, asientoContableId: 5, CancellationToken.None);

        var envelope = JsonNode.Parse(json)!;
        Assert.Equal("ASIENTO_ANULADO", envelope["evento"]!.GetValue<string>());
        Assert.Equal("ANULADO", envelope["asiento"]!["estado"]!.GetValue<string>());
        Assert.Equal(5, envelope["asiento"]!["asientoContableId"]!.GetValue<long>());
    }

    [Fact]
    public async Task ConstruirAsync_DocumentacionActualizada_UsaElMismoSobre()
    {
        var uow = NuevaUnidadConAsientoYFactura();

        var json = await PayloadOutbox.ConstruirAsync(uow, "DOCUMENTACION_ACTUALIZADA", 100, asientoContableId: null, CancellationToken.None);

        var envelope = JsonNode.Parse(json)!;
        Assert.Equal("DOCUMENTACION_ACTUALIZADA", envelope["evento"]!.GetValue<string>());
        Assert.Equal(2, envelope["documentos"]!.AsArray().Count);
    }

    [Fact]
    public async Task ConstruirAsync_SinAsientoVigente_EmiteAsientoNulo()
    {
        var uow = NuevaUnidadConAsientoYFactura();
        uow.AsientoVigenteId = null;

        var json = await PayloadOutbox.ConstruirAsync(uow, "FACTURA_CORREGIDA", 100, asientoContableId: null, CancellationToken.None);

        var envelope = JsonNode.Parse(json)!;
        Assert.True(envelope["asiento"] is null || envelope["asiento"]!.GetValueKind() == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task ConstruirAsync_FacturaInexistente_Lanza()
    {
        var uow = new FakeUnidadDeTrabajo { FacturaACargar = null };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => PayloadOutbox.ConstruirAsync(uow, "FACTURA_VALIDADA", 999, asientoContableId: null, CancellationToken.None));
    }
}
