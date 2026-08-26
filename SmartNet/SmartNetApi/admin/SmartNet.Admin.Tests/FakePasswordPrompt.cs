namespace SmartNet.Admin.Tests;

/// <summary>
/// Test double for <see cref="IPasswordPrompt"/> — no console, no interactivity, an obviously
/// synthetic value handed straight to the caller. Records every prompt message it was shown, so
/// tests can assert an operator-facing prompt actually happened without needing a real TTY
/// (CONVENTIONS.md: never a real-looking credential, even in tests).
/// </summary>
public sealed class FakePasswordPrompt(string claveARetornar) : IPasswordPrompt
{
    public List<string> MensajesMostrados { get; } = [];

    public string ReadPassword(string mensaje)
    {
        MensajesMostrados.Add(mensaje);
        return claveARetornar;
    }
}
