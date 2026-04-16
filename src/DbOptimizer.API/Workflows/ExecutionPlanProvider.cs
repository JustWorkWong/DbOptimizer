using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MySqlConnector;
using Npgsql;

namespace DbOptimizer.API.Workflows;

internal interface IExecutionPlanProvider
{
    Task<ExecutionPlanInvocationResult> ExplainAsync(
        DatabaseOptimizationEngine databaseEngine,
        string sqlText,
        CancellationToken cancellationToken = default);
}

/* =========================
 * ExecutionPlan 获取器
 * 先走 MCP explain，失败后按配置决定是否降级为数据库直连 explain。
 * 这里优先保证 M3-03 功能闭环，后续如果要和 AgentRuntime 共用基础设施可以再抽公共层。
 * ========================= */
internal sealed class ExecutionPlanProvider(
    IConfiguration configuration,
    ExecutionPlanOptions executionPlanOptions,
    ILogger<ExecutionPlanProvider> logger) : IExecutionPlanProvider
{
    private static readonly string[] ExplainToolAliases = ["explain", "get_execution_plan", "GetExecutionPlan"];

    public async Task<ExecutionPlanInvocationResult> ExplainAsync(
        DatabaseOptimizationEngine databaseEngine,
        string sqlText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serverOptions = ResolveServerOptions(databaseEngine);
        Exception? lastException = null;
        string? diagnosticTag = null;

        if (serverOptions.Enabled)
        {
            for (var attempt = 1; attempt <= executionPlanOptions.RetryCount + 1; attempt++)
            {
                try
                {
                    return await InvokeMcpAsync(serverOptions, sqlText, attempt, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    diagnosticTag = ex is TimeoutException ? "mcp_timeout" : "mcp_error";

                    logger.LogWarning(
                        ex,
                        "Execution plan MCP 调用失败。DatabaseEngine={DatabaseEngine}, Attempt={Attempt}/{MaxAttempts}",
                        databaseEngine,
                        attempt,
                        executionPlanOptions.RetryCount + 1);
                }

                if (attempt <= executionPlanOptions.RetryCount && executionPlanOptions.RetryDelayMilliseconds > 0)
                {
                    var delay = executionPlanOptions.RetryDelayMilliseconds * (int)Math.Pow(2, attempt - 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                }
            }
        }
        else
        {
            diagnosticTag = "mcp_disabled";
        }

        if (!executionPlanOptions.EnableDirectDbFallback)
        {
            throw new InvalidOperationException(
                $"Execution plan MCP 调用失败，且未开启直连降级。DatabaseEngine={databaseEngine}",
                lastException);
        }

        return await ExecuteFallbackAsync(
            databaseEngine,
            sqlText,
            executionPlanOptions.RetryCount + 1,
            diagnosticTag ?? "mcp_failure",
            cancellationToken);
    }

    private async Task<ExecutionPlanInvocationResult> InvokeMcpAsync(
        ExecutionPlanMcpServerOptions serverOptions,
        string sqlText,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(executionPlanOptions.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var stopwatch = Stopwatch.StartNew();

        if (!serverOptions.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"暂不支持 MCP transport: {serverOptions.Transport}");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DbOptimizer ExecutionPlan MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments)
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync(cancellationToken: linkedCts.Token);
        var toolName = ResolveExplainToolName(tools.Select(tool => tool.Name));
        var result = await client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>
            {
                ["sql"] = sqlText,
                ["query"] = sqlText
            },
            cancellationToken: linkedCts.Token);

        var textBlocks = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var rawText = textBlocks.Length > 0
            ? string.Join(Environment.NewLine, textBlocks)
            : JsonSerializer.Serialize(result.Content);

        if (result.IsError == true)
        {
            throw new InvalidOperationException($"MCP explain 返回错误：{rawText}");
        }

        return new ExecutionPlanInvocationResult
        {
            ToolName = toolName,
            RawText = rawText,
            AttemptCount = attemptCount,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task<ExecutionPlanInvocationResult> ExecuteFallbackAsync(
        DatabaseOptimizationEngine databaseEngine,
        string sqlText,
        int attemptCount,
        string diagnosticTag,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var rawText = databaseEngine switch
        {
            DatabaseOptimizationEngine.PostgreSql => await ExplainPostgreSqlAsync(sqlText, cancellationToken),
            DatabaseOptimizationEngine.MySql => await ExplainMySqlAsync(sqlText, cancellationToken),
            _ => throw new InvalidOperationException("Unknown database engine for execution plan fallback.")
        };

        logger.LogWarning(
            "Execution plan 走数据库直连降级。DatabaseEngine={DatabaseEngine}, DiagnosticTag={DiagnosticTag}",
            databaseEngine,
            diagnosticTag);

        return new ExecutionPlanInvocationResult
        {
            ToolName = "explain",
            RawText = rawText,
            UsedFallback = true,
            AttemptCount = attemptCount,
            DiagnosticTag = diagnosticTag,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task<string> ExplainPostgreSqlAsync(string sqlText, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ResolvePostgreSqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        return await ExecuteReaderAsync(
            connection,
            $"EXPLAIN (FORMAT JSON) {sqlText}",
            executionPlanOptions.TimeoutSeconds,
            cancellationToken);
    }

    private async Task<string> ExplainMySqlAsync(string sqlText, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ResolveMySqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        return await ExecuteReaderAsync(
            connection,
            $"EXPLAIN {sqlText}",
            executionPlanOptions.TimeoutSeconds,
            cancellationToken);
    }

    private static async Task<string> ExecuteReaderAsync(
        DbConnection connection,
        string sqlText,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sqlText;
        command.CommandTimeout = timeoutSeconds;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private ExecutionPlanMcpServerOptions ResolveServerOptions(DatabaseOptimizationEngine databaseEngine)
    {
        return databaseEngine switch
        {
            DatabaseOptimizationEngine.MySql => executionPlanOptions.MySql,
            DatabaseOptimizationEngine.PostgreSql => executionPlanOptions.PostgreSql,
            _ => throw new InvalidOperationException("Unknown database engine for execution plan MCP call.")
        };
    }

    private string ResolvePostgreSqlConnectionString()
    {
        var fromAspire = configuration.GetConnectionString("dboptimizer-postgres");
        if (!string.IsNullOrWhiteSpace(fromAspire))
        {
            return fromAspire;
        }

        var fallback = configuration.GetConnectionString("PostgreSql");
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        var nested = configuration["DbOptimizer:ConnectionStrings:PostgreSql"];
        if (!string.IsNullOrWhiteSpace(nested))
        {
            return nested;
        }

        throw new InvalidOperationException("Missing PostgreSQL connection string for execution plan explain.");
    }

    private string ResolveMySqlConnectionString()
    {
        var fromAspire = configuration.GetConnectionString("dboptimizer-mysql");
        if (!string.IsNullOrWhiteSpace(fromAspire))
        {
            return fromAspire;
        }

        var fallback = configuration.GetConnectionString("MySql");
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        var nested = configuration["DbOptimizer:ConnectionStrings:MySql"];
        if (!string.IsNullOrWhiteSpace(nested))
        {
            return nested;
        }

        throw new InvalidOperationException("Missing MySQL connection string for execution plan explain.");
    }

    private static string ResolveExplainToolName(IEnumerable<string> toolNames)
    {
        var availableTools = toolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in ExplainToolAliases)
        {
            if (availableTools.Contains(alias))
            {
                return alias;
            }
        }

        throw new InvalidOperationException($"No explain tool alias matched. Available tools: {string.Join(", ", availableTools.OrderBy(name => name))}");
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
