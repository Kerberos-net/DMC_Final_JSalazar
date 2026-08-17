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
}
