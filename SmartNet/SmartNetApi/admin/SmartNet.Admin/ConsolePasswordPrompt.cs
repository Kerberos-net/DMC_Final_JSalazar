using System.Text;

namespace SmartNet.Admin;

/// <summary>
/// Real implementation of <see cref="IPasswordPrompt"/> — <c>Console.ReadKey(intercept: true)</c>
/// reads one key at a time without echoing it to the terminal, exactly the "no-echo prompt" task
/// 5.2/5.3 requires. Never covered by an automated test directly (it needs a real interactive TTY,
/// which no CI/test host provides); what IS proven is that no verb's argument surface ever offers
/// an alternative, argv-based path to a password (<c>AdminArgumentsTests.NoVerb_HasAPasswordBearingFlag</c>).
/// </summary>
public sealed class ConsolePasswordPrompt : IPasswordPrompt
{
    public string ReadPassword(string mensaje)
    {
        Console.Write(mensaje);
        var buffer = new StringBuilder();

        ConsoleKeyInfo tecla;
        while ((tecla = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(tecla.KeyChar))
            {
                buffer.Append(tecla.KeyChar);
            }
        }

        Console.WriteLine();
        return buffer.ToString();
    }
}
