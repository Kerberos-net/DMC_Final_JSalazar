using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 2.11/2.12 -- spec.md's "No SQL adapter writes to a dbo.* table" scenario (ADR 0003).
/// Two checks: (1) none of the 5 external-catalog port interfaces declares a write-shaped method;
/// (2) a literal scan of each of the 5 adapters' own SQL command text confirms no
/// INSERT/UPDATE/DELETE keyword appears anywhere in the file.
/// </summary>
public sealed class NoWriteToDboStructuralTests
{
    private static readonly Type[] ExternalCatalogPorts =
    {
        typeof(ICuentaContableRepository),
        typeof(IMotivoRepository),
        typeof(IProveedorRepository),
        typeof(IOrigenRepository),
        typeof(IDocumentoIdentidadRepository),
    };

    private static readonly string[] AdapterSourceFileNames =
    {
        "SqlCuentaContableRepository.cs",
        "SqlMotivoRepository.cs",
        "SqlProveedorRepository.cs",
        "SqlOrigenRepository.cs",
        "SqlDocumentoIdentidadRepository.cs",
    };

    private static readonly Regex WriteVerb = new(
        @"\b(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoneOfTheFiveExternalCatalogInterfaces_DeclaresAnInsertUpdateOrDeleteMethod()
    {
        foreach (var port in ExternalCatalogPorts)
        {
            var writeShapedMembers = port.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => Regex.IsMatch(m.Name, "Insert|Update|Delete|Eliminar|Actualizar|Guardar", RegexOptions.IgnoreCase))
                .ToList();

            Assert.True(
                writeShapedMembers.Count == 0,
                $"{port.Name} declares write-shaped member(s): {string.Join(", ", writeShapedMembers.Select(m => m.Name))}");
        }
    }

    [Fact]
    public void NoneOfTheFiveAdapters_IssuesAnInsertUpdateOrDeleteStatement()
    {
        var infrastructureSourceDirectory = InfrastructureProjectDirectory();

        foreach (var fileName in AdapterSourceFileNames)
        {
            var path = Path.Combine(infrastructureSourceDirectory, fileName);
            Assert.True(File.Exists(path), $"Expected adapter source file at {path}");

            // Only the SQL command text matters — strip comment lines (`//`, `///`) first so a
            // doc comment mentioning "INSERT/UPDATE/DELETE" in prose (as this adapter's own header
            // does) is not mistaken for an actual write statement.
            var codeOnly = string.Join(
                '\n',
                File.ReadLines(path).Where(line => !line.TrimStart().StartsWith("//")));
            var match = WriteVerb.Match(codeOnly);

            Assert.False(
                match.Success,
                $"{fileName} contains a write verb '{match.Value}' at index {match.Index} — external-catalog " +
                "adapters must be SELECT-only (ADR 0003).");
        }
    }

    private static string InfrastructureProjectDirectory([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFilePath)!, "..", "SmartNet.Catalogos.Infrastructure"));
}
