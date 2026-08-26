namespace SmartNet.Catalogos.Core;

/// <summary>
/// Pure prefix resolution (REGLAS.md §3, design.md Decision 1 / Interfaces-Contracts). "El
/// motivo declara prefijos, no cuentas" — no DB, no HTTP, no clock (ADR 0019), enforced by
/// <c>PurityScanTests</c>.
/// </summary>
public static class ResolucionDePrefijos
{
    // Split por coma, trim, descarta vacíos, deduplica (ordinal). null/"" → lista vacía.
    public static IReadOnlyList<string> ParsearPrefijos(string? prefijosDeclarados)
    {
        if (string.IsNullOrEmpty(prefijosDeclarados))
        {
            return Array.Empty<string>();
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var prefijos = new List<string>();

        foreach (var token in prefijosDeclarados.Split(','))
        {
            var prefijo = token.Trim();

            if (prefijo.Length == 0)
            {
                continue;
            }

            if (vistos.Add(prefijo))
            {
                prefijos.Add(prefijo);
            }
        }

        return prefijos;
    }

    // Hojas cuyo código empieza por algún prefijo. Deduplicadas por código y ordenadas
    // ascendente ordinal: REGLAS.md §3 escalón 3 ("la primera candidata") exige orden
    // determinista. Filtra hojas internamente (design.md Decision 1) — el caller nunca
    // pre-filtra, la regla "solo hojas imputan" es contenido contable, no de query.
    public static IReadOnlyList<CuentaContable> ResolverCandidatas(
        string? prefijosDeclarados, IReadOnlyList<CuentaContable> planDeCuentas)
    {
        var prefijos = ParsearPrefijos(prefijosDeclarados);

        if (prefijos.Count == 0)
        {
            return Array.Empty<CuentaContable>();
        }

        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var candidatas = new List<CuentaContable>();

        foreach (var cuenta in planDeCuentas)
        {
            if (!cuenta.EsHojaImputable)
            {
                continue;
            }

            if (!vistas.Contains(cuenta.Cuenta) &&
                prefijos.Any(prefijo => cuenta.Cuenta.StartsWith(prefijo, StringComparison.Ordinal)))
            {
                vistas.Add(cuenta.Cuenta);
                candidatas.Add(cuenta);
            }
        }

        candidatas.Sort((a, b) => string.CompareOrdinal(a.Cuenta, b.Cuenta));

        return candidatas;
    }

    public static bool EsCandidata(string cuentaCodigo, string? prefijosDeclarados,
        IReadOnlyList<CuentaContable> planDeCuentas) =>
        ResolverCandidatas(prefijosDeclarados, planDeCuentas)
            .Any(candidata => candidata.Cuenta == cuentaCodigo);
}
