using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// spec.md "MUST be pure — no DB, HTTP, or clock dependency (ADR 0019)" / design.md
/// "SmartNet.Facturacion.Core ... pure, PurityScan-guarded".
///
/// Copy of SmartNet.Sugerencia.Core.Tests/PurityScanTests.cs (itself a copy of
/// SmartNet.Contable.Core.Tests/PurityScanTests.cs), retargeted at SmartNet.Facturacion.Core.dll.
/// The one ProjectReference (Contable.Core) is itself pure, so this scan stays meaningful
/// transitively. tasks.md Phase 1, tasks 1.7/1.8.
/// </summary>
public class PurityScanTests
{
    private static readonly string CoreAssemblyPath = Path.Combine(
        AppContext.BaseDirectory, "SmartNet.Facturacion.Core.dll");

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
            "SmartNet.Facturacion.Core must not reference System.Data.SqlClient (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_DoesNotReferenceMicrosoftDataSqlClient()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("Microsoft.Data.SqlClient")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Facturacion.Core must not reference Microsoft.Data.SqlClient (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_DoesNotReferenceMicrosoftAspNetCore()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Facturacion.Core must not reference any Microsoft.AspNetCore.* type (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_DoesNotReferenceSystemNetHttp()
    {
        var result = Types.InAssembly(LoadCoreAssemblyForNetArchTest())
            .Should()
            .NotHaveDependencyOn("System.Net.Http")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "SmartNet.Facturacion.Core must not reference System.Net.Http (ADR 0019 / spec.md).");
    }

    [Fact]
    public void DomainCore_HasNoReferencedAssemblyNamedForSqlClientAspNetCoreOrHttp()
    {
        using var module = ModuleDefinition.ReadModule(CoreAssemblyPath);

        var disallowedPrefixes = new[]
        {
            "System.Data.SqlClient",
            "Microsoft.Data.SqlClient",
            "Microsoft.AspNetCore",
            "System.Net.Http",
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
            "SmartNet.Facturacion.Core must not call DateTime.Now/DateTime.UtcNow directly — " +
            "time MUST be received as a parameter, per ADR 0019 / spec.md. " +
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
