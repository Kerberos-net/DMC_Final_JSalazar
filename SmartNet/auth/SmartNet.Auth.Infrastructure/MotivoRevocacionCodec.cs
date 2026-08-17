using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// Maps <see cref="MotivoRevocacion"/> to/from the exact string literals
/// <c>CK_Sesion_MotivoRevocacion</c> allows (schema 011): <c>CIERRE_SESION</c>,
/// <c>RESTABLECIMIENTO</c>, <c>ADMIN</c>. Kept as a single mapping so no adapter re-spells the
/// value list independently.
/// </summary>
internal static class MotivoRevocacionCodec
{
    public static string ToDbValue(MotivoRevocacion motivo) => motivo switch
    {
        MotivoRevocacion.CierreSesion => "CIERRE_SESION",
        MotivoRevocacion.Restablecimiento => "RESTABLECIMIENTO",
        MotivoRevocacion.Admin => "ADMIN",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "Unknown MotivoRevocacion."),
    };
}
