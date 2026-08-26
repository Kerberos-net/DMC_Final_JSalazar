using SmartNet.Auth.Infrastructure;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// Shared setup for every <c>/api/sesion</c> integration test (design.md Testing Strategy):
/// migrate a throwaway database, seed one <c>fact.Usuario</c> row with a real Argon2id hash (the
/// SAME hasher production uses -- <see cref="Argon2idPasswordHasher"/>, never a shortcut), and
/// expose the known plaintext password so tests can log in for real.
/// </summary>
public abstract class SesionEndpointsTestBase : IAsyncLifetime
{
    protected const string NombreUsuario = "usr_sesion_pruebas";
    protected const string ClavePlanaCorrecta = "Clave-Correcta-2026!";

    protected TestDatabaseFixture Db { get; private set; } = null!;
    protected long UsuarioId { get; private set; }
    protected string KeyRingPath { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Db = await TestDatabaseFixture.CreateAsync();
        await Db.CreateWithoutLoginUserAsync("usr_api");
        await Db.CreateWithoutLoginUserAsync("usr_worker");
        await Db.CreateExternalDboCatalogsAsync();
        await Db.SeedDboMotivoFixtureRowsAsync();
        Assert.Equal(0, Db.RunMigrations());

        var hasher = new Argon2idPasswordHasher();
        var claveHash = hasher.Hash(ClavePlanaCorrecta);

        await Db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('{NombreUsuario}', '{claveHash}');
             """);
        UsuarioId = await Db.ExecuteScalarAsync<long>(
            $"SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = '{NombreUsuario}';");

        KeyRingPath = Path.Combine(
            Path.GetTempPath(), "smartnet-api-tests-keyring", Guid.NewGuid().ToString("N"));

        await AfterDatabaseReadyAsync();
    }

    /// <summary>Hook for derived classes that need extra per-test setup once the DB is migrated.</summary>
    protected virtual Task AfterDatabaseReadyAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        if (Directory.Exists(KeyRingPath))
        {
            Directory.Delete(KeyRingPath, recursive: true);
        }
    }

    protected async Task<int> GetIntentosFallidosAsync() =>
        await Db.ExecuteScalarAsync<int>(
            $"SELECT IntentosFallidos FROM fact.Usuario WHERE UsuarioId = {UsuarioId};");

    protected async Task<int> GetNivelBloqueoAsync() =>
        await Db.ExecuteScalarAsync<int>(
            $"SELECT NivelBloqueo FROM fact.Usuario WHERE UsuarioId = {UsuarioId};");

    protected async Task<DateTime?> GetBloqueadoHastaAsync() =>
        await Db.ExecuteScalarAsync<DateTime?>(
            $"SELECT BloqueadoHasta FROM fact.Usuario WHERE UsuarioId = {UsuarioId};");

    protected static string? ExtractSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (value.StartsWith("__Host-session=", StringComparison.Ordinal))
            {
                // Just the "name=value" pair, suitable for a Cookie request header.
                return value.Split(';')[0];
            }
        }

        return null;
    }
}
