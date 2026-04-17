using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DbOptimizer.Infrastructure.Mcp;

public enum DatabaseEngine
{
    MySql,
    PostgreSql
}

public interface IMcpClientFactory
{
    IDatabaseMcpClient Create(DatabaseEngine databaseEngine);
}

public interface IDatabaseMcpClient
{
    DatabaseEngine DatabaseEngine { get; }

    Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> QueryAsync(string sql, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> ExplainAsync(string sql, CancellationToken cancellationToken = default);

    Task<McpToolInvocationResult> ShowIndexesAsync(string tableName, CancellationToken cancellationToken = default);
}

public sealed record McpToolInvocationResult(
    string ToolName,
    IReadOnlyList<string> TextBlocks,
    string RawText,
    bool IsError,
    bool UsedFallback,
    int AttemptCount,
    string? DiagnosticTag,
    long ElapsedMs)
{
    public static McpToolInvocationResult FromCallResult(
        string toolName,
        CallToolResult result,
        int attemptCount,
        string? diagnosticTag = null,
        long elapsedMs = 0)
    {
        var textBlocks = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var rawText = textBlocks.Length > 0
            ? string.Join(Environment.NewLine, textBlocks)
            : System.Text.Json.JsonSerializer.Serialize(result.Content);

        return new McpToolInvocationResult(
            toolName,
            textBlocks,
            rawText,
            result.IsError ?? false,
            UsedFallback: false,
            AttemptCount: attemptCount,
            DiagnosticTag: diagnosticTag,
            ElapsedMs: elapsedMs);
    }

    public static McpToolInvocationResult FromFallback(
        string toolName,
        string rawText,
        int attemptCount,
        string diagnosticTag,
        long elapsedMs)
    {
        var textBlocks = string.IsNullOrWhiteSpace(rawText)
            ? Array.Empty<string>()
            : [rawText];

        return new McpToolInvocationResult(
            toolName,
            textBlocks,
            rawText,
            IsError: false,
            UsedFallback: true,
            AttemptCount: attemptCount,
            DiagnosticTag: diagnosticTag,
            ElapsedMs: elapsedMs);
    }
}
