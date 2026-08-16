using System.Security.Cryptography;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Tasks 5.1/5.2 — `SmartNet/db/schema/checksums.txt` compensates a real gap in DbUp: DbUp records
/// a script's NAME in `fact.SchemaVersions` and never looks at its content again, so editing an
/// already-applied script is silently accepted — the database and the repository drift apart with
/// nothing failing. This manifest, and the check against it, is the only thing that catches that.
///
/// No database needed: this is a static comparison between `checksums.txt` and the files on disk.
/// The verification logic (`Verify`) is independent of, and never invokes,
/// `generate-checksums.ps1` — the two are separate implementations of the same format so that a
/// bug in one is not laundered by the other silently agreeing with itself.
/// </summary>
public sealed class ChecksumManifestTests
{
    // RED-first, same standard as the lint (coordinator's own framing): a check that cannot go red
    // proves nothing. Three synthetic scenarios, each proving one failure mode is actually caught,
    // before trusting the real guardian test against the real manifest.

    [Fact]
    public void Verify_ReportsAnError_WhenAListedScriptWasEditedAfterItWasHashed()
    {
        var dir = CreateTempSchemaDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact;");
            var originalHash = ComputeSha256Hex(Path.Combine(dir, "001_a.sql"));
            WriteManifest(dir, ("001_a.sql", originalHash));

            // Edited after being hashed and listed — this is exactly the drift DbUp itself cannot
            // detect, and the whole reason this manifest exists.
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact_edited;");

            var result = Verify(dir, Path.Combine(dir, "checksums.txt"));

            Assert.Contains(result.Errors, e => e.Contains("001_a.sql") && e.Contains("changed"));
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_ReportsAWarning_WhenAScriptExistsButIsNotListedInTheManifest()
    {
        var dir = CreateTempSchemaDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact;");
            var hashA = ComputeSha256Hex(Path.Combine(dir, "001_a.sql"));
            WriteManifest(dir, ("001_a.sql", hashA));

            // Added after the manifest was last generated — a normal, expected transient state
            // during active development, not evidence of drift on its own.
            File.WriteAllText(Path.Combine(dir, "002_b.sql"), "CREATE TABLE fact.X (Id INT);");

            var result = Verify(dir, Path.Combine(dir, "checksums.txt"));

            Assert.Empty(result.Errors);
            Assert.Contains(result.Warnings, w => w.Contains("002_b.sql") && w.Contains("not listed"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_ReportsAnError_WhenAManifestEntrysFileNoLongerExists()
    {
        var dir = CreateTempSchemaDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact;");
            var hashA = ComputeSha256Hex(Path.Combine(dir, "001_a.sql"));
            // The manifest lists a script that was then deleted from disk — a dangling entry.
            WriteManifest(dir, ("001_a.sql", hashA), ("999_gone.sql", "0000000000000000000000000000000000000000000000000000000000000000"[..64]));

            var result = Verify(dir, Path.Combine(dir, "checksums.txt"));

            Assert.Contains(result.Errors, e => e.Contains("999_gone.sql") && e.Contains("no longer exists"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Verify_ReportsNothing_WhenTheManifestMatchesTheScriptsExactly()
    {
        var dir = CreateTempSchemaDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact;");
            File.WriteAllText(Path.Combine(dir, "002_b.sql"), "CREATE TABLE fact.X (Id INT);");
            WriteManifest(dir,
                ("001_a.sql", ComputeSha256Hex(Path.Combine(dir, "001_a.sql"))),
                ("002_b.sql", ComputeSha256Hex(Path.Combine(dir, "002_b.sql"))));

            var result = Verify(dir, Path.Combine(dir, "checksums.txt"));

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // rollback/ is out of scope for this manifest — it is never applied by the runner, so "edited
    // after apply" does not apply to it (DboWriteLintTests covers its dbo-safety separately).
    [Fact]
    public void Verify_IgnoresScriptsUnderARollbackSubdirectory()
    {
        var dir = CreateTempSchemaDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_a.sql"), "CREATE SCHEMA fact;");
            WriteManifest(dir, ("001_a.sql", ComputeSha256Hex(Path.Combine(dir, "001_a.sql"))));

            var rollbackDir = Path.Combine(dir, "rollback");
            Directory.CreateDirectory(rollbackDir);
            File.WriteAllText(Path.Combine(rollbackDir, "001_down.sql"), "DROP SCHEMA fact;");

            var result = Verify(dir, Path.Combine(dir, "checksums.txt"));

            Assert.Empty(result.Errors);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // The real guardian: the actual checksums.txt against the actual scripts in
    // SmartNet/db/schema/. Zero errors, zero warnings — the manifest was generated from exactly
    // the files present today.
    [Fact]
    public void RealManifest_MatchesTheRealScripts_Exactly()
    {
        var schemaPath = RealSchemaPath();
        var manifestPath = Path.Combine(schemaPath, "checksums.txt");
        Assert.True(File.Exists(manifestPath), $"Expected {manifestPath} to exist.");

        var result = Verify(schemaPath, manifestPath);

        Assert.True(result.Errors.Count == 0, "Errors:\n" + string.Join('\n', result.Errors));
        Assert.True(result.Warnings.Count == 0, "Warnings:\n" + string.Join('\n', result.Warnings));
    }

    private static string RealSchemaPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema"));

    private static string CreateTempSchemaDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smartnet-checksum-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteManifest(string dir, params (string FileName, string Hash)[] entries)
    {
        var lines = entries
            .OrderBy(e => e.FileName, StringComparer.Ordinal)
            .Select(e => $"{e.Hash}  {e.FileName}");
        File.WriteAllText(Path.Combine(dir, "checksums.txt"), string.Join("\r\n", lines) + "\r\n");
    }

    private static string ComputeSha256Hex(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Verification result: `Errors` block a build (edited-after-hashed content, or a manifest
    /// entry whose file has vanished — both are drift the manifest exists to catch). `Warnings`
    /// never block — a script present on disk but not yet listed in the manifest is the normal,
    /// expected state right after adding a new script and before running
    /// `generate-checksums.ps1`, not evidence that anything already-applied has drifted.
    /// </summary>
    private static (List<string> Errors, List<string> Warnings) Verify(string schemaDir, string manifestPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var manifestLines = File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in manifestLines)
        {
            var parts = line.Split("  ", 2, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                errors.Add($"Malformed manifest line: '{line}'");
                continue;
            }

            manifest[parts[1]] = parts[0];
        }

        foreach (var (fileName, recordedHash) in manifest)
        {
            var filePath = Path.Combine(schemaDir, fileName);
            if (!File.Exists(filePath))
            {
                errors.Add($"{fileName}: listed in checksums.txt but the file no longer exists.");
                continue;
            }

            var actualHash = ComputeSha256Hex(filePath);
            if (!string.Equals(actualHash, recordedHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{fileName}: content has changed since it was hashed (recorded {recordedHash}, actual {actualHash}).");
            }
        }

        var actualScripts = Directory.GetFiles(schemaDir, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Cast<string>();
        foreach (var fileName in actualScripts)
        {
            if (!manifest.ContainsKey(fileName))
            {
                warnings.Add($"{fileName}: exists on disk but is not listed in checksums.txt (run generate-checksums.ps1).");
            }
        }

        return (errors, warnings);
    }
}
