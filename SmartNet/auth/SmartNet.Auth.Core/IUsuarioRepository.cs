namespace SmartNet.Auth.Core;

/// <summary>
/// Port over <c>fact.Usuario</c> (design.md Decision 5). No implementation lives in this
/// project — the SQL adapter is Phase 3's <c>SmartNet.Auth.Infrastructure</c>.
/// </summary>
public interface IUsuarioRepository
{
    Task<UsuarioCredentialState?> FindByNameAsync(string nombreUsuario, CancellationToken ct);

    // Signature unchanged by Decision 8: it already persists the whole state object. Only the
    // UPDATE behind it widens to three columns — the exact reason Decision 5 made this
    // state-shaped rather than field-shaped, guarding against "a state field the UPDATE forgets
    // to write".
    Task SaveCredentialStateAsync(UsuarioCredentialState estado, CancellationToken ct);

    Task UpdateClaveHashAsync(long usuarioId, string claveHash, CancellationToken ct);
}
