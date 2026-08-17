namespace SmartNet.Auth.Core;

/// <summary>
/// The state <see cref="AccessPolicy"/> reads and rewrites — one field per persisted column,
/// nothing derived (design.md Decision 5, revised by Decision 8: THREE lockout fields, not two,
/// because <c>IntentosFallidos</c> alone cannot carry both "how many failures until the next
/// lock" and "how long that lock will be").
/// </summary>
public sealed record UsuarioCredentialState(
    long UsuarioId,
    string NombreUsuario,
    string ClaveHash,
    int IntentosFallidos, // failures inside the CURRENT window; 0 while locked
    int NivelBloqueo, // locks already served; picks the NEXT duration
    DateTimeOffset? BloqueadoHasta,
    bool Activo);
