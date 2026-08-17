using System.Text.RegularExpressions;

namespace SmartNet.Admin.Tests;

/// <summary>
/// Task 5.10/5.11 — static regression guard: no <c>UPDATE</c> statement anywhere in
/// <c>SmartNet/db/schema/*.sql</c> targets <c>ClaveHash</c>. That column has exactly one writer in
/// this project, <c>SqlUsuarioRepository.UpdateClaveHashAsync</c> (called only from
/// <c>SmartNet.Admin</c>'s `usuario crear` / `restablecer-clave` verbs) — never versioned SQL.
///
/// This test's job is to catch FUTURE drift, not to change anything now: as of this Work Unit,
/// every shipped script (`002_seguridad.sql` through `012_usuario_nivel_bloqueo.sql`) already
/// satisfies it — 002 only ever CREATEs the column, and no other script mentions it at all. This
/// mirrors item #1's task 4.7 framing exactly: "not duplicated, it's a regression guard" — the
/// value here is asserting the property will keep holding as new scripts are added, not proving
/// something that was already independently true.
/// </summary>
public sealed class ClaveHashNeverUpdatedInVersionedSqlTests
{
    // A batch is any UPDATE statement whose SET clause mentions ClaveHash. Statements are split on
    // GO (this project's own batch separator, per 012_usuario_nivel_bloqueo.sql's own comment)
    // and on ';' so an unrelated UPDATE elsewhere in the same script, or in a later batch, can
    // never be conflated with one that actually touches ClaveHash.
    private static readonly Regex UpdateClaveHashPattern = new(
        @"UPDATE\s+fact\.Usuario\b[^;]*?\bSET\b[^;]*?\bClaveHash\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    [Fact]
    public void NoSchemaScript_ContainsAnUpdateStatementTargetingClaveHash()
    {
        var scriptsPath = ResolveSchemaScriptsPath();
        var scripts = Directory.GetFiles(scriptsPath, "*.sql");
        Assert.NotEmpty(scripts); // Guards against a silently-empty glob hiding a passing-by-accident test.

        var infractores = new List<string>();
        foreach (var script in scripts)
        {
            var contenido = File.ReadAllText(script);
            var lotes = contenido.Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO", "\nGO"], StringSplitOptions.None);
            foreach (var lote in lotes)
            {
                if (UpdateClaveHashPattern.IsMatch(lote))
                {
                    infractores.Add(Path.GetFileName(script));
                    break;
                }
            }
        }

        Assert.Empty(infractores);
    }

    // Same repo-root-discovery trick as SmartNet.Db.Runner.RunnerOptions.ResolveDefaultScriptsPath
    // (walk up from the executing assembly looking for the .git marker) — kept independent of the
    // working directory the test runner happens to be invoked from.
    private static string ResolveSchemaScriptsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        var repoRoot = dir?.FullName
            ?? throw new InvalidOperationException(
                "No se pudo ubicar la raíz del repositorio (.git) para resolver la ruta de esquemas.");

        return Path.Combine(repoRoot, "SmartNet", "db", "schema");
    }
}
