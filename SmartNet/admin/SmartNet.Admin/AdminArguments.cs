namespace SmartNet.Admin;

/// <summary>
/// Command-line parsing for <c>smartnet-admin</c> (design.md Decision 7). Mirrors
/// <c>SmartNet.Db.Runner.RunnerOptions</c>'s convention: <see cref="Parse"/> returns
/// <see langword="null"/> on any invalid or incomplete invocation, and the caller prints
/// <see cref="Usage"/> and exits non-zero — never a silent default.
///
/// <see cref="RecognizedFlagsByVerb"/> is the complete, single-source-of-truth flag surface for
/// every verb (task 5.2's regression guard reads it directly): NO verb ever recognizes a
/// password-bearing flag. The password is always read afterward, interactively, via
/// <see cref="IPasswordPrompt"/> — never from <c>argv</c>, which lands in shell history, `ps`, and
/// Windows process-creation audit records.
/// </summary>
public static class AdminArguments
{
    public const string Usage =
        "Uso:\n" +
        "  smartnet-admin usuario crear             --nombre <u>\n" +
        "  smartnet-admin usuario restablecer-clave --nombre <u>\n" +
        "  smartnet-admin sesion  purgar             --retencion-dias <n>\n" +
        "\n" +
        "La contraseña NUNCA se pasa como argumento: se solicita de forma interactiva, sin eco, " +
        "por la entrada estándar.";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RecognizedFlagsByVerb =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["usuario crear"] = new[] { "--nombre" },
            ["usuario restablecer-clave"] = new[] { "--nombre" },
            ["sesion purgar"] = new[] { "--retencion-dias" },
        };

    public static AdminCommand? Parse(string[] args)
    {
        if (args.Length < 2)
        {
            return null;
        }

        var grupo = args[0];
        var verbo = args[1];
        var banderas = ParseFlags(args, startIndex: 2);

        return (grupo, verbo) switch
        {
            ("usuario", "crear") => ParseUsuarioCrear(banderas),
            ("usuario", "restablecer-clave") => ParseUsuarioRestablecerClave(banderas),
            ("sesion", "purgar") => ParseSesionPurgar(banderas),
            _ => null,
        };
    }

    private static AdminCommand? ParseUsuarioCrear(IReadOnlyDictionary<string, string> banderas) =>
        banderas.TryGetValue("--nombre", out var nombre) && !string.IsNullOrWhiteSpace(nombre)
            ? new AdminCommand.UsuarioCrear(nombre)
            : null;

    private static AdminCommand? ParseUsuarioRestablecerClave(IReadOnlyDictionary<string, string> banderas) =>
        banderas.TryGetValue("--nombre", out var nombre) && !string.IsNullOrWhiteSpace(nombre)
            ? new AdminCommand.UsuarioRestablecerClave(nombre)
            : null;

    // 5.8's explicit decision: REQUIRED, no hardcoded default. Absence (or a non-positive value)
    // returns null; the caller then prints Usage and exits 1 -- exactly RunnerOptions' own shape
    // for SMARTNET_DB_CONNECTION.
    private static AdminCommand? ParseSesionPurgar(IReadOnlyDictionary<string, string> banderas) =>
        banderas.TryGetValue("--retencion-dias", out var valor) &&
        int.TryParse(valor, out var retencionDias) &&
        retencionDias > 0
            ? new AdminCommand.SesionPurgar(retencionDias)
            : null;

    private static Dictionary<string, string> ParseFlags(string[] args, int startIndex)
    {
        var banderas = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = startIndex; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                banderas[args[i]] = args[++i];
            }
        }

        return banderas;
    }
}
