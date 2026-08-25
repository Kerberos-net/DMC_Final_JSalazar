using Microsoft.Extensions.Configuration;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 3.5 -- shared-storage root resolution, same pattern as <see cref="ApiConnectionOptions"/>/
/// <see cref="ApiKeyRingOptions"/> (design D2, ADR 0013 "raíz configurable entregada a ambos
/// runtimes"), under a variable name distinct from the worker's own
/// <c>SMARTNET_WORKER_STORAGE_ROOT</c> (Python's <c>config.py</c>) -- same reasoning as
/// <c>ApiConnectionOptions</c>'s deliberately different name from the runner's.
/// </summary>
public class DocumentoStorageOptionsTests
{
    [Fact]
    public void Resolve_ThrowsWithUsageMessage_WhenConfigurationIsAbsent()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => DocumentoStorageOptions.Resolve(configuration));

        Assert.Contains("SMARTNET_API_STORAGE_ROOT", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ReturnsTheConfiguredValue_WhenPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SMARTNET_API_STORAGE_ROOT"] = @"C:\irrelevant-for-this-test",
            })
            .Build();

        var resolved = DocumentoStorageOptions.Resolve(configuration);

        Assert.Equal(@"C:\irrelevant-for-this-test", resolved);
    }

    [Fact]
    public void StorageRootKey_IsNotTheWorkersStorageRootVariableName()
    {
        Assert.Equal("SMARTNET_API_STORAGE_ROOT", DocumentoStorageOptions.StorageRootKey);
        Assert.NotEqual("SMARTNET_WORKER_STORAGE_ROOT", DocumentoStorageOptions.StorageRootKey);
    }
}
