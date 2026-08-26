using Microsoft.Extensions.Time.Testing;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

namespace SmartNet.Admin.Tests;

/// <summary>
/// Task 5.6/5.7 -- `usuario restablecer-clave --nombre &lt;u&gt;`: updates <c>ClaveHash</c> via
/// the same Argon2id derivation as `usuario crear`, clears all three lockout fields
/// (<c>IntentosFallidos=0</c>, <c>BloqueadoHasta=NULL</c>, <c>NivelBloqueo=0</c>), and revokes
/// every existing session for that user via
/// <c>RevokeAllForUsuarioAsync(…, MotivoRevocacion.Restablecimiento)</c> — design.md's own framing:
/// "a password reset that leaves the old cookie working is not a reset".
/// </summary>
public sealed class UsuarioRestablecerClaveTests : AdminOperationsTestBase
{
    private async Task<long> SeedUsuarioLockedOutConSesionActivaAsync()
    {
        await Db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.Usuario (NombreUsuario, ClaveHash, IntentosFallidos, NivelBloqueo, BloqueadoHasta)
            VALUES ('usr_cli_reset', '$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 4, 2, DATEADD(MINUTE, 30, SYSUTCDATETIME()));
            """);

        return await Db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_cli_reset';");
    }

    [Fact]
    public async Task UpdatesClaveHash_AndClearsAllThreeLockoutFields()
    {
        await SeedUsuarioLockedOutConSesionActivaAsync();
        var prompt = new FakePasswordPrompt("clave-nueva-sintetica");
        var hasher = new Argon2idPasswordHasher();
        var sut = new AdminOperations(Usuarios, Sesiones, hasher, prompt, new FakeTimeProvider());

        var exitCode = await sut.EjecutarAsync(new AdminCommand.UsuarioRestablecerClave("usr_cli_reset"), CancellationToken.None);

        Assert.Equal(0, exitCode);
        var estado = await Usuarios.FindByNameAsync("usr_cli_reset", CancellationToken.None);
        Assert.NotNull(estado);
        Assert.Equal(PasswordVerification.Correct, hasher.Verify("clave-nueva-sintetica", estado!.ClaveHash));
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(0, estado.NivelBloqueo);
        Assert.Null(estado.BloqueadoHasta);
    }

    [Fact]
    public async Task RevokesEveryExistingSession_ForThatUser()
    {
        var usuarioId = await SeedUsuarioLockedOutConSesionActivaAsync();
        var tokenHash = "a".PadLeft(64, '0');
        var ahora = DateTimeOffset.UtcNow;
        await Sesiones.CreateAsync(usuarioId, tokenHash, ahora.AddHours(8), "ticket-previo-al-reset", CancellationToken.None);
        var prompt = new FakePasswordPrompt("clave-nueva-sintetica-2");
        var sut = new AdminOperations(Usuarios, Sesiones, new Argon2idPasswordHasher(), prompt, new FakeTimeProvider());

        await sut.EjecutarAsync(new AdminCommand.UsuarioRestablecerClave("usr_cli_reset"), CancellationToken.None);

        var activa = await Sesiones.FindActiveAsync(tokenHash, ahora, CancellationToken.None);
        Assert.Null(activa);
        var motivo = await Db.ExecuteScalarAsync<string>(
            $"SELECT MotivoRevocacion FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");
        Assert.Equal("RESTABLECIMIENTO", motivo);
    }

    [Fact]
    public async Task ReturnsANonZeroExitCode_ForAnUnknownUsername()
    {
        var prompt = new FakePasswordPrompt("clave-que-nunca-se-usa");
        var sut = new AdminOperations(Usuarios, Sesiones, new Argon2idPasswordHasher(), prompt, new FakeTimeProvider());

        var exitCode = await sut.EjecutarAsync(new AdminCommand.UsuarioRestablecerClave("no-existe-este-usuario"), CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        // The unknown-username path must never reach the prompt -- there is nothing to reset.
        Assert.Empty(prompt.MensajesMostrados);
    }
}
