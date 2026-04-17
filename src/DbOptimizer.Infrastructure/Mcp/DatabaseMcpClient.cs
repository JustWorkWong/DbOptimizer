using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace DbOptimizer.Infrastructure.Mcp;

/* =========================
 * 数据库 MCP 客户端
 * 设计目标：
 * 1) 统一封装 MySQL / PostgreSQL 的 MCP 调用入口
 * 2) 兼容文档中工具命名不完全一致的情况，通过别名自动匹配真实工具名
 * 3) 为 M2-02 的超时 / 重试 / 降级预留稳定包裹点
 * ========================= */
public class DatabaseMcpClient(
    DatabaseEngine databaseEngine,
    McpServerOptions serverOptions,
    McpOptions mcpOptions,
    IDatabaseMcpFallbackExecutor fallbackExecutor,
    ILogger<DatabaseMcpClient> logger) : IDatabaseMcpClient, IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<McpToolKind, string[]> ToolAliases = new Dictionary<McpToolKind, string[]>
    {
        [McpToolKind.Query] = ["query", "execute_query", "run_query"],
        [McpToolKind.DescribeTable] = ["describe_table", "describe", "get_table_stats", "GetTableStats"],
        [McpToolKind.Explain] = ["explain", "get_execution_plan", "GetExecutionPlan"],
        [McpToolKind.ShowIndexes] = ["show_indexes", "get_table_indexes", "GetTableIndexes"]
    };

    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private readonly ConcurrentDictionary<McpToolKind, string> _resolvedTools = new();
    private McpClient? _client;

    public DatabaseEngine DatabaseEngine => databaseEngine;

    public async Task<IReadOnlyList<string>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = await GetClientAsync(cancellationToken);
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => tool.Name).ToArray();
    }

    public Task<McpToolInvocationResult> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            McpToolKind.Query,
            new Dictionary<string, object?>
            {
                ["sql"] = sql,
                ["query"] = sql
            },
            cancellationToken);
    }

    public Task<McpToolInvocationResult> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            McpToolKind.DescribeTable,
            new Dictionary<string, object?>
            {
                ["tableName"] = tableName,
                ["table"] = tableName,
                ["name"] = tableName
            },
            cancellationToken);
    }

    public Task<McpToolInvocationResult> ExplainAsync(string sql, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            McpToolKind.Explain,
            new Dictionary<string, object?>
            {
                ["sql"] = sql,
                ["query"] = sql
            },
            cancellationToken);
    }

    public Task<McpToolInvocationResult> ShowIndexesAsync(string tableName, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            McpToolKind.ShowIndexes,
            new Dictionary<string, object?>
            {
                ["tableName"] = tableName,
                ["table"] = tableName,
                ["name"] = tableName
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _clientLock.Dispose();

        if (_client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    private async Task<McpToolInvocationResult> ExecuteAsync(
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!serverOptions.Enabled)
        {
            if (!mcpOptions.EnableDirectDbFallback)
            {
                throw new InvalidOperationException($"MCP client for {databaseEngine} is disabled and direct fallback is disabled.");
            }

            return await ExecuteFallbackAsync(
                toolKind,
                arguments,
                attemptCount: 0,
                diagnosticTag: "mcp_disabled",
                cancellationToken);
        }

        var attemptCount = 0;
        Exception? lastException = null;
        string? lastDiagnosticTag = null;

        while (attemptCount <= mcpOptions.RetryCount)
        {
            attemptCount++;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(mcpOptions.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var client = await GetClientAsync(linkedCts.Token);
                var toolName = await ResolveToolNameAsync(client, toolKind, linkedCts.Token);

                logger.LogInformation(
                    "Calling MCP tool {ToolName} for database {DatabaseEngine}. Attempt={Attempt}.",
                    toolName,
                    databaseEngine,
                    attemptCount);

                var result = await client.CallToolAsync(toolName, arguments, cancellationToken: linkedCts.Token);
                var invocationResult = McpToolInvocationResult.FromCallResult(
                    toolName,
                    result,
                    attemptCount,
                    elapsedMs: stopwatch.ElapsedMilliseconds);

                WriteAuditLog(
                    "mcp_call_succeeded",
                    toolKind,
                    invocationResult.AttemptCount,
                    invocationResult.UsedFallback,
                    invocationResult.DiagnosticTag);

                return invocationResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "MCP call canceled by caller for {DatabaseEngine}/{ToolKind}.",
                    databaseEngine,
                    toolKind);
                throw;
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                lastException = ex;
                lastDiagnosticTag = "mcp_timeout";

                logger.LogWarning(
                    ex,
                    "MCP timeout for {DatabaseEngine}/{ToolKind}. Attempt={Attempt}/{MaxAttempts}.",
                    databaseEngine,
                    toolKind,
                    attemptCount,
                    mcpOptions.RetryCount + 1);
            }
            catch (Exception ex) when (!IsPermissionError(ex))
            {
                lastException = ex;
                lastDiagnosticTag = IsDeterministicFailure(ex) ? "mcp_deterministic_error" : "mcp_error";

                logger.LogWarning(
                    ex,
                    "MCP call failed for {DatabaseEngine}/{ToolKind}. Attempt={Attempt}/{MaxAttempts}.",
                    databaseEngine,
                    toolKind,
                    attemptCount,
                    mcpOptions.RetryCount + 1);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "MCP permission-like failure for {DatabaseEngine}/{ToolKind}. Fallback is blocked.",
                    databaseEngine,
                    toolKind);
                throw;
            }

            if (lastException is not null && IsDeterministicFailure(lastException))
            {
                break;
            }

            if (attemptCount <= mcpOptions.RetryCount)
            {
                await DelayBeforeRetryAsync(attemptCount, cancellationToken);
            }
        }

        if (!mcpOptions.EnableDirectDbFallback)
        {
            throw new InvalidOperationException(
                $"MCP call failed for {databaseEngine}/{toolKind} and direct fallback is disabled.",
                lastException);
        }

        return await ExecuteFallbackAsync(
            toolKind,
            arguments,
            attemptCount,
            lastDiagnosticTag ?? "mcp_failure",
            cancellationToken);
    }

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var transport = CreateTransport();
            _client = await McpClient.CreateAsync(transport);
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private IClientTransport CreateTransport()
    {
        if (!serverOptions.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Transport '{serverOptions.Transport}' is not supported yet.");
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = $"{databaseEngine} MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments)
        });
    }

    private async Task<string> ResolveToolNameAsync(McpClient client, McpToolKind toolKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_resolvedTools.TryGetValue(toolKind, out var resolvedTool))
        {
            return resolvedTool;
        }

        var tools = await client.ListToolsAsync();
        var availableTools = tools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in ToolAliases[toolKind])
        {
            if (availableTools.Contains(alias))
            {
                _resolvedTools[toolKind] = alias;
                return alias;
            }
        }

        throw new InvalidOperationException(
            $"No MCP tool alias matched for {databaseEngine}/{toolKind}. Available tools: {string.Join(", ", availableTools.OrderBy(name => name))}");
    }

    private async Task DelayBeforeRetryAsync(int attemptCount, CancellationToken cancellationToken)
    {
        if (mcpOptions.RetryDelayMilliseconds <= 0)
        {
            return;
        }

        var delayMilliseconds = checked(mcpOptions.RetryDelayMilliseconds * (int)Math.Pow(2, attemptCount - 1));
        await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
    }

    private async Task<McpToolInvocationResult> ExecuteFallbackAsync(
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        int attemptCount,
        string diagnosticTag,
        CancellationToken cancellationToken)
    {
        var fallbackResult = await fallbackExecutor.ExecuteAsync(
            databaseEngine,
            toolKind,
            arguments,
            attemptCount,
            diagnosticTag,
            cancellationToken);

        WriteAuditLog(
            "mcp_call_fallback",
            toolKind,
            fallbackResult.AttemptCount,
            fallbackResult.UsedFallback,
            fallbackResult.DiagnosticTag);

        return fallbackResult;
    }

    private void WriteAuditLog(
        string eventName,
        McpToolKind toolKind,
        int attemptCount,
        bool usedFallback,
        string? diagnosticTag)
    {
        if (!mcpOptions.EnableAuditLogging)
        {
            return;
        }

        logger.LogInformation(
            "MCP audit event {EventName}. DatabaseEngine={DatabaseEngine}, ToolKind={ToolKind}, AttemptCount={AttemptCount}, UsedFallback={UsedFallback}, DiagnosticTag={DiagnosticTag}.",
            eventName,
            databaseEngine,
            toolKind,
            attemptCount,
            usedFallback,
            diagnosticTag ?? "-");
    }

    private static bool IsPermissionError(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access denied", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeterministicFailure(Exception exception)
    {
        if (exception is NotSupportedException)
        {
            return true;
        }

        return exception is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.Contains("No MCP tool alias matched", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ParseArguments(string rawArguments)
    {
        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in rawArguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                FlushArgument(arguments, current);
                continue;
            }

            current.Append(ch);
        }

        FlushArgument(arguments, current);
        return arguments.ToArray();
    }

    private static void FlushArgument(ICollection<string> arguments, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
    }
}
