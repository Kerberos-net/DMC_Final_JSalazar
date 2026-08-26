namespace SmartNet.Admin;

/// <summary>
/// Port over interactive, no-echo password entry (design.md Decision 7, tasks.md 5.2/5.3). The
/// ONLY way any verb ever obtains a password — never <c>argv</c>, which lands in shell history,
/// `ps`, and Windows process-creation audit records.
/// </summary>
public interface IPasswordPrompt
{
    /// <summary>
    /// Displays <paramref name="mensaje"/>, then reads a password from the interactive input
    /// stream without echoing it, until Enter.
    /// </summary>
    string ReadPassword(string mensaje);
}
