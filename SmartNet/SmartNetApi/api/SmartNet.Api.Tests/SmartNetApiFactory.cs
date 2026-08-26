using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartNet.Auth.Core;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> customization for every
/// <c>SmartNet.Api.Tests</c> suite (design.md Testing Strategy): points
/// <see cref="ApiConnectionOptions"/> at a real <see cref="TestDatabaseFixture"/> database and
/// <see cref="ApiKeyRingOptions"/> at a throwaway per-instance key-ring directory, via
/// <c>ConfigureAppConfiguration</c> -- the documented way to override environment-variable-sourced
/// configuration in a <c>WebApplicationFactory</c> without touching real process environment
/// variables (avoids cross-test races under xUnit's parallelism).
///
/// Also lets a test substitute a <see cref="TimeProvider"/> (task 4.9's key-ring restart
/// simulation and task 4.17's escalation end-to-end both need this) and an <see cref="IPasswordHasher"/>
/// decorator (task 4.14/4.16's call-count assertions).
/// </summary>
internal sealed class SmartNetApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _keyRingPath;
    private readonly string _storageRoot;
    private readonly TimeProvider? _timeProvider;
    private readonly IPasswordHasher? _passwordHasher;

    public SmartNetApiFactory(
        string connectionString,
        string? keyRingPath = null,
        TimeProvider? timeProvider = null,
        IPasswordHasher? passwordHasher = null,
        string? storageRoot = null)
    {
        _connectionString = connectionString;
        _keyRingPath = keyRingPath ?? Path.Combine(Path.GetTempPath(), "smartnet-api-tests-keyring", Guid.NewGuid().ToString("N"));
        _timeProvider = timeProvider;
        _passwordHasher = passwordHasher;
        // task 3.5 (design D2) -- a fresh throwaway directory per factory instance, same shape as
        // the key-ring default above: tests that need real bytes on disk (threat-matrix orphan-row
        // and happy-path scenarios) create files under this exact path via Db-independent helpers.
        _storageRoot = storageRoot ?? Path.Combine(Path.GetTempPath(), "smartnet-api-tests-storage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
    }

    public string StorageRoot => _storageRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiConnectionOptions.ConnectionStringKey] = _connectionString,
                [ApiKeyRingOptions.KeyRingPathKey] = _keyRingPath,
                [DocumentoStorageOptions.StorageRootKey] = _storageRoot,
            });
        });

        builder.ConfigureServices(services =>
        {
            if (_timeProvider is not null)
            {
                services.AddSingleton(_timeProvider);
            }

            if (_passwordHasher is not null)
            {
                services.AddSingleton(_passwordHasher);
            }
        });
    }
}
