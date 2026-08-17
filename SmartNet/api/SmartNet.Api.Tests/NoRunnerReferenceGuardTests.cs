using Mono.Cecil;

namespace SmartNet.Api.Tests;

/// <summary>
/// design.md Decision 6, the single most important test in this work unit: "The API host does not
/// reference SmartNet.Db.Runner, and must not." A runtime that can alter a shared database at
/// boot is precisely the thing item #1's permission boundary exists to prevent (ADR 0012 order:
/// schema -> API -> worker; the runner runs as the deploy principal and exits BEFORE the API ever
/// starts).
///
/// Scans the COMPILED SmartNet.Api.dll's own assembly-reference table -- same technique family as
/// SmartNet.Auth.Core.Tests' PurityScanTests (task 2.2) -- rather than trusting the .csproj's
/// ProjectReference list, which would not catch a transitive reference introduced through a
/// dependency's own dependency. This test is re-run (by construction: it runs on every `dotnet
/// test`) after every later task in this phase as a regression guard, not just once at the start
/// (task 4.3).
/// </summary>
public class NoRunnerReferenceGuardTests
{
    private static readonly string ApiAssemblyPath = Path.Combine(
        AppContext.BaseDirectory, "SmartNet.Api.dll");

    [Fact]
    public void ApiAssembly_HasNoReferenceToDbRunner_DirectOrTransitive()
    {
        using var module = ModuleDefinition.ReadModule(ApiAssemblyPath);

        var offendingReferences = module.AssemblyReferences
            .Where(reference => reference.Name.StartsWith("SmartNet.Db.Runner", StringComparison.Ordinal))
            .Select(reference => reference.Name)
            .ToList();

        Assert.True(offendingReferences.Count == 0,
            "SmartNet.Api must never reference SmartNet.Db.Runner, direct or transitive " +
            "(design.md Decision 6) -- a web host that can alter schema at boot defeats ADR 0012's " +
            "deploy-then-serve ordering. Offending references: " + string.Join(", ", offendingReferences));
    }
}
