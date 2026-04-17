using Microsoft.Extensions.Configuration;

namespace DbOptimizer.Infrastructure.Mcp;

public sealed record McpFallbackOptions
{
    public required string MySqlConnectionString { get; init; }

    public required string PostgreSqlConnectionString { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}
