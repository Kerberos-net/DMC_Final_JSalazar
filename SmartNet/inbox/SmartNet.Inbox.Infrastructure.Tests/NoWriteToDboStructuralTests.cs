using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Task 3.10 -- unlike item #3's external-catalog adapters (SELECT-only against every `dbo.*` table)
/// or item #6's `SmartNet.TiposCambio.Infrastructure` (zero `dbo.` involvement at all),
/// <see cref="SqlPromocionRepository"/> legitimately reads ONE `dbo.*` table
/// (`dbo.Proveedor`, ADR 0003's "clase externa") while also writing its own `fact.*` tables in the
/// same file. The check here is therefore narrower than both prior variants: every SQL text block
/// that mentions `dbo.` must be a bare SELECT -- never wrapped in a write verb.
/// </summary>
public sealed class NoWriteToDboStructuralTests
{
    private const string AdapterFileName = "SqlPromocionRepository.cs";

    private static readonly Regex TripleQuotedBlock = new(
        "\"\"\"(?<body>.*?)\"\"\"", RegexOptions.Singleline | RegexOptions.Compiled);

    // A single-line `CommandText = "SELECT ... dbo. ...";` never uses a triple-quoted raw string,
    // so it needs its own, narrower match: the double-quoted literal itself.
    private static readonly Regex SingleLineStringLiteral = new(
        "\"(?<body>[^\"]*)\"", RegexOptions.Compiled);

    [Fact]
    public void EveryDboReferencingSqlBlock_IsSelectOnly()
    {
        var path = Path.Combine(InfrastructureProjectDirectory(), AdapterFileName);
        Assert.True(File.Exists(path), $"Expected adapter source file at {path}");

        var content = File.ReadAllText(path);
        var blocksWithDbo = TripleQuotedBlock.Matches(content)
            .Select(m => m.Groups["body"].Value)
            .Concat(SingleLineStringLiteral.Matches(content).Select(m => m.Groups["body"].Value))
            .Where(body => body.Contains("dbo.", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        Assert.NotEmpty(blocksWithDbo); // sanity: this adapter does query dbo.Proveedor somewhere.
        foreach (var body in blocksWithDbo)
        {
            var trimmed = body.TrimStart();
            Assert.StartsWith("SELECT", trimmed, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoAdapterFile_IssuesAnInsertUpdateOrDeleteAgainstDboDirectly()
    {
        var infrastructureSourceDirectory = InfrastructureProjectDirectory();
        var writeVerb = new Regex(
            @"(INSERT\s+INTO\s+dbo\.|UPDATE\s+dbo\.|DELETE\s+FROM\s+dbo\.)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var file in Directory.GetFiles(infrastructureSourceDirectory, "*.cs"))
        {
            var codeOnly = string.Join(
                '\n',
                File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//")));
            var match = writeVerb.Match(codeOnly);

            Assert.False(
                match.Success,
                $"{Path.GetFileName(file)} writes to a dbo.* table ('{match.Value}') — nobody writes an external " +
                "table (ADR 0003).");
        }
    }

    private static string InfrastructureProjectDirectory([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFilePath)!, "..", "SmartNet.Inbox.Infrastructure"));
}
