using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DbOptimizer.AgentRuntime;

internal enum DatabaseEngine
{
    MySql,
    PostgreSql
}

internal interface IMcpClientFactory
{
    IDatabaseMcpClient Create(DatabaseEngine databaseEngine);
}

internal interface IDatabaseMcpClient
{
    DatabaseEngine DatabaseEngine { get; }

    Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> QueryAsync(string sql, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> ExplainAsync(string sql, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> ShowIndexesAsync(string tableName, CancellationToken cancellationToken = default);
}

internal sealed record McpToolInvocationResult(
    string ToolName,
    IReadOnlyList<string> TextBlocks,
    string RawText,
    bool IsError)
{
    public static McpToolInvocationResult FromCallResult(string toolName, CallToolResult result)
    {
        var textBlocks = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var rawText = textBlocks.Length > 0
            ? string.Join(Environment.NewLine, textBlocks)
            : System.Text.Json.JsonSerializer.Serialize(result.Content);

        return new McpToolInvocationResult(toolName, textBlocks, rawText, result.IsError ?? false);
    }
}
