namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// BACKLOG #13, Phase 2 (tasks 2.1/2.3/2.5/2.7), design.md D2/D4/D5, ADR 0019 level 1 — pure
/// derivation functions with no DB/HTTP/clock dependency. All facts (error counts, pending
/// CommandQueue rows, "now") are received as parameters, resolved by Infrastructure.
///
/// RED/GREEN granularity (documented per the project's existing compression precedent —
/// PermissionMatrixTests.cs's header comment): this file's tests were written together against no
/// production code (OrigenBandeja.cs did not exist) — every call site failed to compile, which is
/// RED. OrigenBandeja.cs was then authored once and the same tests re-run to GREEN.
/// </summary>
public class OrigenBandejaTests
{
    // ---------------------------------------------------------------------------------------
    // Task 2.1/2.2 — origen = FACTURA iff EstadoConsumo=="PROMOVIDO" && FacturaId != null, else
    // INCIDENCIA.
    // ---------------------------------------------------------------------------------------
    [Theory]
    [InlineData("PROMOVIDO", 42L, "FACTURA")]
    [InlineData("PROMOVIDO", null, "INCIDENCIA")]
    [InlineData("PENDIENTE", 42L, "INCIDENCIA")]
    [InlineData("PENDIENTE", null, "INCIDENCIA")]
    [InlineData("DESCARTADO", null, "INCIDENCIA")]
    public void Derivar_Origen_SiguePrecisamenteEstadoYFacturaId(string estadoConsumo, long? facturaId, string esperado)
    {
        var origen = OrigenBandeja.Derivar(estadoConsumo, facturaId);

        Assert.Equal(esperado, origen);
    }

    // ---------------------------------------------------------------------------------------
    // Task 2.3/2.4 — default-view predicate: PENDIENTE OR >=1 error no-OBSOLETO; DESCARTADO and
    // error-free PROMOVIDO are excluded.
    // ---------------------------------------------------------------------------------------
    [Theory]
    [InlineData("PENDIENTE", 0, true)]
    [InlineData("PENDIENTE", 2, true)]
    [InlineData("PROMOVIDO", 1, true)]   // promoted but still has an open (non-OBSOLETO) error
    [InlineData("PROMOVIDO", 0, false)]  // error-free PROMOVIDO is terminal, excluded
    [InlineData("DESCARTADO", 0, false)] // DESCARTADO with no open error is terminal, excluded
    [InlineData("DESCARTADO", 1, true)]  // DESCARTADO but still carries an open error
    public void EsVistaPorDefecto_CombinaEstadoPendienteConErroresAbiertos(
        string estadoConsumo, int erroresNoObsoletos, bool esperado)
    {
        var incluido = OrigenBandeja.EsVistaPorDefecto(estadoConsumo, erroresNoObsoletos);

        Assert.Equal(esperado, incluido);
    }

    // ---------------------------------------------------------------------------------------
    // Task 2.5/2.6 — PoliticaDeReprocesamiento.VentanaBloqueo: null when no pending CommandQueue
    // row, else MAX(CreadoEn) + ventanaMinutos. Pure — the "now" comparison happens in SQL (D5),
    // this function only adds the window to a given timestamp.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public void VentanaBloqueo_EsNull_CuandoNoHayComandoPendiente()
    {
        var resultado = PoliticaDeReprocesamiento.VentanaBloqueo(ultimoCreadoEnPendiente: null);

        Assert.Null(resultado);
    }

    [Fact]
    public void VentanaBloqueo_SumaCincoMinutos_AlUltimoCreadoEn()
    {
        var ultimoCreadoEn = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        var resultado = PoliticaDeReprocesamiento.VentanaBloqueo(ultimoCreadoEn);

        Assert.Equal(new DateTime(2026, 8, 24, 10, 5, 0, DateTimeKind.Utc), resultado);
    }

    [Fact]
    public void VentanaBloqueo_UsaVentanaMinutosExplicita_CuandoSePasaDistintaDeCinco()
    {
        var ultimoCreadoEn = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

        var resultado = PoliticaDeReprocesamiento.VentanaBloqueo(ultimoCreadoEn, ventanaMinutos: 10);

        Assert.Equal(new DateTime(2026, 8, 24, 10, 10, 0, DateTimeKind.Utc), resultado);
    }

    [Fact]
    public void VentanaMinutos_ConstanteEsCinco()
    {
        Assert.Equal(5, PoliticaDeReprocesamiento.VentanaMinutos);
    }

    // ---------------------------------------------------------------------------------------
    // Task 2.7/2.8 — envelope math: totalPaginas = ceil(totalRegistros/tamanioPagina).
    // ---------------------------------------------------------------------------------------
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(41, 20, 3)]
    public void CalcularTotalPaginas_RedondeaHaciaArriba(int totalRegistros, int tamanioPagina, int esperado)
    {
        var totalPaginas = EnvelopeBandeja.CalcularTotalPaginas(totalRegistros, tamanioPagina);

        Assert.Equal(esperado, totalPaginas);
    }
}
