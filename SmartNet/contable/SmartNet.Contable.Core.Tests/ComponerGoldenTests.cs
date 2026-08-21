using SmartNet.Catalogos.Core;
using SmartNet.Contable.Core;
using SmartNet.TiposCambio.Core;

namespace SmartNet.Contable.Core.Tests;

/// <summary>
/// tasks.md Phase 3 — the 7 golden examples of REGLAS.md §10, transcribed line by line (cuenta,
/// tipo, importe, bloque) from the document, not from the implementation.
/// </summary>
public class ComponerGoldenTests
{
    private static CuentaContable Cuenta(string codigo, string descripcion, string? ctaRefleja = null, string? ctaPuente = null) =>
        new(codigo, descripcion, null, ctaRefleja, ctaPuente);

    private static LineaAsiento Linea(AsientoContable asiento, string cuenta, Bloque bloque, TipoLinea tipo) =>
        Assert.Single(asiento.Lineas, l => l.CuentaCodigo == cuenta && l.Bloque == bloque && l.Tipo == tipo);

    // ---- 10.1: factura gravada en soles, con destino -----------------------------------------

    [Fact]
    public void Golden_10_1_FacturaGravadaEnSoles_ConDestino()
    {
        var cuentaFlete = Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111");
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00234", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Fletes",
            Comprobante: TipoComprobante.Factura, Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m, IgvOrig: 180.00m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaFlete, 1000.00m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        var cargo = Linea(asiento, "631111", Bloque.Principal, TipoLinea.D);
        Assert.Equal(1000.00m, cargo.Debe);

        var igv = Linea(asiento, "401111", Bloque.Principal, TipoLinea.D);
        Assert.Equal(180.00m, igv.Debe);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.H);
        Assert.Equal(1180.00m, proveedor.Haber);

        var reflejo = Linea(asiento, "946311", Bloque.Destino, TipoLinea.D);
        Assert.Equal(1000.00m, reflejo.Debe);

        var puente = Linea(asiento, "791111", Bloque.Destino, TipoLinea.H);
        Assert.Equal(1000.00m, puente.Haber);
    }

    // ---- 10.2: boleta, IGV al costo -----------------------------------------------------------

    [Fact]
    public void Golden_10_2_Boleta_IgvAlCosto()
    {
        var cuentaUtiles = Cuenta("656111", "UTILES DE ESCRITORIO");
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00456", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Utiles de escritorio",
            Comprobante: TipoComprobante.Boleta, Afectacion: Afectacion.Inafecta,
            BaseOrig: 1180.00m, IgvOrig: 0m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaUtiles, 1180.00m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        var cargo = Linea(asiento, "656111", Bloque.Principal, TipoLinea.D);
        Assert.Equal(1180.00m, cargo.Debe);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.H);
        Assert.Equal(1180.00m, proveedor.Haber);

        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "401111");
    }

    // ---- 10.3: factura en dólares, redondeo derivado ------------------------------------------

    [Fact]
    public void Golden_10_3_FacturaDolares_RedondeoDerivado()
    {
        var cuentaMotivo = Cuenta("601111", "COMPRAS RELACIONADAS");
        var tc = TipoCambioCongelado.DeTipoCambio(new TipoCambio(
            new DateOnly(2026, 8, 12), OrigenTipoCambio.Sbs, 3.7880m, 3.7895m,
            new DateTime(2026, 8, 12, 20, 0, 0)));

        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00789", EsRelacionada: true, Moneda: MonedaAsiento.Usd,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Compra relacionada",
            Comprobante: TipoComprobante.Factura, Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m, IgvOrig: 180.00m, PercepcionOrig: 0m,
            TipoCambio: tc,
            Cargos: new[] { new CargoSolicitado(cuentaMotivo, 3789.50m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        Assert.Equal(4471.61m, asiento.NetoPEN);
        Assert.Equal(682.11m, asiento.IgvPEN);
        Assert.Equal(3789.50m, asiento.BasePEN);

        var igv = Linea(asiento, "401111", Bloque.Principal, TipoLinea.D);
        Assert.Equal(682.11m, igv.Debe);

        var proveedor = Linea(asiento, "431212", Bloque.Principal, TipoLinea.H);
        Assert.Equal(4471.61m, proveedor.Haber);
    }

    // ---- 10.4: factura con percepción ----------------------------------------------------------

    [Fact]
    public void Golden_10_4_FacturaConPercepcion()
    {
        var cuentaMotivo = Cuenta("602111", "COMPRA DE MERCADERIAS");
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00312", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Mercaderia",
            Comprobante: TipoComprobante.Factura, Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m, IgvOrig: 180.00m, PercepcionOrig: 23.60m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaMotivo, 1000.00m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        var percepcion = Linea(asiento, "401131", Bloque.Principal, TipoLinea.D);
        Assert.Equal(23.60m, percepcion.Debe);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.H);
        Assert.Equal(1203.60m, proveedor.Haber);

        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "401131" && l.Bloque == Bloque.Destino);
    }

    // ---- 10.5: nota de crédito sobre factura gravada -------------------------------------------

    [Fact]
    public void Golden_10_5_NotaDeCreditoSobreFacturaGravada()
    {
        var cuentaFlete = Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111");
        var entradaFactura = new EntradaAsiento(
            ProveedorCodigo: "P00234", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 1), MotivoDescripcion: "Fletes",
            Comprobante: TipoComprobante.Factura, Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m, IgvOrig: 180.00m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaFlete, 1000.00m) },
            Herencia: null);
        var facturaAsiento = ComposicionDeAsiento.Componer(entradaFactura);
        var herencia = HerenciaNotaCredito.DesdeAsiento(facturaAsiento);

        var cuentaFleteParcial = Cuenta("631111", "FLETE TRASLADO DE MERCADERIA", "946311", "791111");
        herencia = herencia with { CargosCongelados = new[] { new CargoSolicitado(cuentaFleteParcial, 200.00m) } };

        var entradaNC = new EntradaAsiento(
            ProveedorCodigo: "P00234", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 20), MotivoDescripcion: null,
            Comprobante: TipoComprobante.NotaCredito, Afectacion: Afectacion.Gravada,
            BaseOrig: 200.00m, IgvOrig: 36.00m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: Array.Empty<CargoSolicitado>(),
            Herencia: herencia);

        var asiento = ComposicionDeAsiento.Componer(entradaNC);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.D);
        Assert.Equal(236.00m, proveedor.Debe);

        var cargo = Linea(asiento, "631111", Bloque.Principal, TipoLinea.H);
        Assert.Equal(200.00m, cargo.Haber);

        var igv = Linea(asiento, "401111", Bloque.Principal, TipoLinea.H);
        Assert.Equal(36.00m, igv.Haber);

        var reflejo = Linea(asiento, "946311", Bloque.Destino, TipoLinea.H);
        Assert.Equal(200.00m, reflejo.Haber);

        var puente = Linea(asiento, "791111", Bloque.Destino, TipoLinea.D);
        Assert.Equal(200.00m, puente.Debe);
    }

    // ---- 10.6: nota de crédito sobre boleta ----------------------------------------------------

    [Fact]
    public void Golden_10_6_NotaDeCreditoSobreBoleta()
    {
        var cuentaSuministros = Cuenta("656101", "SUMINISTROS", "946561", "791111");
        var entradaBoleta = new EntradaAsiento(
            ProveedorCodigo: "P00512", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 7, 15), MotivoDescripcion: "Suministros",
            Comprobante: TipoComprobante.Boleta, Afectacion: Afectacion.Inafecta,
            BaseOrig: 118.00m, IgvOrig: 0m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaSuministros, 118.00m) },
            Herencia: null);
        var boletaAsiento = ComposicionDeAsiento.Componer(entradaBoleta);
        var herencia = HerenciaNotaCredito.DesdeAsiento(boletaAsiento);

        var entradaNC = new EntradaAsiento(
            ProveedorCodigo: "P00512", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 20), MotivoDescripcion: null,
            Comprobante: TipoComprobante.NotaCredito, Afectacion: Afectacion.Gravada,
            BaseOrig: 118.00m, IgvOrig: 0m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: Array.Empty<CargoSolicitado>(),
            Herencia: herencia);

        var asiento = ComposicionDeAsiento.Componer(entradaNC);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.D);
        Assert.Equal(118.00m, proveedor.Debe);

        var cargo = Linea(asiento, "656101", Bloque.Principal, TipoLinea.H);
        Assert.Equal(118.00m, cargo.Haber);

        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "401111");

        var reflejo = Linea(asiento, "946561", Bloque.Destino, TipoLinea.H);
        Assert.Equal(118.00m, reflejo.Haber);

        var puente = Linea(asiento, "791111", Bloque.Destino, TipoLinea.D);
        Assert.Equal(118.00m, puente.Debe);
    }

    // ---- 10.7: NC 100% en dólares hereda el TC de la factura -----------------------------------

    [Fact]
    public void Golden_10_7_NotaDeCredito100PorCientoDolares_HeredaTipoDeCambio()
    {
        var cuentaFlete = Cuenta("631111", "FLETE TRASLADO DE MERCADERIA");
        var tcFactura = TipoCambioCongelado.DeTipoCambio(new TipoCambio(
            new DateOnly(2026, 8, 12), OrigenTipoCambio.Sbs, 3.700000m, 3.712000m,
            new DateTime(2026, 8, 12, 20, 0, 0)));

        var entradaFactura = new EntradaAsiento(
            ProveedorCodigo: "P00301", EsRelacionada: false, Moneda: MonedaAsiento.Usd,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Fletes",
            Comprobante: TipoComprobante.Factura, Afectacion: Afectacion.Gravada,
            BaseOrig: 10000.00m, IgvOrig: 1525.42m, PercepcionOrig: 0m,
            TipoCambio: tcFactura,
            Cargos: new[] { new CargoSolicitado(cuentaFlete, 37120.00m) },
            Herencia: null);
        var facturaAsiento = ComposicionDeAsiento.Componer(entradaFactura);
        Assert.Equal(42782.36m, facturaAsiento.NetoPEN);

        var herencia = HerenciaNotaCredito.DesdeAsiento(facturaAsiento);
        Assert.NotNull(herencia.TipoCambioCongelado);
        Assert.Equal(3.712000m, herencia.TipoCambioCongelado!.Venta);

        // La NC llega con su PROPIO TC de fecha 3.715000, que Componer debe ignorar en favor del heredado.
        var tcPropioDeLaNota = TipoCambioCongelado.DeTipoCambio(new TipoCambio(
            new DateOnly(2026, 9, 3), OrigenTipoCambio.Sbs, 3.700000m, 3.715000m,
            new DateTime(2026, 9, 3, 20, 0, 0)));

        var entradaNC = new EntradaAsiento(
            ProveedorCodigo: "P00301", EsRelacionada: false, Moneda: MonedaAsiento.Usd,
            FechaContable: new DateOnly(2026, 9, 3), MotivoDescripcion: null,
            Comprobante: TipoComprobante.NotaCredito, Afectacion: Afectacion.Gravada,
            BaseOrig: 10000.00m, IgvOrig: 1525.42m, PercepcionOrig: 0m,
            TipoCambio: tcPropioDeLaNota,
            Cargos: Array.Empty<CargoSolicitado>(),
            Herencia: herencia);

        var asientoNC = ComposicionDeAsiento.Componer(entradaNC);

        Assert.Equal(3.712000m, asientoNC.TipoCambioVenta);
        Assert.Equal(42782.36m, asientoNC.NetoPEN);

        var proveedorFactura = Linea(facturaAsiento, "421212", Bloque.Principal, TipoLinea.H);
        var proveedorNC = Linea(asientoNC, "421212", Bloque.Principal, TipoLinea.D);
        Assert.Equal(proveedorFactura.Haber, proveedorNC.Debe);
        Assert.Equal(0.00m, proveedorFactura.Haber - proveedorNC.Debe);
    }

    // ---- tasks.md 3.19: estructura, nunca rechaza (ADR 0006 BORRADOR) --------------------------

    [Theory]
    [InlineData(true, true)]   // factura gravada
    [InlineData(true, false)]  // factura no gravada
    [InlineData(false, true)]  // boleta "gravada" en el catálogo (aun así sin 401111)
    [InlineData(false, false)] // boleta no gravada
    public void Componer_CuatroCasosDePrincipal_NuncaLanza(bool esFactura, bool esGravada)
    {
        var cuentaSinReflejo = Cuenta("602111", "COMPRA DE MERCADERIAS");
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00999", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Mercaderia",
            Comprobante: esFactura ? TipoComprobante.Factura : TipoComprobante.Boleta,
            Afectacion: esGravada ? Afectacion.Gravada : Afectacion.Inafecta,
            BaseOrig: 100.00m, IgvOrig: esGravada ? 18.00m : 0m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaSinReflejo, 100.00m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        Assert.NotEmpty(asiento.Lineas);
        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo == "602111" && l.Bloque == Bloque.Destino);
    }

    // ---- verify-report WARNING (2026-08-19), SUGGESTION 1: regresión que pinea el comportamiento
    // actual de Boleta + Gravada, hoy asumido imposible por #3/#11 y confiado sin verificar
    // TipoComprobante en el discriminador `esGravada` de Componer. NO es una aprobación de que este
    // sea el comportamiento correcto — es un centinela: si el discriminador cambia (o si #3/#11
    // dejan de garantizar la exclusión), este test se rompe y obliga a una revisión consciente en
    // vez de un drift silencioso. El guard/validación en #3/#11 queda explícitamente fuera de
    // alcance de #8 (seguimiento aparte).

    [Fact]
    public void Componer_BoletaMarcadaGravada_PineaGeneracionActualDeLinea401111()
    {
        var cuentaSinReflejo = Cuenta("602111", "COMPRA DE MERCADERIAS");
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00999", EsRelacionada: false, Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12), MotivoDescripcion: "Mercaderia",
            Comprobante: TipoComprobante.Boleta, Afectacion: Afectacion.Gravada,
            BaseOrig: 100.00m, IgvOrig: 18.00m, PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(cuentaSinReflejo, 100.00m) },
            Herencia: null);

        var asiento = ComposicionDeAsiento.Componer(entrada);

        // Comportamiento ACTUAL (no el correcto contablemente): el discriminador solo mira
        // AfectacionCongelada, así que una boleta marcada Gravada SÍ recibe línea 401111 con el
        // IGV completo, como si fuera una factura gravada.
        var igv = Linea(asiento, "401111", Bloque.Principal, TipoLinea.D);
        Assert.Equal(18.00m, igv.Debe);

        var proveedor = Linea(asiento, "421211", Bloque.Principal, TipoLinea.H);
        Assert.Equal(118.00m, proveedor.Haber);
    }
}
