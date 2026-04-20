using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbOptimizer.Infrastructure.Tests.Workflows;

public sealed class ExecutionPlanProviderTests
{
    [Fact]
    public async Task ExplainAsync_WhenMcpIsDisabled_ThrowsInsteadOfFallingBack()
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

        var provider = new ExecutionPlanProvider(
            configuration,
            options,
            NullLogger<ExecutionPlanProvider>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExplainAsync(DatabaseOptimizationEngine.MySql, "SELECT 1"));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MCP", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
