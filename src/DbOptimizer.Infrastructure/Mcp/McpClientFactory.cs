using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Mcp;

public sealed class McpClientFactory(IServiceProvider serviceProvider) : IMcpClientFactory
{
    public IDatabaseMcpClient Create(DatabaseEngine databaseEngine)
    {
        return databaseEngine switch
        {
            DatabaseEngine.MySql => serviceProvider.GetRequiredService<MySqlMcpClient>(),
            DatabaseEngine.PostgreSql => serviceProvider.GetRequiredService<PostgreSqlMcpClient>(),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseEngine), databaseEngine, "Unsupported database engine.")
        };
    }
}

public sealed class MySqlMcpClient(
    McpOptions mcpOptions,
    McpFallbackOptions fallbackOptions,
    IDatabaseMcpFallbackExecutor fallbackExecutor,
    ILogger<DatabaseMcpClient> logger)
    : DatabaseMcpClient(DatabaseEngine.MySql, mcpOptions.MySql, mcpOptions, fallbackOptions, fallbackExecutor, logger);

public sealed class PostgreSqlMcpClient(
    McpOptions mcpOptions,
    McpFallbackOptions fallbackOptions,
    IDatabaseMcpFallbackExecutor fallbackExecutor,
    ILogger<DatabaseMcpClient> logger)
    : DatabaseMcpClient(DatabaseEngine.PostgreSql, mcpOptions.PostgreSql, mcpOptions, fallbackOptions, fallbackExecutor, logger);
