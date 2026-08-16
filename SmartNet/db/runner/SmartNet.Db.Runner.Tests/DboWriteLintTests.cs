using System.Text.RegularExpressions;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Coordinator-directed follow-up (item 4) — brings the static `dbo`-write lint forward from
/// task 5.5. A pure text scan over the versioned scripts in `SmartNet/db/schema/`: no `CREATE`,
/// `ALTER`, `DROP`, `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, `MERGE`, or `REFERENCES` may target a
/// `dbo` object. The only permitted mention of `dbo` anywhere in the versioned SQL is a
/// `GRANT SELECT ON OBJECT::dbo.&lt;table&gt; TO fact_api|fact_worker;` line, or a SQL comment. No
/// database needed — this is what the relaxed `NoTableCreatedByThisProject_ExistsOutsideSchemaFact`
/// assertion (SchemaShapeTests.cs) no longer guarantees on its own, restored here properly.
///
/// Task 5.5 also asks for a CI lint step wired into the build; that packaging (and the broader
/// "unqualified dbo. outside the four/five permitted lines" framing task 5.5 describes) remains
/// open in Phase 5. This test is the detection logic task 5.5 can wrap in a CI step later.
/// </summary>
public sealed class DboWriteLintTests
{
    private static readonly Regex AllowedGrantLine = new(
        @"^\s*GRANT\s+SELECT\s+ON\s+OBJECT::dbo\.\w+\s+TO\s+fact_(api|worker)\s*;\s*$",
        RegexOptions.IgnoreCase);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex LineComment = new(@"--[^\r\n]*");
    private static readonly Regex DboMention = new(@"\bdbo\b", RegexOptions.IgnoreCase);

    // RED-first: the detection logic itself did not exist before this test. A throwaway script
    // with a real dbo write proves the lint actually catches the violation it claims to catch —
    // the real schema/ directory is already clean, so asserting against it alone would never have
    // been RED against any lint bug (the same reasoning already documented for task 3.4).
    [Fact]
    public void Lint_DetectsViolation_WhenScriptWritesToDbo()
    {
        var path = CreateScriptFile("UPDATE dbo.Proveedor SET proveedor = 'x' WHERE codpro = 'P00000';");
        try
        {
            var violations = FindDboViolations(path);
            Assert.NotEmpty(violations);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptDeclaresReferencesToDbo()
    {
        var path = CreateScriptFile(
            "CREATE TABLE fact.X (Y INT NOT NULL, CONSTRAINT FK_X_Y FOREIGN KEY (Y) REFERENCES dbo.CuentaContable (cuenta));");
        try
        {
            var violations = FindDboViolations(path);
            Assert.NotEmpty(violations);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptCreatesADboObject()
    {
        var path = CreateScriptFile("CREATE TABLE dbo.Intruso (Id INT NOT NULL);");
        try
        {
            var violations = FindDboViolations(path);
            Assert.NotEmpty(violations);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Lint_AllowsGrantSelectLines_AndComments()
    {
        var path = CreateScriptFile(
            """
            -- A comment mentioning dbo.Proveedor is fine, it is not executable.
            GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_api;
            GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_worker;
            /* another comment about dbo.CuentaContable, also fine */
            GRANT SELECT ON OBJECT::dbo.DocumentoIdentidad TO fact_api;
            """);
        try
        {
            var violations = FindDboViolations(path);
            Assert.Empty(violations);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    // The actual guardian: the real versioned scripts, 001-008, have no disallowed dbo mention.
    [Fact]
    public void RealSchemaScripts_HaveNoDisallowedDboMentions()
    {
        var schemaPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema"));
        Assert.True(Directory.Exists(schemaPath), $"Expected to find SmartNet/db/schema/ at {schemaPath}.");

        var scripts = Directory.GetFiles(schemaPath, "*.sql");
        Assert.NotEmpty(scripts);

        var allViolations = scripts.SelectMany(FindDboViolations).ToList();
        Assert.True(allViolations.Count == 0,
            "Disallowed dbo mention(s) found:\n" + string.Join('\n', allViolations));
    }

    /// <summary>
    /// Strips SQL comments, then flags any remaining line that mentions `dbo` unless the line is
    /// exactly an allowed `GRANT SELECT ON OBJECT::dbo.&lt;table&gt; TO fact_api|fact_worker;`
    /// statement. Comment stripping is regex-based, adequate for this project's own scripts (none
    /// of which embed `--` or `/*`/`*/` inside a string literal); not a general-purpose SQL parser.
    /// </summary>
    private static List<string> FindDboViolations(string path)
    {
        var raw = File.ReadAllText(path);
        var withoutBlockComments = BlockComment.Replace(raw, string.Empty);
        var withoutComments = LineComment.Replace(withoutBlockComments, string.Empty);

        var violations = new List<string>();
        var lines = withoutComments.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!DboMention.IsMatch(line))
            {
                continue;
            }

            if (AllowedGrantLine.IsMatch(line))
            {
                continue;
            }

            violations.Add($"{Path.GetFileName(path)}:{i + 1}: {line.Trim()}");
        }

        return violations;
    }

    private static string CreateScriptFile(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smartnet-dbo-lint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "001_sample.sql");
        File.WriteAllText(path, content);
        return path;
    }
}
