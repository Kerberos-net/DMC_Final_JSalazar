using SmartNet.Catalogos.Core;
using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// BACKLOG #24 Phase 1 (design A1/A2, ADR 0019 nivel 1) — <see cref="SembradoDeAsiento"/> is a pure
/// builder: it maps a <see cref="FacturaPersistida"/> plus the externally-resolved
/// <see cref="HechosDeComposicion"/> onto an <see cref="EntradaAsiento"/> and runs
/// <see cref="ComposicionDeAsiento.Componer"/> (untouched). The golden cases transcribe REGLAS.md
/// §10.1/§10.2/§10.3 end to end; the placeholder case is design A2 (no suggested account).
/// </summary>
public class SembradoDeAsientoTests
{
    private static CuentaContable Cuenta(
        string codigo, string descripcion, string? ctaRefleja = null, string? ctaPuente = null) =>
        new(codigo, descripcion, null, ctaRefleja, ctaPuente);

    private static FacturaPersistida Factura(
        string tipoComprobante = "01",
        decimal totalOrig = 1180.00m,
        decimal? igvOrig = 180.00m,
        string moneda = "PEN",
        string? afectacion = "GRAVADA",
        string proveedorCodigo = "P00234") => new(
        FacturaId: 100,
        Estado: FacturaPersistida.PendienteValidacion,
        ProveedorCodigo: proveedorCodigo,
        RucProveedor: "20100000001",
        TipoComprobante: tipoComprobante,
        Numero: "F001-00234",
        TotalOrig: totalOrig,
        Moneda: moneda,
        FechaEmision: new DateOnly(2026, 8, 12),
        Motivo: 22,
        Afectacion: afectacion,
        Version: new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
        IgvOrig: igvOrig);

    private static LineaAsiento Linea(AsientoContable asiento, string cuenta, Bloque bloque, TipoLinea tipo) =>
        Assert.Single(asiento.Lineas, l => l.CuentaCodigo == cuenta && l.Bloque == bloque && l.Tipo == tipo);

    // ---- Construir: field-by-field map -------------------------------------------------------

    [Fact]
    public void Construir_MapeaLosCamposDeLaFacturaYLosHechos()
    {
        var cuenta = Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111");
        var factura = Factura();
        var hechos = new HechosDeComposicion(
            EsRelacionada: true, MotivoDescripcion: "Fletes traslado de mercaderia",
            TipoCambio: null, CuentaSugerida: cuenta);

        var entrada = SembradoDeAsiento.Construir(factura, hechos);

        Assert.Equal("P00234", entrada.ProveedorCodigo);
        Assert.True(entrada.EsRelacionada);
        Assert.Equal(MonedaAsiento.Pen, entrada.Moneda);
        Assert.Equal(new DateOnly(2026, 8, 12), entrada.FechaContable);
        Assert.Equal("Fletes traslado de mercaderia", entrada.MotivoDescripcion);
        Assert.Equal(TipoComprobante.Factura, entrada.Comprobante);
        Assert.Equal(Afectacion.Gravada, entrada.Afectacion);
        Assert.Equal(1000.00m, entrada.BaseOrig);
        Assert.Equal(180.00m, entrada.IgvOrig);
        Assert.Equal(0m, entrada.PercepcionOrig);
        Assert.Null(entrada.TipoCambio);
        Assert.Null(entrada.Herencia);
        var cargo = Assert.Single(entrada.Cargos);
        Assert.Equal("631111", cargo.Cuenta.Cuenta);
        Assert.Equal(1000.00m, cargo.ImportePEN);
    }

    [Fact]
    public void Construir_IgvOrigNulo_DerivaBaseIgualAlTotalYCeroIgv()
    {
        var factura = Factura(tipoComprobante: "03", totalOrig: 1180.00m, igvOrig: null, afectacion: "INAFECTA");
        var hechos = new HechosDeComposicion(false, "Utiles", null, Cuenta("656111", "UTILES DE ESCRITORIO"));

        var entrada = SembradoDeAsiento.Construir(factura, hechos);

        Assert.Equal(1180.00m, entrada.BaseOrig);
        Assert.Equal(0m, entrada.IgvOrig);
        Assert.Equal(TipoComprobante.Boleta, entrada.Comprobante);
        Assert.Equal(Afectacion.Inafecta, entrada.Afectacion);
    }

    // ---- Sembrar: REGLAS.md §10 goldens ----------------------------------------------------

    [Fact]
    public void Sembrar_10_1_FacturaGravadaEnSoles_ConDestino()
    {
        var factura = Factura();
        var hechos = new HechosDeComposicion(
            false, "Fletes", null, Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111"));

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        Assert.Equal(1000.00m, Linea(asiento, "631111", Bloque.Principal, TipoLinea.D).Debe);
        Assert.Equal(180.00m, Linea(asiento, "401111", Bloque.Principal, TipoLinea.D).Debe);
        Assert.Equal(1180.00m, Linea(asiento, "421211", Bloque.Principal, TipoLinea.H).Haber);
        Assert.Equal(1000.00m, Linea(asiento, "946311", Bloque.Destino, TipoLinea.D).Debe);
        Assert.Equal(1000.00m, Linea(asiento, "791111", Bloque.Destino, TipoLinea.H).Haber);
        Assert.Equal(1000.00m, asiento.BasePEN);
        Assert.Equal(180.00m, asiento.IgvPEN);
        Assert.Equal(1180.00m, asiento.NetoPEN);
    }

    [Fact]
    public void Sembrar_10_2_Boleta_ElIgvVaAlCosto_SinLinea401111()
    {
        var factura = Factura(tipoComprobante: "03", totalOrig: 1180.00m, igvOrig: null, afectacion: "INAFECTA", proveedorCodigo: "P00456");
        var hechos = new HechosDeComposicion(false, "Utiles de escritorio", null, Cuenta("656111", "UTILES DE ESCRITORIO"));

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        Assert.Equal(1180.00m, Linea(asiento, "656111", Bloque.Principal, TipoLinea.D).Debe);
        Assert.Equal(1180.00m, Linea(asiento, "421211", Bloque.Principal, TipoLinea.H).Haber);
        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "401111");
        Assert.Equal(1180.00m, asiento.NetoPEN);
    }

    [Fact]
    public void Sembrar_10_3_FacturaEnDolares_RedondeoDerivado()
    {
        var factura = Factura(moneda: "USD", proveedorCodigo: "P00789");
        var hechos = new HechosDeComposicion(
            EsRelacionada: true, MotivoDescripcion: "Compra relacionada",
            TipoCambio: TipoCambioCongelado.Heredado(3.7895m),
            CuentaSugerida: Cuenta("601111", "COMPRAS RELACIONADAS"));

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        Assert.Equal(4471.61m, asiento.NetoPEN);
        Assert.Equal(682.11m, asiento.IgvPEN);
        Assert.Equal(3789.50m, asiento.BasePEN);
        Assert.Equal(3789.50m, Linea(asiento, "601111", Bloque.Principal, TipoLinea.D).Debe);
        Assert.Equal(682.11m, Linea(asiento, "401111", Bloque.Principal, TipoLinea.D).Debe);
        Assert.Equal(4471.61m, Linea(asiento, "431212", Bloque.Principal, TipoLinea.H).Haber);
    }

    // ---- Sembrar: design A2 — no suggested account -----------------------------------------

    [Fact]
    public void Sembrar_SinCuentaSugerida_AgregaUnaLineaPlaceholderSinCuenta_YElAsientoCuadra()
    {
        var factura = Factura();
        var hechos = new HechosDeComposicion(false, "Fletes", null, CuentaSugerida: null);

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        var placeholder = Assert.Single(asiento.Lineas, l => l.SinCuenta);
        Assert.Equal(Bloque.Principal, placeholder.Bloque);
        Assert.Equal(TipoLinea.D, placeholder.Tipo);
        Assert.Equal(1000.00m, placeholder.Debe);
        Assert.Equal(asiento.Lineas.Max(l => l.Orden), placeholder.Orden);

        var totalDebe = asiento.Lineas.Sum(l => l.Debe);
        var totalHaber = asiento.Lineas.Sum(l => l.Haber);
        Assert.Equal(totalDebe, totalHaber);
        Assert.Equal(1180.00m, totalDebe);
    }

    // ---- Sembrar: guard (Batch 7) — GRAVADA con IgvOrig = 0 no puede sembrar un 401111 Debe = 0 --

    [Fact]
    public void Sembrar_GravadaConIgvCero_NoSiembraLineaConImporteCero_YElAsientoCuadra()
    {
        // GRAVADA + IgvOrig = 0 → Componer emite un 401111 con Debe = 0 que CK_Linea_Tipo rechaza.
        var factura = Factura(igvOrig: 0m);
        var hechos = new HechosDeComposicion(
            false, "Fletes", null, Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111"));

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "401111");
        Assert.DoesNotContain(asiento.Lineas, l => l.Debe == 0m && l.Haber == 0m);
        Assert.Equal(asiento.Lineas.Sum(l => l.Debe), asiento.Lineas.Sum(l => l.Haber));
        // Orden renumerado 1..n contiguo tras el drop.
        Assert.Equal(
            Enumerable.Range(1, asiento.Lineas.Count).Select(i => (short)i),
            asiento.Lineas.Select(l => l.Orden).OrderBy(o => o));

        // §7 sigue siendo la puerta: un asiento GRAVADO sin su línea 401111 no confirma.
        var resultado = InvariantesDeConfirmacion.Evaluar(asiento, new DateOnly(2000, 1, 1));
        Assert.IsType<ResultadoConfirmacion.InvariantesIncumplidas>(resultado);
    }

    [Fact]
    public void Sembrar_GravadaConIgvCero_SinCuentaSugerida_UsaElPlaceholder_YCuadra()
    {
        var factura = Factura(igvOrig: 0m);
        var hechos = new HechosDeComposicion(false, "Fletes", null, CuentaSugerida: null);

        var asiento = SembradoDeAsiento.Sembrar(factura, hechos);

        Assert.DoesNotContain(asiento.Lineas, l => l.Debe == 0m && l.Haber == 0m);
        Assert.Single(asiento.Lineas, l => l.SinCuenta);
        Assert.Equal(asiento.Lineas.Sum(l => l.Debe), asiento.Lineas.Sum(l => l.Haber));

        var resultado = InvariantesDeConfirmacion.Evaluar(asiento, new DateOnly(2000, 1, 1));
        Assert.IsType<ResultadoConfirmacion.InvariantesIncumplidas>(resultado);
    }
}
