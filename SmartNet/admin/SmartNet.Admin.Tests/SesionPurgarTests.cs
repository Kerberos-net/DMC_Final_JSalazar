using Microsoft.Extensions.Time.Testing;
using SmartNet.Auth.Infrastructure;

namespace SmartNet.Admin.Tests;

/// <summary>
/// Task 5.8/5.9 -- `sesion purgar --retencion-dias &lt;n&gt;`: deletes <c>fact.Sesion</c> rows
/// older than the retention window and leaves rows within the window untouched. The sole
/// <c>DELETE</c> caller in the whole permission matrix (design.md Decision 3).
/// </summary>
public sealed class SesionPurgarTests : AdminOperationsTestBase
{
    private async Task<long> SeedUsuarioAsync()
    {
        await Db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_cli_purga', 'hash-de-prueba');");
        return await Db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_cli_purga';");
    }

    [Fact]
    public async Task DeletesRowsOlderThanTheRetentionWindow_AndLeavesNewerRowsUntouched()
    {
        var usuarioId = await SeedUsuarioAsync();
        var ahora = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(ahora);
        var tokenHashVieja = "b".PadLeft(64, '0');
        var tokenHashNueva = "c".PadLeft(64, '0');
        await Sesiones.CreateAsync(usuarioId, tokenHashVieja, ahora.AddHours(8), "ticket-viejo", CancellationToken.None);
        await Sesiones.CreateAsync(usuarioId, tokenHashNueva, ahora.AddHours(8), "ticket-nuevo", CancellationToken.None);
        await Db.ExecuteNonQueryAsync(
            $"UPDATE fact.Sesion SET CreadaEn = DATEADD(DAY, -100, SYSUTCDATETIME()) WHERE TokenHash = '{tokenHashVieja}';");

        var sut = new AdminOperations(
            Usuarios, Sesiones, new Argon2idPasswordHasher(), new FakePasswordPrompt("no-se-usa"), timeProvider);

        var exitCode = await sut.EjecutarAsync(new AdminCommand.SesionPurgar(90), CancellationToken.None);

        Assert.Equal(0, exitCode);
        var cuentaVieja = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE TokenHash = '{tokenHashVieja}';");
        var cuentaNueva = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE TokenHash = '{tokenHashNueva}';");
        Assert.Equal(0, cuentaVieja);
        Assert.Equal(1, cuentaNueva);
    }

    [Fact]
    public async Task NeverPromptsForAPassword()
    {
        var usuarioId = await SeedUsuarioAsync();
        var ahora = DateTimeOffset.UtcNow;
        await Sesiones.CreateAsync(usuarioId, "d".PadLeft(64, '0'), ahora.AddHours(8), "ticket", CancellationToken.None);
        var prompt = new FakePasswordPrompt("no-se-usa");
        var sut = new AdminOperations(Usuarios, Sesiones, new Argon2idPasswordHasher(), prompt, new FakeTimeProvider(ahora));

        await sut.EjecutarAsync(new AdminCommand.SesionPurgar(90), CancellationToken.None);

        Assert.Empty(prompt.MensajesMostrados);
    }
}
