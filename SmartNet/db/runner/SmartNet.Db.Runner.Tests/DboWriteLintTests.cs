using System.Text.RegularExpressions;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Coordinator-directed follow-up (item 4) — brings the static `dbo`-write lint forward from
/// task 5.5. A pure text scan over the versioned scripts in `SmartNet/db/schema/`: no `CREATE`,
/// `ALTER`, `DROP`, `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, or `MERGE` statement may TARGET a
/// `dbo` object, and no `REFERENCES` may point at one. Reading `dbo` — a bare `SELECT ... FROM
/// dbo.X`/`JOIN dbo.X`, or the `GRANT SELECT ON OBJECT::dbo.&lt;table&gt;` lines in `008` — is
/// always allowed; ADR 0003's rule is "nadie escribe una tabla externa", not "nadie la lee". No
/// database needed — this is what the relaxed `NoTableCreatedByThisProject_ExistsOutsideSchemaFact`
/// assertion (SchemaShapeTests.cs) no longer guarantees on its own, restored here properly.
///
/// **Corrected during Work Unit 4** (Phase 4, base-data): the first draft of this lint (Work Unit 3)
/// flagged *any* line mentioning `dbo` outside an allowed `GRANT` line — which would have wrongly
/// rejected `010_motivo_atributo_demo.sql`'s legitimate `INSERT INTO fact.MotivoAtributo ... SELECT
/// ... FROM dbo.Motivo`, a read, the moment that script was written. Verified before fixing: the old
/// blanket rule really did flag that exact statement (a throwaway reflection probe against the old
/// implementation, discarded once confirmed — not kept as a permanent test since the fixed
/// implementation below is what ships). Rewritten to check each SQL *statement* for a forbidden verb
/// (`CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/`TRUNCATE`/`MERGE`) whose own target is
/// `dbo.*`, or a `REFERENCES dbo.*` clause — never a bare mention of the word `dbo`.
///
/// Task 5.5 also asks for a CI lint step wired into the build; that packaging remains open in
/// Phase 5. This test is the detection logic task 5.5 can wrap in a CI step later.
/// </summary>
public sealed class DboWriteLintTests
{
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex LineComment = new(@"--[^\r\n]*");

    // Each targets the verb immediately followed (optionally through INTO/TABLE/FROM) by a dbo.*
    // identifier — the verb's own object, not an unrelated dbo mention elsewhere in the statement.
    private static readonly Regex[] ForbiddenTargets =
    [
        new(@"\bINSERT\s+(INTO\s+)?dbo\.", RegexOptions.IgnoreCase),
        new(@"\bUPDATE\s+dbo\.", RegexOptions.IgnoreCase),
        new(@"\bDELETE\s+(FROM\s+)?dbo\.", RegexOptions.IgnoreCase),
        new(@"\bCREATE\s+(TABLE|VIEW|INDEX|SCHEMA|PROCEDURE|FUNCTION|SEQUENCE)\s+dbo\.", RegexOptions.IgnoreCase),
        new(@"\bALTER\s+(TABLE|VIEW|SCHEMA|INDEX|DATABASE)\s+dbo\.", RegexOptions.IgnoreCase),
        new(@"\bDROP\s+(TABLE|VIEW|INDEX|SCHEMA|PROCEDURE|FUNCTION|SEQUENCE)\s+dbo\.", RegexOptions.IgnoreCase),
        new(@"\bTRUNCATE\s+TABLE\s+dbo\.", RegexOptions.IgnoreCase),
        new(@"\bMERGE\s+(INTO\s+)?dbo\.", RegexOptions.IgnoreCase),
        new(@"\bREFERENCES\s+dbo\.", RegexOptions.IgnoreCase),
        // `SELECT ... INTO dbo.X` both creates and populates a dbo table without naming any verb
        // matched above. Verified as a real blind spot of the Work Unit 4 rewrite before adding it.
        new(@"\bINTO\s+dbo\.", RegexOptions.IgnoreCase),
    ];

    // RED-first: the detection logic itself did not exist before this test. A throwaway script
    // with a real dbo write proves the lint actually catches the violation it claims to catch —
    // the real schema/ directory is already clean, so asserting against it alone would never have
    // been RED against any lint bug (the same reasoning already documented for task 3.4).
    [Fact]
    public void Lint_DetectsViolation_WhenScriptWritesToDbo()
    {
        AssertViolation("UPDATE dbo.Proveedor SET proveedor = 'x' WHERE codpro = 'P00000';");
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptInsertsIntoDbo()
    {
        AssertViolation("INSERT INTO dbo.Motivo (codigo, motivo) VALUES (999, 'Intruso');");
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptDeletesFromDbo()
    {
        AssertViolation("DELETE FROM dbo.Proveedor WHERE codpro = 'P00000';");
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptDeclaresReferencesToDbo()
    {
        AssertViolation(
            "CREATE TABLE fact.X (Y INT NOT NULL, CONSTRAINT FK_X_Y FOREIGN KEY (Y) REFERENCES dbo.CuentaContable (cuenta));");
    }

    [Fact]
    public void Lint_DetectsViolation_WhenScriptCreatesADboObject()
    {
        AssertViolation("CREATE TABLE dbo.Intruso (Id INT NOT NULL);");
    }

    // Found while verifying Work Unit 4's rewrite of this lint. The SELECT INTO hole was real and
    // reachable; it does not exist in the real scripts today. A guardian that misses a construct is
    // worse than none, because it is trusted.
    [Fact]
    public void Lint_DetectsViolation_WhenScriptCreatesADboTableWithSelectInto()
    {
        AssertViolation("SELECT codigo, motivo INTO dbo.MotivoCopia FROM fact.MotivoAtributo;");
    }

    // Dynamic SQL does not evade the patterns above: the scan is textual and does not respect
    // string literals, so `EXEC('CREATE TABLE dbo.X ...')` is matched by the plain CREATE rule.
    // Verified by probe — a dedicated EXEC pattern added nothing and was removed. What DOES evade
    // it is a concatenated schema name (`EXEC('CREATE TABLE ' + @s + '.X')`), which no text scan
    // can catch; that residue belongs to review, and to 008 granting no DDL right on dbo.
    [Fact]
    public void Lint_DetectsViolation_WhenScriptHidesADboWriteInDynamicSql()
    {
        AssertViolation("EXEC('CREATE TABLE dbo.Intruso (Id INT NOT NULL)');");
    }

    [Fact]
    public void Lint_AllowsGrantSelectLines_AndComments()
    {
        AssertNoViolation(
            """
            -- A comment mentioning dbo.Proveedor is fine, it is not executable.
            GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_api;
            GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_worker;
            /* another comment about dbo.CuentaContable, also fine */
            GRANT SELECT ON OBJECT::dbo.DocumentoIdentidad TO fact_api;
            """);
    }

    // Regression for the bug found and fixed during Work Unit 4: a bare read of dbo must be
    // allowed, including as the SELECT source of an INSERT into fact. This is exactly
    // 010_motivo_atributo_demo.sql's own shape.
    [Fact]
    public void Lint_AllowsSelectReadsFromDbo_IncludingAsAnInsertSelectSource()
    {
        AssertNoViolation(
            """
            INSERT INTO fact.MotivoAtributo (Motivo, OrigenLibro, Activo)
            SELECT codigo, '02', 1
            FROM dbo.Motivo
            WHERE codigo IN (5, 13, 16);
            """);
        AssertNoViolation("SELECT COUNT(*) FROM dbo.Motivo WHERE codigo = 5;");
        AssertNoViolation(
            "SELECT m.codigo FROM dbo.Motivo m JOIN fact.MotivoAtributo a ON a.Motivo = m.codigo;");
    }

    // The actual guardian: the real versioned scripts, 001-010 (whatever exists today), have no
    // disallowed dbo mention.
    [Fact]
    public void RealSchemaScripts_HaveNoDisallowedDboMentions()
    {
        var schemaPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema"));
        Assert.True(Directory.Exists(schemaPath), $"Expected to find SmartNet/db/schema/ at {schemaPath}.");

        // Recursive on purpose, so `rollback/` is covered too. The down scripts are advisory and
        // the runner never applies them, but a human may run one by hand against a real database —
        // which makes them exactly the place where an unnoticed `dbo` write would do its damage.
        var scripts = Directory.GetFiles(schemaPath, "*.sql", SearchOption.AllDirectories);
        Assert.Contains(scripts, s => s.Contains("rollback", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(scripts);

        var allViolations = scripts.SelectMany(FindDboViolations).ToList();
        Assert.True(allViolations.Count == 0,
            "Disallowed dbo mention(s) found:\n" + string.Join('\n', allViolations));
    }

    private static void AssertViolation(string sql)
    {
        var path = CreateScriptFile(sql);
        try
        {
            Assert.NotEmpty(FindDboViolations(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static void AssertNoViolation(string sql)
    {
        var path = CreateScriptFile(sql);
        try
        {
            Assert.Empty(FindDboViolations(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    /// <summary>
    /// Strips SQL comments, then flags any remaining SQL *statement* whose own verb targets a
    /// `dbo.*` object, or that declares `REFERENCES dbo.*`. A bare read of `dbo` (`SELECT`, `JOIN`)
    /// is never flagged. Statements are split on `;`; comment stripping and statement splitting are
    /// regex-based, adequate for this project's own scripts (none of which embed `--`, `/*`/`*/`, or
    /// `;` inside a string literal); not a general-purpose SQL parser.
    /// </summary>
    private static List<string> FindDboViolations(string path)
    {
        var raw = File.ReadAllText(path);
        var withoutBlockComments = BlockComment.Replace(raw, string.Empty);
        var withoutComments = LineComment.Replace(withoutBlockComments, string.Empty);

        var violations = new List<string>();
        var statements = withoutComments.Split(';');
        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            foreach (var forbidden in ForbiddenTargets)
            {
                if (forbidden.IsMatch(statement))
                {
                    violations.Add($"{Path.GetFileName(path)}: {statement.Trim()}");
                    break;
                }
            }
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
