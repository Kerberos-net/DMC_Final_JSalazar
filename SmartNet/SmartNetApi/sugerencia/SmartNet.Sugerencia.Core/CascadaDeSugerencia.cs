using SmartNet.Catalogos.Core;

namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Pure 3-tier account/motivo ranking cascade (REGLAS.md §3, ADR 0011 rev. 4, design.md
/// Interfaces/Contracts + Cascade algorithm). No DB, HTTP, or clock dependency (ADR 0019),
/// enforced structurally by <c>PurityScanTests</c> (PR 2 / tasks.md Phase 6).
/// </summary>
public static class CascadaDeSugerencia
{
    public static SugerenciaDeCuenta? SugerirCuenta(
        IReadOnlyList<SugerenciaCuenta> usoDelProveedorEnElMotivo,
        IReadOnlyList<SugerenciaCuenta> usoGlobalDelMotivo,
        IReadOnlyList<CuentaContable> candidatasVigentes)
    {
        var vigentes = new HashSet<string>(
            candidatasVigentes.Select(c => c.Cuenta), StringComparer.Ordinal);

        if (vigentes.Count == 0)
        {
            return null;
        }

        var ganadorTier1 = ElegirGanador(usoDelProveedorEnElMotivo, vigentes);
        if (ganadorTier1 is not null)
        {
            return ConstruirSugerencia(ganadorTier1.Value, EscalonSugerencia.ProveedorYMotivo);
        }

        var ganadorTier2 = ElegirGanador(usoGlobalDelMotivo, vigentes);
        if (ganadorTier2 is not null)
        {
            return ConstruirSugerencia(ganadorTier2.Value, EscalonSugerencia.MotivoGlobal);
        }

        // Tier 3 — ordinal minimum re-derived internally (design.md Decision 4): an unsorted
        // caller cannot make the suggestion non-deterministic.
        var primeraCandidata = vigentes.Min(StringComparer.Ordinal)!;
        return new SugerenciaDeCuenta(
            primeraCandidata,
            EscalonSugerencia.PrimeraCandidata,
            Veces: 0,
            VecesDelAmbito: 0,
            Fundamento: $"Sin historial: primera candidata vigente por codigo ({primeraCandidata}).");
    }

    public static SugerenciaDeMotivo? SugerirMotivo(
        IReadOnlyList<SugerenciaCuenta> usoDelProveedor, IReadOnlySet<int> motivosOfrecibles)
    {
        var agregadoPorMotivo = usoDelProveedor
            .Where(f => motivosOfrecibles.Contains(f.Motivo))
            .GroupBy(f => f.Motivo)
            .Select(g => (Motivo: g.Key, Veces: g.Sum(f => f.Veces), UltimoUso: g.Max(f => f.UltimoUso)))
            .ToList();

        if (agregadoPorMotivo.Count == 0)
        {
            return null;
        }

        // VecesDelAmbito: total observations across every offerable motivo for this provider
        // (design.md Decision 3's denominator applied to the single-tier motivo cascade) — the
        // fraction shown is always "veces con este motivo" over "veces con este proveedor",
        // never just the winner's own count against itself.
        var vecesDelAmbito = agregadoPorMotivo.Sum(m => m.Veces);

        var ganador = agregadoPorMotivo
            .OrderByDescending(m => m.Veces)
            .ThenByDescending(m => m.UltimoUso)
            .ThenBy(m => m.Motivo)
            .First();

        var fundamento = $"Usado {ganador.Veces} de {vecesDelAmbito} veces con este proveedor.";

        return new SugerenciaDeMotivo(ganador.Motivo, ganador.Veces, vecesDelAmbito, fundamento);
    }

    private static (string CuentaCodigo, int Veces, int VecesDelAmbito, DateTimeOffset UltimoUso)? ElegirGanador(
        IReadOnlyList<SugerenciaCuenta> filas, HashSet<string> vigentes)
    {
        var filtradas = filas
            .Where(f => f.Veces > 0 && vigentes.Contains(f.CuentaCodigo))
            .ToList();

        if (filtradas.Count == 0)
        {
            return null;
        }

        var vecesDelAmbito = filtradas.Sum(f => f.Veces);

        var ganadora = filtradas
            .OrderByDescending(f => f.Veces)
            .ThenByDescending(f => f.UltimoUso)
            .ThenBy(f => f.CuentaCodigo, StringComparer.Ordinal)
            .First();

        return (ganadora.CuentaCodigo, ganadora.Veces, vecesDelAmbito, ganadora.UltimoUso);
    }

    private static SugerenciaDeCuenta ConstruirSugerencia(
        (string CuentaCodigo, int Veces, int VecesDelAmbito, DateTimeOffset UltimoUso) ganador,
        EscalonSugerencia escalon)
    {
        var fundamento = $"Usado {ganador.Veces} de {ganador.VecesDelAmbito} veces " +
            (escalon == EscalonSugerencia.ProveedorYMotivo
                ? "con este proveedor para este motivo."
                : "globalmente para este motivo.");

        return new SugerenciaDeCuenta(
            ganador.CuentaCodigo, escalon, ganador.Veces, ganador.VecesDelAmbito, fundamento);
    }
}
