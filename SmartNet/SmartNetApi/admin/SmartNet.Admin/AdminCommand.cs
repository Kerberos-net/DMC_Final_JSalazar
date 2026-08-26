namespace SmartNet.Admin;

/// <summary>
/// The parsed shape of one <c>smartnet-admin</c> invocation (design.md Decision 7). Never carries
/// a password — the password is read interactively, after parsing, by <see cref="IPasswordPrompt"/>,
/// never from <c>argv</c>.
/// </summary>
public abstract record AdminCommand
{
    public sealed record UsuarioCrear(string NombreUsuario) : AdminCommand;

    public sealed record UsuarioRestablecerClave(string NombreUsuario) : AdminCommand;

    public sealed record SesionPurgar(int RetencionDias) : AdminCommand;
}
