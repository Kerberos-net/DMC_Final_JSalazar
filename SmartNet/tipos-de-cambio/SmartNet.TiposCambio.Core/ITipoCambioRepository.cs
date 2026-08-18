namespace SmartNet.TiposCambio.Core;

/// <summary>Outcome of a MANUAL rate load (design.md Decision 3 — PK enforces duplicates, the adapter only translates).</summary>
public enum ResultadoCargaManual
{
    Cargada,
    YaExistia,
}

/// <summary>
/// Port over <c>fact.TipoCambio</c> (design.md Interfaces/Contracts). Implementation lives in
/// <c>SmartNet.TiposCambio.Infrastructure</c> (Phase 2). Consumers: #8 (freezes <c>Venta</c>),
/// #11 (409 and manual load).
/// </summary>
public interface ITipoCambioRepository
{
    Task<ResultadoTipoCambio> ObtenerVigenteAsync(DateOnly fecha, CancellationToken ct);

    /// <summary>
    /// Inserts a MANUAL row (design.md Decision 4 — the adapter hardcodes <c>Origen='MANUAL'</c>,
    /// no <see cref="OrigenTipoCambio"/> parameter here; enforces the ADR 0003 partition in the
    /// signature, not in a comment). <paramref name="fechaConsulta"/> is received as a parameter,
    /// never the ambient clock (ADR 0019) — same rule as <c>RegistrarUsoAsync</c> in item #3.
    /// </summary>
    Task<ResultadoCargaManual> CargarManualAsync(
        DateOnly fecha,
        decimal compra,
        decimal venta,
        DateTime fechaConsulta,
        long? cargadoPorUsuarioId,
        CancellationToken ct);
}
