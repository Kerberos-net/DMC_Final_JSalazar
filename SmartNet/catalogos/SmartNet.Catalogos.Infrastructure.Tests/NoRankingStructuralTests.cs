using System.Reflection;
using System.Text.RegularExpressions;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 3.9/3.10 -- spec.md's "no single best suggestion" scenario (design.md Decision 2, item
/// #9's job, not this one's). Reflection over <see cref="ISugerenciaCuentaRepository"/>'s members:
/// no method name suggests ranking, sorting, or selecting one preferred candidate. This is a
/// by-construction confirmation, not a RED-first task -- <see cref="ISugerenciaCuentaRepository"/>
/// already existed from Phase 1 (WU1) with exactly the 4 storage-only methods design.md specifies;
/// there is nothing to change to make it pass, only to confirm.
/// </summary>
public sealed class NoRankingStructuralTests
{
    private static readonly Regex RankingShapedName = new(
        "Mejor|Best|Preferida|Preferred|Ordenar|Sort|Rank|Elegir|Select(?!ed$)|Top",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void ISugerenciaCuentaRepository_DeclaresNoMethod_ThatRanksSortsOrSelectsOneCandidate()
    {
        var members = typeof(ISugerenciaCuentaRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var suspects = members.Where(m => RankingShapedName.IsMatch(m.Name)).ToList();

        Assert.True(
            suspects.Count == 0,
            $"ISugerenciaCuentaRepository declares ranking/selection-shaped member(s): " +
            string.Join(", ", suspects.Select(m => m.Name)));

        // The 4 methods design.md specifies, no more, no fewer -- storage access only.
        Assert.Equal(4, members.Length);
        Assert.Contains(members, m => m.Name == nameof(ISugerenciaCuentaRepository.ListarPorProveedorYMotivoAsync));
        Assert.Contains(members, m => m.Name == nameof(ISugerenciaCuentaRepository.ListarPorMotivoAsync));
        Assert.Contains(members, m => m.Name == nameof(ISugerenciaCuentaRepository.ListarPorProveedorAsync));
        Assert.Contains(members, m => m.Name == nameof(ISugerenciaCuentaRepository.RegistrarUsoAsync));
    }

    /// <summary>
    /// tasks.md (item #9, Phase 6.3): extends this guard — never weakens it (the assertion above
    /// stays byte-for-byte) — to the whole <c>SmartNet.Catalogos.Core.dll</c> assembly, not just
    /// <see cref="ISugerenciaCuentaRepository"/>'s members. design.md Decision 1: item #9's cascade
    /// lives in a separate <c>SmartNet.Sugerencia.Core</c> assembly precisely so this boundary can
    /// be checked structurally — if a ranking-shaped type ever lands back in Catalogos.Core, this
    /// fails even though the narrower interface-only check above would not catch it.
    /// </summary>
    [Fact]
    public void CatalogosCore_DeclaresNoType_ThatIsRankingOrSelectionShaped()
    {
        var catalogosCoreAssembly = typeof(ISugerenciaCuentaRepository).Assembly;

        var suspects = catalogosCoreAssembly.GetTypes()
            .Where(t => t.IsPublic && RankingShapedName.IsMatch(t.Name))
            .Select(t => t.Name)
            .ToList();

        Assert.True(
            suspects.Count == 0,
            "SmartNet.Catalogos.Core.dll declares ranking/selection-shaped public type(s) — that " +
            "logic belongs to SmartNet.Sugerencia.Core (item #9, design.md Decision 1): " +
            string.Join(", ", suspects));
    }
}
