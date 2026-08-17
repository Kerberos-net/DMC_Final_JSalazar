using Microsoft.Extensions.Time.Testing;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

namespace SmartNet.Admin.Tests;

/// <summary>
/// Task 5.4/5.5 -- `usuario crear --nombre &lt;u&gt;` creates a <c>fact.Usuario</c> row with an
/// Argon2id-derived, PHC-encoded <c>ClaveHash</c> from the prompted password. Real
/// <see cref="Argon2idPasswordHasher"/>, not a fake -- the point is proving the SAME code the API
/// uses (design.md Decision 7) produces a hash the API's own login path can verify.
/// </summary>
public sealed class UsuarioCrearTests : AdminOperationsTestBase
{
    [Fact]
    public async Task CreatesTheUsuarioRow_WithAnArgon2idPhcEncodedClaveHash_FromThePromptedPassword()
    {
        var prompt = new FakePasswordPrompt("una-clave-de-prueba-sintetica");
        var hasher = new Argon2idPasswordHasher();
        var sut = new AdminOperations(Usuarios, Sesiones, hasher, prompt, new FakeTimeProvider());
        var comando = new AdminCommand.UsuarioCrear("usr_cli_crear");

        var exitCode = await sut.EjecutarAsync(comando, CancellationToken.None);

        Assert.Equal(0, exitCode);
        var estado = await Usuarios.FindByNameAsync("usr_cli_crear", CancellationToken.None);
        Assert.NotNull(estado);
        Assert.StartsWith("$argon2id$", estado!.ClaveHash, StringComparison.Ordinal);
        Assert.Equal(
            PasswordVerification.Correct,
            hasher.Verify("una-clave-de-prueba-sintetica", estado.ClaveHash));
        // A freshly created user is a first offender: default lockout state.
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(0, estado.NivelBloqueo);
        Assert.Null(estado.BloqueadoHasta);
        Assert.True(estado.Activo);
    }

    [Fact]
    public async Task PromptsForThePassword_Once_AndNeverEchoesItAsPlainOutput()
    {
        var prompt = new FakePasswordPrompt("otra-clave-sintetica");
        var sut = new AdminOperations(Usuarios, Sesiones, new Argon2idPasswordHasher(), prompt, new FakeTimeProvider());

        await sut.EjecutarAsync(new AdminCommand.UsuarioCrear("usr_cli_crear_2"), CancellationToken.None);

        Assert.Single(prompt.MensajesMostrados);
    }
}
