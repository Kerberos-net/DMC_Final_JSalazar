using SmartNet.Contable.Core;

namespace SmartNet.Contable.Core.Tests;

/// <summary>
/// tasks.md Phase 4 — REGLAS.md §7 invariantes de confirmación dentro del alcance de #8 (design.md
/// Decisión 4): globales 1–5, PRINCIPAL, DESTINO. Asientos construidos a mano, sin pasar por
/// <see cref="ComposicionDeAsiento"/> (design.md Testing Strategy).
/// </summary>
public class InvariantesDeConfirmacionTests
{
    private static readonly DateOnly FechaCorte = new(2026, 8, 1);

    /// <summary>Espejo manual del golden §10.1: factura gravada en soles, con destino, cuadrada.</summary>
    private static AsientoContable AsientoValido() => new(
        ProveedorCodigo: "P00234",
        FechaContable: new DateOnly(2026, 8, 12),
        MotivoDescripcion: "Fletes",
        TipoCambioVenta: null,
        BasePEN: 1000.00m,
        IgvPEN: 180.00m,
        NetoPEN: 1180.00m,
        AfectacionCongelada: Afectacion.Gravada,
        Comprobante: TipoComprobante.Factura,
        Lineas: new List<LineaAsiento>
        {
            new(1, Bloque.Principal, TipoLinea.D, 1000.00m, 0m, "631111", "FLETE", "946311", "791111"),
            new(2, Bloque.Principal, TipoLinea.D, 180.00m, 0m, "401111", "IGV - CUENTA PROPIA", null, null),
            new(3, Bloque.Principal, TipoLinea.H, 0m, 1180.00m, "421211", "FACTURAS Y BOLETAS EN SOLES", null, null),
            new(4, Bloque.Destino, TipoLinea.D, 1000.00m, 0m, "946311", null, null, null),
            new(5, Bloque.Destino, TipoLinea.H, 0m, 1000.00m, "791111", "CARGAS IMPUTABLES", null, null),
        });

    private static ResultadoConfirmacion.InvariantesIncumplidas EvaluarYEsperarRechazo(AsientoContable asiento, DateOnly? corte = null)
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(asiento, corte ?? FechaCorte);
        return Assert.IsType<ResultadoConfirmacion.InvariantesIncumplidas>(resultado);
    }

    // ---- global 1: SUM(Debe) = SUM(Haber) -------------------------------------------------------

    [Fact]
    public void Global1_AsientoCuadrado_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Global1_Descuadre_Rechaza()
    {
        var asiento = AsientoValido();
        var descuadrado = asiento with
        {
            Lineas = asiento.Lineas.Select(l => l.CuentaCodigo == "421211" ? l with { Haber = 1000.00m } : l).ToList(),
        };

        var rechazo = EvaluarYEsperarRechazo(descuadrado);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.SumaDebeIgualHaber);
    }

    // ---- global 2: ninguna línea sin cuenta ------------------------------------------------------

    [Fact]
    public void Global2_TodaLineaConCuenta_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Global2_LineaSinCuenta_Rechaza()
    {
        var asiento = AsientoValido();
        var conLineaSinCuenta = asiento with
        {
            Lineas = asiento.Lineas.Select(l => l.CuentaCodigo == "631111" ? l with { CuentaCodigo = null } : l).ToList(),
        };

        var rechazo = EvaluarYEsperarRechazo(conLineaSinCuenta);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.LineaSinCuenta);
    }

    // ---- global 3: FechaContable >= FechaCorteContable -------------------------------------------

    [Fact]
    public void Global3_FechaEnOTrasElCorte_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), new DateOnly(2026, 8, 12));
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Global3_FechaAnteriorAlCorte_Rechaza()
    {
        // fechaCorteContable llega como PARAMETRO, nunca DateTime.Today (ADR 0019) — PurityScanTests
        // lo comprueba a nivel de ensamblado; este test comprueba el comportamiento.
        var rechazo = EvaluarYEsperarRechazo(AsientoValido(), corte: new DateOnly(2026, 9, 1));

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.FechaAnteriorAlCorte);
    }

    // ---- global 4: proveedor != P00000 ------------------------------------------------------------

    [Fact]
    public void Global4_ProveedorDistintoDeVarios_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Global4_ProveedorVarios_Rechaza()
    {
        var asiento = AsientoValido() with { ProveedorCodigo = "P00000" };

        var rechazo = EvaluarYEsperarRechazo(asiento);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.ProveedorVarios);
    }

    // ---- global 5: Tipo=D => Debe>0,Haber=0 (e inverso) -------------------------------------------

    [Fact]
    public void Global5_TipoLineaConsistente_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Global5_TipoDConHaberPositivo_Rechaza()
    {
        var asiento = AsientoValido();
        var inconsistente = asiento with
        {
            Lineas = asiento.Lineas.Select(l => l.CuentaCodigo == "631111"
                ? l with { Haber = 5.00m }
                : l).ToList(),
        };

        var rechazo = EvaluarYEsperarRechazo(inconsistente);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.TipoLineaInconsistente);
    }

    // ---- PRINCIPAL: cargos/401111/proveedor según REGLAS.md §7 tabla ------------------------------

    [Fact]
    public void Principal_FacturaGravadaConsistente_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Principal_401111IndebidoEnNotaCreditoSobreBoleta_Rechaza()
    {
        // Espejo manual del golden §10.6 pero con un 401111 colado — la boleta nunca otorgó
        // crédito fiscal, así que revertirlo es indebido (REGLAS.md §7, cuarta fila de la tabla).
        var asiento = new AsientoContable(
            ProveedorCodigo: "P00512",
            FechaContable: new DateOnly(2026, 8, 20),
            MotivoDescripcion: null,
            TipoCambioVenta: null,
            BasePEN: 118.00m,
            IgvPEN: 0m,
            NetoPEN: 118.00m,
            AfectacionCongelada: Afectacion.Inafecta,
            Comprobante: TipoComprobante.NotaCredito,
            Lineas: new List<LineaAsiento>
            {
                new(1, Bloque.Principal, TipoLinea.D, 118.00m, 0m, "421211", "FACTURAS Y BOLETAS EN SOLES", null, null),
                new(2, Bloque.Principal, TipoLinea.H, 0m, 118.00m, "656101", "SUMINISTROS", null, null),
                new(3, Bloque.Principal, TipoLinea.H, 0m, 0.00m, "401111", "IGV - CUENTA PROPIA", null, null),
            });

        var rechazo = EvaluarYEsperarRechazo(asiento);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.Principal);
    }

    // ---- verify-report WARNING (2026-08-19), SUGGESTION 1: regresión que pinea el comportamiento
    // actual de Boleta + Gravada en EvaluarPrincipal. El discriminador `esGravada` de esta invariante
    // también mira solo AfectacionCongelada, confiando en que #3/#11 nunca marquen Gravada una
    // boleta. Este test NO valida que sea correcto contablemente — fija que, HOY, un asiento
    // Boleta+Gravada internamente consistente (401111 presente y cuadrado con el IGV) es ACEPTADO
    // como Confirmable. Si el día de mañana se agrega el guard TipoComprobante==Boleta (fuera de
    // alcance de #8), este test debe romperse a propósito y forzar su actualización consciente.

    [Fact]
    public void Principal_BoletaMarcadaGravadaConsistente_PineaAceptacionActual()
    {
        var asiento = new AsientoContable(
            ProveedorCodigo: "P00999",
            FechaContable: new DateOnly(2026, 8, 12),
            MotivoDescripcion: "Mercaderia",
            TipoCambioVenta: null,
            BasePEN: 100.00m,
            IgvPEN: 18.00m,
            NetoPEN: 118.00m,
            AfectacionCongelada: Afectacion.Gravada,
            Comprobante: TipoComprobante.Boleta,
            Lineas: new List<LineaAsiento>
            {
                new(1, Bloque.Principal, TipoLinea.D, 100.00m, 0m, "602111", "COMPRA DE MERCADERIAS", null, null),
                new(2, Bloque.Principal, TipoLinea.D, 18.00m, 0m, "401111", "IGV - CUENTA PROPIA", null, null),
                new(3, Bloque.Principal, TipoLinea.H, 0m, 118.00m, "421211", "FACTURAS Y BOLETAS EN SOLES", null, null),
            });

        var resultado = InvariantesDeConfirmacion.Evaluar(asiento, FechaCorte);

        // Comportamiento ACTUAL (no el correcto contablemente): sin chequear TipoComprobante, la
        // invariante PRINCIPAL trata este asiento como una factura gravada válida y lo confirma.
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    // ---- DESTINO: cada cargo con CtaReflejaCodigo tiene su par ------------------------------------

    [Fact]
    public void Destino_ParReflejoPuentePresente_Acepta()
    {
        var resultado = InvariantesDeConfirmacion.Evaluar(AsientoValido(), FechaCorte);
        Assert.IsType<ResultadoConfirmacion.Confirmable>(resultado);
    }

    [Fact]
    public void Destino_FaltaElPar_Rechaza()
    {
        var asiento = AsientoValido();
        // La cuenta 631111 sigue congelando CtaReflejaCodigo=946311 en la línea PRINCIPAL, aunque
        // el catálogo vivo ya no lo declare (REGLAS.md §7 "Del bloque DESTINO") — evaluado contra
        // el dato congelado en la línea, nunca contra el catálogo.
        var sinDestino = asiento with
        {
            Lineas = asiento.Lineas.Where(l => l.Bloque != Bloque.Destino).ToList(),
        };

        var rechazo = EvaluarYEsperarRechazo(sinDestino);

        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.Destino);
    }

    // ---- multi-fallo -------------------------------------------------------------------------------

    [Fact]
    public void MultiFallo_DosInvariantesIncumplidasSimultaneamente_ProduceDosEntradas()
    {
        var asiento = AsientoValido();
        var conDosFallos = asiento with
        {
            ProveedorCodigo = "P00000",
            Lineas = asiento.Lineas.Select(l => l.CuentaCodigo == "421211" ? l with { Haber = 999.00m } : l).ToList(),
        };

        var rechazo = EvaluarYEsperarRechazo(conDosFallos);

        Assert.True(rechazo.Fallos.Count >= 2);
        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.ProveedorVarios);
        Assert.Contains(rechazo.Fallos, f => f.Invariante == InvarianteContable.SumaDebeIgualHaber);
    }

    // ---- tasks.md 4.13: la precondición vieja de NC no aparece en ningún lugar --------------------

    [Fact]
    public void InvarianteContable_NoCodificaLaPrecondicionViejaDeNC()
    {
        // proposal.md Non-Goals / design.md Decisión 4: la precondición "factura original
        // validada" fue relajada por el dueño del proyecto y #8 no debe codificarla, ni siquiera
        // como placeholder. Siete valores exactos: 5 globales + PRINCIPAL + DESTINO.
        var nombres = Enum.GetNames<InvarianteContable>();

        Assert.Equal(7, nombres.Length);
        Assert.DoesNotContain(nombres, n => n.Contains("Validada", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nombres, n => n.Contains("Precondicion", StringComparison.OrdinalIgnoreCase));
    }
}
