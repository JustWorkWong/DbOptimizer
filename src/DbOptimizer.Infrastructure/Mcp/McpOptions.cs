namespace DbOptimizer.Infrastructure.Mcp;

public sealed record McpOptions
{
    public required McpServerOptions MySql { get; init; }

    public required McpServerOptions PostgreSql { get; init; }

    public int TimeoutSeconds { get; init; }

    public int RetryCount { get; init; }

    public int RetryDelayMilliseconds { get; init; } = 1_000;

    public bool EnableDirectDbFallback { get; init; } = true;

    public bool EnableAuditLogging { get; init; } = true;
}

public sealed record McpServerOptions
{
    public bool Enabled { get; init; }

    public required string Transport { get; init; }

    public required string Command { get; init; }

    public required string Arguments { get; init; }
}

public enum McpToolKind
{
    Query,
    DescribeTable,
    Explain,
    ShowIndexes
}
