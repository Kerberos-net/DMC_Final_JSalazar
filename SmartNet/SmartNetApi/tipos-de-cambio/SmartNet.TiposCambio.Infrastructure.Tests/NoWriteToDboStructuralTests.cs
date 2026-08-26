using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartNet.TiposCambio.Infrastructure.Tests;

/// <summary>
/// Task 2.6 -- analog of item #3's <c>NoWriteToDboStructuralTests</c>. Unlike the catalog adapters
/// (SELECT-only against `dbo.*`), <see cref="SqlTipoCambioRepository"/> both reads and writes
/// `fact.TipoCambio` -- there is no `dbo.*` involvement here at all (ADR 0003: `fact.TipoCambio`
/// is this project's own table, not an external catalog). The check is therefore stricter than
/// item #3's "no write verb": a literal, comment-stripped scan confirming the adapter's source
/// never mentions `dbo.` anywhere (same comment-stripping fix as item #3's task 2.11, applied here
/// to a broader forbidden token).
/// </summary>
public sealed class NoWriteToDboStructuralTests
{
    private const string AdapterFileName = "SqlTipoCambioRepository.cs";

    private static readonly Regex DboMention = new(
        @"\bdbo\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void TheAdapter_NeverMentionsDbo()
    {
        var infrastructureSourceDirectory = InfrastructureProjectDirectory();
        var path = Path.Combine(infrastructureSourceDirectory, AdapterFileName);
        Assert.True(File.Exists(path), $"Expected adapter source file at {path}");

        // Only code matters -- strip comment-only lines (`//`, `///`) first so a doc comment
        // mentioning "dbo." in prose is not mistaken for actual SQL/reference.
        var codeOnly = string.Join(
            '\n',
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("//")));
        var match = DboMention.Match(codeOnly);

        Assert.False(
            match.Success,
            $"{AdapterFileName} mentions 'dbo.' at index {match.Index} — the adapter must operate " +
            "only on fact.TipoCambio (ADR 0003).");
    }

    private static string InfrastructureProjectDirectory([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFilePath)!, "..", "SmartNet.TiposCambio.Infrastructure"));
}
