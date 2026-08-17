namespace SmartNet.Auth.Core;

/// <summary>
/// ADR 0007's numbers, in one place, so every spec.md scenario maps to one file
/// (design.md Decision 5 / Decision 8).
/// </summary>
public sealed record LockoutPolicy(
    int UmbralFallos, // 5   — ADR 0007, "cinco fallos consecutivos"
    TimeSpan DuracionBase, // 15 min — ADR 0007
    int Factor, // 2   — duplicando desde la base (ADR 0007 Revisión 4)
    int NivelMaximo) // 3   — techo en 120 min (ADR 0007 Revisión 4 / design.md Decision 8)
{
    public static LockoutPolicy Adr0007 { get; } = new(5, TimeSpan.FromMinutes(15), 2, 3);
}
