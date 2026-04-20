using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbOptimizer.Infrastructure.Tests.Workflows;

public sealed class ConfigCollectionProviderTests
{
    [Fact]
    public async Task CollectConfigAsync_WhenMcpIsDisabled_ThrowsInsteadOfReturningFallbackSnapshot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DbOptimizer:ConfigCollection:MySql:Enabled"] = "false",
                ["DbOptimizer:ConfigCollection:MySql:Transport"] = "stdio",
                ["DbOptimizer:ConfigCollection:MySql:Command"] = "npx",
                ["DbOptimizer:ConfigCollection:MySql:Arguments"] = "-y @modelcontextprotocol/server-mysql"
            })
            .Build();

        var provider = new ConfigCollectionProvider(
            configuration,
            new ConfigCollectionOptions(),
            NullLogger<ConfigCollectionProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CollectConfigAsync(DbOptimizer.Core.Models.DatabaseOptimizationEngine.MySql, "mysql-local"));

        Assert.Contains("MCP", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
