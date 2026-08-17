using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// spec.md, "The domain-core assembly does not reference infrastructure types directly" /
/// ADR 0019 ("núcleo sin dependencias de base de datos, HTTP ni reloj") /
/// design.md Decision 5 ("SmartNet.Auth.Core: classlib, net10.0 — ZERO infrastructure package
/// references").
///
/// Mechanism chosen (task 2.2), and why:
///   - <see cref="NetArchTest.Rules"/> for the assembly-reference checks (does the compiled
///     assembly's reference table mention System.Data.SqlClient, Microsoft.Data.SqlClient, or
///     any Microsoft.AspNetCore.* assembly). NetArchTest wraps Mono.Cecil already and gives a
///     declarative, low-noise API for "assembly does not have dependency on X" — exactly the
///     shape of spec's scenario.
///   - Direct <see cref="Mono.Cecil"/> IL inspection for the DateTime.Now / DateTime.UtcNow
///     call sites. NetArchTest has no primitive for "does not call member X on type Y"; a
///     reference-table check would not catch this because System.Private.CoreLib is always
///     referenced (every type ultimately derives from System.Object). Scanning method bodies'
///     IL for `callvirt`/`call` instructions targeting `System.DateTime::get_Now` or
///     `System.DateTime::get_UtcNow` is a real, automated, CI-runnable check against the actual
///     compiled bytes — not a naming convention, not a code-review promise, not a source-text
///     grep (which a `// DateTime.Now` comment or a string literal would falsely trip, and which
///     a call routed through an alias would falsely miss).
///
/// This test runs against the COMPILED assembly (dotnet build output), so it is re-run at task
/// 2.17 against the complete core, not just this empty-project baseline (task 2.3).
/// </summary>
public class PurityScanTests
{
    private static readonly string CoreAssemblyPath = Path.Combine(
        AppContext.BaseDirectory, "SmartNet.Auth.Core.dll");

    private static Assembly LoadCoreAssemblyForNetArchTest() =>
        Assembly.LoadFrom(CoreAssemblyPath);

    [Fact]
    public void DomainCore_DoesNotReferenceSystemDataSqlClient()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("System.Data.SqlClient")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Auth.Core must not reference System.Data.SqlClient (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_DoesNotReferenceMicrosoftDataSqlClient()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("Microsoft.Data.SqlClient")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Auth.Core must not reference Microsoft.Data.SqlClient (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_DoesNotReferenceMicrosoftAspNetCore()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Auth.Core must not reference any Microsoft.AspNetCore.* type (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_HasNoReferencedAssemblyNamedForSqlClientOrAspNetCore()
    {
        using var module = ModuleDefinition.ReadModule(CoreAssemblyPath);

        var disallowedPrefixes = new[]
        {
            "System.Data.SqlClient",
            "Microsoft.Data.SqlClient",
            "Microsoft.AspNetCore",
        };

        var offendingReferences = module.AssemblyReferences
            .Where(reference => disallowedPrefixes.Any(prefix =>
                reference.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(reference => reference.Name)
            .ToList();

        Assert.True(offendingReferences.Count == 0,
            "Referenced assemblies include disallowed infrastructure types: " +
            string.Join(", ", offendingReferences));
    }

    [Fact]
    public void DomainCore_DoesNotCallDateTimeNowOrUtcNowDirectly()
    {
        using var module = ModuleDefinition.ReadModule(
            CoreAssemblyPath,
            new ReaderParameters { ReadSymbols = false });

        var offendingCallSites = new List<string>();

        foreach (var type in module.Types)
        {
            ScanTypeForAmbientClockCalls(type, offendingCallSites);
        }

        Assert.True(offendingCallSites.Count == 0,
            "SmartNet.Auth.Core must not call DateTime.Now/DateTime.UtcNow directly — " +
            "time MUST be received as a parameter (TimeProvider), per ADR 0019 / spec.md. " +
            "Offending call sites: " + string.Join(", ", offendingCallSites));
    }

    private static void ScanTypeForAmbientClockCalls(TypeDefinition type, List<string> offendingCallSites)
    {
        foreach (var nested in type.NestedTypes)
        {
            ScanTypeForAmbientClockCalls(nested, offendingCallSites);
        }

        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference calledMethod)
                {
                    continue;
                }

                var declaringTypeName = calledMethod.DeclaringType?.FullName;
                var isAmbientClockCall =
                    declaringTypeName == "System.DateTime" &&
                    (calledMethod.Name == "get_Now" || calledMethod.Name == "get_UtcNow");

                if (isAmbientClockCall)
                {
                    offendingCallSites.Add($"{type.FullName}.{method.Name} -> {declaringTypeName}.{calledMethod.Name}");
                }
            }
        }
    }
}
