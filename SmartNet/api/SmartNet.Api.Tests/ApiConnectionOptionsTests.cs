using Microsoft.Extensions.Configuration;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.4/4.5 -- connection-string resolution, parsed the same way as
/// SmartNet.Db.Runner.RunnerOptions but under a DELIBERATELY different variable name
/// (design.md Decision 6): reusing the runner's SMARTNET_DB_CONNECTION would hand the API host
/// the deploy principal's rights on a shared database.
/// </summary>
public class ApiConnectionOptionsTests
{
    [Fact]
    public void Resolve_ThrowsWithUsageMessage_WhenConfigurationIsAbsent()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ApiConnectionOptions.Resolve(configuration));

        Assert.Contains("SMARTNET_API_DB_CONNECTION", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ReturnsTheConfiguredValue_WhenPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SMARTNET_API_DB_CONNECTION"] = "Server=irrelevant-for-this-test;",
            })
            .Build();

        var resolved = ApiConnectionOptions.Resolve(configuration);

        Assert.Equal("Server=irrelevant-for-this-test;", resolved);
    }

    // The exact variable-name-distinctness assertion the coordinator's scope explicitly calls
    // for: not merely "the new one works" but that it is NOT the runner's variable.
    [Fact]
    public void ConnectionStringKey_IsNotTheRunnersConnectionStringVariableName()
    {
        Assert.Equal("SMARTNET_API_DB_CONNECTION", ApiConnectionOptions.ConnectionStringKey);
        Assert.NotEqual("SMARTNET_DB_CONNECTION", ApiConnectionOptions.ConnectionStringKey);
    }
}
