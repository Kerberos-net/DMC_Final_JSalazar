using SmartNet.Auth.Core;

namespace SmartNet.Admin;

/// <summary>
/// Dispatches a parsed <see cref="AdminCommand"/> to the already-shipped
/// <see cref="IUsuarioRepository"/> / <see cref="ISesionRepository"/> / <see cref="IPasswordHasher"/>
/// ports (design.md Decision 7). Deliberately thin — no new domain/business-rule logic lives here,
/// only composition of pieces Phases 2/3 already proved correct in isolation.
/// </summary>
public sealed class AdminOperations(
    IUsuarioRepository usuarios,
    ISesionRepository sesiones,
    IPasswordHasher hasher,
    IPasswordPrompt prompt,
    TimeProvider timeProvider)
{
    public Task<int> EjecutarAsync(AdminCommand comando, CancellationToken ct) => comando switch
    {
        AdminCommand.UsuarioCrear crear => UsuarioCrearAsync(crear, ct),
        AdminCommand.UsuarioRestablecerClave restablecer => UsuarioRestablecerClaveAsync(restablecer, ct),
        AdminCommand.SesionPurgar purgar => SesionPurgarAsync(purgar, ct),
        _ => throw new InvalidOperationException($"Comando no reconocido: {comando.GetType().Name}"),
    };

    private async Task<int> UsuarioCrearAsync(AdminCommand.UsuarioCrear comando, CancellationToken ct)
    {
        var clave = prompt.ReadPassword($"Contraseña para '{comando.NombreUsuario}': ");
        var claveHash = hasher.Hash(clave);

        var usuarioId = await usuarios.CreateAsync(comando.NombreUsuario, claveHash, ct);

        Console.WriteLine($"Usuario '{comando.NombreUsuario}' creado (UsuarioId={usuarioId}).");
        return 0;
    }

    // design.md Decision 7: "restablecer-clave también llama a RevokeAllForUsuarioAsync(…,
    // RESTABLECIMIENTO): un restablecimiento de contraseña que deja la cookie anterior funcionando
    // no es un restablecimiento."
    private async Task<int> UsuarioRestablecerClaveAsync(AdminCommand.UsuarioRestablecerClave comando, CancellationToken ct)
    {
        var estado = await usuarios.FindByNameAsync(comando.NombreUsuario, ct);
        if (estado is null)
        {
            Console.Error.WriteLine($"No existe el usuario '{comando.NombreUsuario}'.");
            return 1;
        }

        var clave = prompt.ReadPassword($"Nueva contraseña para '{comando.NombreUsuario}': ");
        var claveHash = hasher.Hash(clave);
        await usuarios.UpdateClaveHashAsync(estado.UsuarioId, claveHash, ct);

        // Clears all three lockout fields in one call -- SaveCredentialStateAsync is
        // state-shaped precisely so a call site can never forget one of the three (design.md
        // Decision 8).
        await usuarios.SaveCredentialStateAsync(
            estado with { IntentosFallidos = 0, NivelBloqueo = 0, BloqueadoHasta = null }, ct);

        var ahora = timeProvider.GetUtcNow();
        await sesiones.RevokeAllForUsuarioAsync(estado.UsuarioId, MotivoRevocacion.Restablecimiento, ahora, ct);

        Console.WriteLine($"Contraseña de '{comando.NombreUsuario}' restablecida; sesiones existentes revocadas.");
        return 0;
    }

    private async Task<int> SesionPurgarAsync(AdminCommand.SesionPurgar comando, CancellationToken ct)
    {
        var ahora = timeProvider.GetUtcNow();
        var corte = ahora.AddDays(-comando.RetencionDias);

        var eliminadas = await sesiones.DeleteOlderThanAsync(corte, ct);

        Console.WriteLine($"Se eliminaron {eliminadas} sesión(es) creadas antes de {corte:O}.");
        return 0;
    }
}
