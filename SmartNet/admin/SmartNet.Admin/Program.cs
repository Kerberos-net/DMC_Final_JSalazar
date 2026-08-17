using SmartNet.Admin;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

var comando = AdminArguments.Parse(args);
if (comando is null)
{
    Console.Error.WriteLine(AdminArguments.Usage);
    return 1;
}

string connectionString;
try
{
    connectionString = AdminConnectionOptions.Resolve();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

IUsuarioRepository usuarios = new SqlUsuarioRepository(connectionString);
ISesionRepository sesiones = new SqlSesionRepository(connectionString);
IPasswordHasher hasher = new Argon2idPasswordHasher();
IPasswordPrompt prompt = new ConsolePasswordPrompt();

var operaciones = new AdminOperations(usuarios, sesiones, hasher, prompt, TimeProvider.System);

return await operaciones.EjecutarAsync(comando, CancellationToken.None);

// Exposes the top-level Program class to SmartNet.Admin.Tests, same pattern as SmartNet.Api's
// InternalsVisibleTo for WebApplicationFactory<Program>.
public partial class Program;
