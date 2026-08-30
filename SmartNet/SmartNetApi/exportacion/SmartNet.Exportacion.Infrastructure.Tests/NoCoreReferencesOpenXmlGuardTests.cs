using System.Xml.Linq;

namespace SmartNet.Exportacion.Infrastructure.Tests;

/// <summary>
/// tasks.md PR1 task 1.2 / ADR 0021 decision 2: no <c>*.Core</c> project may reference
/// <c>DocumentFormat.OpenXml</c>, directly or transitively. Same mechanism family as
/// <c>SmartNet.Api.Tests/NoRunnerReferenceGuardTests</c> (structural, not a comment): here it walks
/// every <c>*.Core.csproj</c> project-reference closure and asserts the package never appears.
/// The accounting core stays infrastructure-free (ADR 0019); a misplaced package would break that
/// by transitivity where <c>PurityScanTests</c> — which scans code, not the package graph — cannot.
/// </summary>
public sealed class NoCoreReferencesOpenXmlGuardTests
{
    private const string PaqueteProhibido = "DocumentFormat.OpenXml";

    [Fact]
    public void NingunProyectoCore_ReferenciaDocumentFormatOpenXml_DirectaOTransitivamente()
    {
        var raizApi = LocalizarRaizSmartNetApi();
        var proyectosCore = Directory
            .EnumerateFiles(raizApi, "*.Core.csproj", SearchOption.AllDirectories)
            .ToList();

        Assert.NotEmpty(proyectosCore); // guard against a silently empty scan

        var infractores = new List<string>();
        foreach (var proyectoCore in proyectosCore)
        {
            var cadena = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ReferenciaPaquete(proyectoCore, PaqueteProhibido, cadena, visitados: new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                infractores.Add($"{Path.GetFileNameWithoutExtension(proyectoCore)} -> {string.Join(" -> ", cadena)}");
            }
        }

        Assert.True(
            infractores.Count == 0,
            $"Un proyecto *.Core alcanza {PaqueteProhibido} (ADR 0021 decision 2 / ADR 0019): "
                + string.Join(" | ", infractores));
    }

    private static bool ReferenciaPaquete(
        string csprojPath,
        string paquete,
        HashSet<string> cadena,
        HashSet<string> visitados)
    {
        var completo = Path.GetFullPath(csprojPath);
        if (!visitados.Add(completo) || !File.Exists(completo))
        {
            return false;
        }

        var documento = XDocument.Load(completo);

        var referenciaDirecta = documento
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Any(e => string.Equals((string?)e.Attribute("Include"), paquete, StringComparison.OrdinalIgnoreCase));

        if (referenciaDirecta)
        {
            cadena.Add($"{Path.GetFileNameWithoutExtension(completo)} (PackageReference)");
            return true;
        }

        var carpeta = Path.GetDirectoryName(completo)!;
        foreach (var projRef in documento.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var rel = (string?)projRef.Attribute("Include");
            if (string.IsNullOrWhiteSpace(rel))
            {
                continue;
            }

            var destino = Path.GetFullPath(Path.Combine(carpeta, rel.Replace('\\', Path.DirectorySeparatorChar)));
            if (ReferenciaPaquete(destino, paquete, cadena, visitados))
            {
                cadena.Add(Path.GetFileNameWithoutExtension(completo));
                return true;
            }
        }

        return false;
    }

    private static string LocalizarRaizSmartNetApi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidato = Path.Combine(dir.FullName, "SmartNet", "SmartNetApi");
            if (Directory.Exists(candidato) && File.Exists(Path.Combine(candidato, "SmartNet.sln")))
            {
                return candidato;
            }

            if (dir.Name == "SmartNetApi" && File.Exists(Path.Combine(dir.FullName, "SmartNet.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz SmartNet/SmartNetApi (con SmartNet.sln).");
    }
}
