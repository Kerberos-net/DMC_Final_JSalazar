using System.Text.Json.Nodes;
using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// ADR 0019 level-2 contract test (BACKLOG #14, Fase 5, tasks.md 5.1): <see cref="PayloadOutbox"/>
/// against the SAME fixture `SmartNet/worker/tests/fixtures/outbox_event_payload.golden.json` that
/// `test_outbox_event_payload_contract.py` asserts the Python consumer passes through byte-for-byte
/// unchanged. Proves the .NET producer writes exactly what the wire format both sides agreed on --
/// not two independently self-asserted shapes (unlike <see cref="PayloadOutboxTests"/>'s inline
/// literals, task 1.1), mirroring <c>PayloadInboxContractTests</c>' precedent (item #7).
/// </summary>
public sealed class PayloadOutboxContractTests
{
    private static FakeUnidadDeTrabajo NuevaUnidadConAsientoYFactura()
    {
        return new FakeUnidadDeTrabajo
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
    }

    [Fact]
    public async Task ConstruirAsync_FacturaValidada_MatchesTheSharedGoldenFixtureExactly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "outbox_event_payload.golden.json");
        var golden = File.ReadAllText(path);

        var uow = NuevaUnidadConAsientoYFactura();
        var json = await PayloadOutbox.ConstruirAsync(
            uow, "FACTURA_VALIDADA", 100, asientoContableId: null, CancellationToken.None);

        var actual = JsonNode.Parse(json);
        var expected = JsonNode.Parse(golden);
        Assert.True(
            JsonNode.DeepEquals(actual, expected),
            $"Expected:\n{expected!.ToJsonString()}\n\nActual:\n{actual!.ToJsonString()}");
    }
}
