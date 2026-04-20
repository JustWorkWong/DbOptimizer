using DbOptimizer.Infrastructure.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Mcp;

public sealed class DatabaseMcpClientTests
{
    [Fact]
    public async Task QueryAsync_WhenMcpIsDisabled_ThrowsInsteadOfFallingBack()
    {
        var client = new DatabaseMcpClient(
            DatabaseEngine.MySql,
            new McpServerOptions
            {
                Enabled = false,
                Transport = "stdio",
                Command = "npx",
                Arguments = string.Empty
            },
            new McpOptions
            {
                MySql = new McpServerOptions
                {
                    Enabled = false,
                    Transport = "stdio",
                    Command = "npx",
                    Arguments = string.Empty
                },
                PostgreSql = new McpServerOptions
                {
                    Enabled = false,
                    Transport = "stdio",
                    Command = "npx",
                    Arguments = string.Empty
                },
                TimeoutSeconds = 5,
                RetryCount = 0,
                RetryDelayMilliseconds = 0,
                EnableDirectDbFallback = false
            },
            new McpFallbackOptions
            {
                MySqlConnectionString = "Server=localhost;Database=test;",
                PostgreSqlConnectionString = "Host=localhost;Database=test;"
            },
            Mock.Of<IDatabaseMcpFallbackExecutor>(),
            NullLogger<DatabaseMcpClient>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.QueryAsync("SELECT 1"));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MCP", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
