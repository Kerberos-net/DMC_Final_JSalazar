namespace SmartNet.Auth.Core;

/// <summary>
/// The heart. Pure, static, allocation-cheap, fully deterministic (design.md Decision 5 / ADR 0019).
/// </summary>
public static class AccessPolicy
{
    /// <summary>
    /// design.md Login sequence step 2: locked iff <c>BloqueadoHasta</c> is strictly in the future
    /// relative to <paramref name="ahora"/>. <c>null</c> or a past value is not locked.
    /// </summary>
    public static AccessDecision Evaluate(UsuarioCredentialState estado, DateTimeOffset ahora) =>
        estado.BloqueadoHasta is { } bloqueadoHasta && bloqueadoHasta > ahora
            ? AccessDecision.Locked
            : AccessDecision.Allowed;

    /// <summary>
    /// design.md Decision 8, "ApplyFailure, precisely". Reachable only for a failure evaluated
    /// while NOT locked (Evaluate already rejects a locked account before any hash is checked —
    /// design.md Login sequence step 2 — so this is unreachable during a live lock).
    /// </summary>
    public static UsuarioCredentialState ApplyFailure(
        UsuarioCredentialState estado, LockoutPolicy politica, DateTimeOffset ahora)
    {
        var intentosFallidos = estado.IntentosFallidos + 1;

        if (intentosFallidos < politica.UmbralFallos)
        {
            return estado with { IntentosFallidos = intentosFallidos };
        }

        // Arm the lock, in this exact order (design.md Decision 8): read NivelBloqueo BEFORE
        // the saturating increment — this is what makes the first lock 15 minutes, not 30.
        var nivelBloqueoActual = Math.Min(estado.NivelBloqueo, politica.NivelMaximo);
        var duracion = politica.DuracionBase * Math.Pow(politica.Factor, nivelBloqueoActual);

        return estado with
        {
            IntentosFallidos = 0,
            NivelBloqueo = Math.Min(estado.NivelBloqueo + 1, politica.NivelMaximo),
            BloqueadoHasta = ahora + duracion,
        };
    }

    /// <summary>
    /// ADR 0007 Revisión 4: a success clears the escalation entirely, all three fields — proof of
    /// credential possession ends the guessing hypothesis (design.md Decision 8).
    /// </summary>
    public static UsuarioCredentialState ApplySuccess(UsuarioCredentialState estado) =>
        estado with { IntentosFallidos = 0, BloqueadoHasta = null, NivelBloqueo = 0 };
}
