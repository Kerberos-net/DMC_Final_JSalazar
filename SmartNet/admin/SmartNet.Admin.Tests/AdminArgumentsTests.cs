namespace SmartNet.Admin.Tests;

/// <summary>
/// Task 5.2/5.3 -- the argument parser's recognized flag surface, per verb. The load-bearing
/// assertion is <see cref="NoVerb_HasAPasswordBearingFlag"/>: it enumerates every flag every verb
/// will ever read from <c>argv</c> and proves none of them is password-shaped. argv lands in shell
/// history, in <c>ps</c>, and in Windows process-creation audit records -- this test is the guard
/// against a future verb quietly growing a <c>--clave</c>/<c>--password</c> parameter.
/// </summary>
public sealed class AdminArgumentsTests
{
    [Fact]
    public void NoVerb_HasAPasswordBearingFlag()
    {
        var todasLasBanderas = AdminArguments.RecognizedFlagsByVerb.Values.SelectMany(flags => flags);

        foreach (var bandera in todasLasBanderas)
        {
            Assert.False(
                bandera.Contains("clave", StringComparison.OrdinalIgnoreCase) ||
                bandera.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                bandera.Contains("contrase", StringComparison.OrdinalIgnoreCase) ||
                bandera.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                bandera.Contains("pwd", StringComparison.OrdinalIgnoreCase),
                $"La bandera '{bandera}' parece transportar una contraseña por argv.");
        }
    }

    [Fact]
    public void RecognizedFlagsByVerb_CoversExactlyTheThreeShippedVerbs()
    {
        Assert.Equal(
            new[] { "sesion purgar", "usuario crear", "usuario restablecer-clave" },
            AdminArguments.RecognizedFlagsByVerb.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Parse_UsuarioCrear_ParsesNombre()
    {
        var comando = AdminArguments.Parse(["usuario", "crear", "--nombre", "usr_prueba"]);

        var crear = Assert.IsType<AdminCommand.UsuarioCrear>(comando);
        Assert.Equal("usr_prueba", crear.NombreUsuario);
    }

    [Fact]
    public void Parse_UsuarioRestablecerClave_ParsesNombre()
    {
        var comando = AdminArguments.Parse(["usuario", "restablecer-clave", "--nombre", "usr_prueba"]);

        var restablecer = Assert.IsType<AdminCommand.UsuarioRestablecerClave>(comando);
        Assert.Equal("usr_prueba", restablecer.NombreUsuario);
    }

    [Fact]
    public void Parse_SesionPurgar_ParsesRetencionDias()
    {
        var comando = AdminArguments.Parse(["sesion", "purgar", "--retencion-dias", "90"]);

        var purgar = Assert.IsType<AdminCommand.SesionPurgar>(comando);
        Assert.Equal(90, purgar.RetencionDias);
    }

    // The one decision made explicitly for task 5.8: --retencion-dias is REQUIRED, no hardcoded
    // default. Absence fails parsing -- Program.cs then prints AdminArguments.Usage and exits 1,
    // the same "no default, no committed fallback" discipline as SMARTNET_API_DB_CONNECTION.
    [Fact]
    public void Parse_SesionPurgar_ReturnsNull_WhenRetencionDiasIsMissing()
    {
        var comando = AdminArguments.Parse(["sesion", "purgar"]);

        Assert.Null(comando);
    }

    [Fact]
    public void Parse_SesionPurgar_ReturnsNull_WhenRetencionDiasIsNotAPositiveInteger()
    {
        Assert.Null(AdminArguments.Parse(["sesion", "purgar", "--retencion-dias", "abc"]));
        Assert.Null(AdminArguments.Parse(["sesion", "purgar", "--retencion-dias", "0"]));
        Assert.Null(AdminArguments.Parse(["sesion", "purgar", "--retencion-dias", "-5"]));
    }

    [Fact]
    public void Parse_UsuarioCrear_ReturnsNull_WhenNombreIsMissing()
    {
        Assert.Null(AdminArguments.Parse(["usuario", "crear"]));
    }

    [Fact]
    public void Parse_ReturnsNull_ForAnUnknownVerb()
    {
        Assert.Null(AdminArguments.Parse(["sesion", "eliminar"]));
        Assert.Null(AdminArguments.Parse([]));
        Assert.Null(AdminArguments.Parse(["usuario"]));
    }
}
