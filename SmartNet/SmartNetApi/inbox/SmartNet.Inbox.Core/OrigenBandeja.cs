namespace SmartNet.Inbox.Core;

/// <summary>
/// Pure `origen` derivation and default-view predicate for the bandeja (design.md D2, ADR 0019
/// level 1). Both facts (state, error counts) are resolved by Infrastructure and passed in — this
/// type touches no database, no clock.
/// </summary>
public static class OrigenBandeja
{
    /// <summary>
    /// `origen` = FACTURA iff EstadoConsumo=="PROMOVIDO" &amp;&amp; FacturaId is not null, else
    /// INCIDENCIA (design.md Data Flow, Derivation rules).
    /// </summary>
    public static string Derivar(string estadoConsumo, long? facturaId) =>
        estadoConsumo == "PROMOVIDO" && facturaId is not null ? "FACTURA" : "INCIDENCIA";

    /// <summary>
    /// Default view (no filters supplied) = EstadoConsumo == "PENDIENTE" OR at least one error
    /// with Clasificacion &lt;&gt; "OBSOLETO" (design.md Derivation rules). `DESCARTADO` and
    /// error-free `PROMOVIDO` rows fall through both conditions and are excluded (terminal).
    /// </summary>
    public static bool EsVistaPorDefecto(string estadoConsumo, int erroresNoObsoletos) =>
        estadoConsumo == "PENDIENTE" || erroresNoObsoletos > 0;
}

/// <summary>
/// BACKLOG #21 follow-up — the closed vocabulary for <c>GET /api/bandeja?estadoDerivado=</c>, the
/// SPA estado-chip filter. Values map 1:1 to the dashboard cards / derived Estado chip buckets,
/// plus <c>TODOS</c> for the whole eligible set. Pure (ADR 0019 level 1): the endpoint validates
/// against this set, <c>SqlBandejaRepository</c> applies the same first-match CASE the resumen uses.
/// </summary>
public static class EstadoDerivadoBandeja
{
    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "TODOS", "PENDIENTE", "VALIDADA", "ERROR", "ALERTA", "DESCARTADA",
        };

    public static bool EsValido(string? valor) => valor is not null && Valores.Contains(valor);
}

/// <summary>
/// BACKLOG #13, design.md D5 — the 5-minute reprocesar window lives here, testable, not buried in a
/// SQL string. The engine still evaluates "now" (SQL's SYSUTCDATETIME) against this window (D5); this
/// pure function only adds the window to a given timestamp (ADR 0019 level 1 — no ambient clock).
/// </summary>
public static class PoliticaDeReprocesamiento
{
    public const int VentanaMinutos = 5;

    /// <summary>
    /// Returns null when no `fact.CommandQueue` row is pending for the document; otherwise the
    /// last pending command's `CreadoEn` plus <paramref name="ventanaMinutos"/> — the instant the
    /// reprocesar control re-enables.
    /// </summary>
    public static DateTime? VentanaBloqueo(DateTime? ultimoCreadoEnPendiente, int ventanaMinutos = VentanaMinutos) =>
        ultimoCreadoEnPendiente?.AddMinutes(ventanaMinutos);
}

/// <summary>
/// BACKLOG #13, design.md — pagination envelope math shared by the repository and the endpoint.
/// </summary>
public static class EnvelopeBandeja
{
    /// <summary>totalPaginas = ceil(totalRegistros / tamanioPagina); 0 when totalRegistros is 0.</summary>
    public static int CalcularTotalPaginas(int totalRegistros, int tamanioPagina) =>
        (int)Math.Ceiling(totalRegistros / (double)tamanioPagina);
}
