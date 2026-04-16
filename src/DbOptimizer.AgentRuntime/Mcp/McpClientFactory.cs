namespace DbOptimizer.AgentRuntime;

internal sealed class McpClientFactory(IServiceProvider serviceProvider) : IMcpClientFactory
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

internal sealed class MySqlMcpClient(
    RuntimeOptions runtimeOptions,
    IDatabaseMcpFallbackExecutor fallbackExecutor,
    ILogger<DatabaseMcpClient> logger)
    : DatabaseMcpClient(DatabaseEngine.MySql, runtimeOptions.Mcp.MySql, runtimeOptions.Mcp, fallbackExecutor, logger);

internal sealed class PostgreSqlMcpClient(
    RuntimeOptions runtimeOptions,
    IDatabaseMcpFallbackExecutor fallbackExecutor,
    ILogger<DatabaseMcpClient> logger)
    : DatabaseMcpClient(DatabaseEngine.PostgreSql, runtimeOptions.Mcp.PostgreSql, runtimeOptions.Mcp, fallbackExecutor, logger);
