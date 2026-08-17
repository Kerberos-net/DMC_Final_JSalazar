using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.4 -- proves the "absent ⇒ startup failure" behaviour through the REAL host pipeline
/// (not just the unit-level ApiConnectionOptionsTests), by constructing a WebApplicationFactory
/// that deliberately does NOT supply SMARTNET_API_DB_CONNECTION.
/// </summary>
public class StartupFailsFastTests
{
    private sealed class MissingConnectionStringFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Deliberately supplies the key-ring path but NOT the connection string, so the
            // failure under test is unambiguously attributable to the connection string.
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ApiKeyRingOptions.KeyRingPathKey] =
                        Path.Combine(Path.GetTempPath(), "smartnet-api-tests-keyring-missing-conn"),
                });
            });
        }
    }

    [Fact]
    public void Startup_FailsFast_WhenConnectionStringConfigurationIsAbsent()
    {
        using var factory = new MissingConnectionStringFactory();

        var ex = Record.Exception(() => factory.Services);

        Assert.NotNull(ex);
        Assert.Contains("SMARTNET_API_DB_CONNECTION", ex!.ToString(), StringComparison.Ordinal);
    }
}
