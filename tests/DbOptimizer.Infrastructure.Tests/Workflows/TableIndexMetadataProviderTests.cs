using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbOptimizer.Infrastructure.Tests.Workflows;

public sealed class TableIndexMetadataProviderTests
{
    [Fact]
    public async Task GetIndexesAsync_WhenMcpIsDisabled_ThrowsInsteadOfFallingBack()
    {
        var configuration = new ConfigurationBuilder().Build();
        var options = new ExecutionPlanOptions
        {
            MySql = new ExecutionPlanMcpServerOptions
            {
                Enabled = false,
                Transport = "stdio",
                Command = "npx",
                Arguments = string.Empty
            }
        };

        var provider = new TableIndexMetadataProvider(
            configuration,
            options,
            NullLogger<TableIndexMetadataProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetIndexesAsync(DatabaseOptimizationEngine.MySql, "orders"));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MCP", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
