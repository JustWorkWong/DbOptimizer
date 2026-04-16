using System.Text.Json;
using DbOptimizer.API.Workflows;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DbOptimizer.API.SlowQuery;

/* =========================
 * 慢查询采集器实现
 * 职责：
 * 1) 通过 MCP 调用目标数据库的慢查询接口
 * 2) MySQL: 查询 mysql.slow_log 表（最近 N 条）
 * 3) PostgreSQL: 查询 pg_stat_statements 视图（按平均执行时间排序）
 * 4) 错误处理：MCP 失败时返回空列表并记录日志
 * ========================= */
internal sealed class SlowQueryCollector(
    IConfiguration configuration,
    ExecutionPlanOptions executionPlanOptions,
    SlowQueryCollectionOptions collectionOptions,
    ILogger<SlowQueryCollector> logger) : ISlowQueryCollector
{
    public async Task<IReadOnlyList<RawSlowQuery>> CollectAsync(
        string databaseId,
        DatabaseOptimizationEngine databaseType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serverOptions = ResolveServerOptions(databaseType);
        if (!serverOptions.Enabled)
        {
            logger.LogWarning("MCP 未启用。DatabaseId={DatabaseId}, DatabaseType={DatabaseType}", databaseId, databaseType);
            return Array.Empty<RawSlowQuery>();
        }

        try
        {
            return await InvokeMcpAsync(serverOptions, databaseId, databaseType, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "慢查询采集失败。DatabaseId={DatabaseId}, DatabaseType={DatabaseType}", databaseId, databaseType);
            return Array.Empty<RawSlowQuery>();
        }
    }

    private async Task<IReadOnlyList<RawSlowQuery>> InvokeMcpAsync(
        ExecutionPlanMcpServerOptions serverOptions,
        string databaseId,
        DatabaseOptimizationEngine databaseType,
        CancellationToken cancellationToken)
    {
        var connectionString = ResolveConnectionString(databaseId);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DbOptimizer SlowQuery MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = connectionString
            }
        });

        await using var client = await McpClient.CreateAsync(transport);

        var sql = BuildSlowQuerySql(databaseType);
        var arguments = new Dictionary<string, object?>
        {
            ["sql"] = sql
        };

        var result = await client.CallToolAsync(
            "query",
            arguments,
            cancellationToken: cancellationToken);

        return ParseMcpResponse(result, databaseType);
    }

    private string BuildSlowQuerySql(DatabaseOptimizationEngine databaseType)
    {
        var limit = collectionOptions.MaxCollectionCount;
        var thresholdSeconds = collectionOptions.SlowThresholdMs / 1000.0;

        return databaseType switch
        {
            DatabaseOptimizationEngine.MySql => $@"
                SELECT
                    sql_text,
                    query_time,
                    start_time,
                    user_host,
                    db,
                    rows_examined,
                    rows_sent
                FROM mysql.slow_log
                WHERE query_time >= {thresholdSeconds}
                ORDER BY start_time DESC
                LIMIT {limit}",

            DatabaseOptimizationEngine.PostgreSql => $@"
                SELECT
                    query AS sql_text,
                    mean_exec_time / 1000.0 AS query_time,
                    NOW() AS start_time,
                    '' AS user_host,
                    '' AS db,
                    0 AS rows_examined,
                    rows AS rows_sent
                FROM pg_stat_statements
                WHERE mean_exec_time >= {thresholdSeconds * 1000}
                ORDER BY mean_exec_time DESC
                LIMIT {limit}",

            _ => throw new NotSupportedException($"不支持的数据库类型: {databaseType}")
        };
    }

    private IReadOnlyList<RawSlowQuery> ParseMcpResponse(
        CallToolResult result,
        DatabaseOptimizationEngine databaseType)
    {
        if (result.IsError == true)
        {
            logger.LogWarning("MCP 返回错误。Content={Content}", JsonSerializer.Serialize(result.Content));
            return Array.Empty<RawSlowQuery>();
        }

        var results = new List<RawSlowQuery>();
        var textBlocks = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        foreach (var text in textBlocks)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(text);
                var rows = jsonDoc.RootElement.EnumerateArray();

                foreach (var row in rows)
                {
                    var rawQuery = ParseRow(row, databaseType);
                    if (rawQuery != null)
                    {
                        results.Add(rawQuery);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "解析慢查询响应失败。Content={Content}", text);
            }
        }

        return results;
    }

    private RawSlowQuery? ParseRow(JsonElement row, DatabaseOptimizationEngine databaseType)
    {
        try
        {
            var sqlText = row.TryGetProperty("sql_text", out var sqlProp) ? sqlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(sqlText))
            {
                return null;
            }

            var queryTime = row.TryGetProperty("query_time", out var timeProp)
                ? timeProp.GetDouble()
                : 0;

            var executedAt = row.TryGetProperty("start_time", out var startProp)
                ? DateTimeOffset.Parse(startProp.GetString() ?? DateTimeOffset.UtcNow.ToString())
                : DateTimeOffset.UtcNow;

            var userName = row.TryGetProperty("user_host", out var userProp)
                ? userProp.GetString()
                : null;

            var databaseName = row.TryGetProperty("db", out var dbProp)
                ? dbProp.GetString()
                : null;

            var rowsExamined = row.TryGetProperty("rows_examined", out var examinedProp)
                ? examinedProp.GetInt64()
                : 0;

            var rowsSent = row.TryGetProperty("rows_sent", out var sentProp)
                ? sentProp.GetInt64()
                : 0;

            return new RawSlowQuery
            {
                SqlText = sqlText,
                ExecutionTime = TimeSpan.FromSeconds(queryTime),
                ExecutedAt = executedAt,
                UserName = userName,
                DatabaseName = databaseName,
                RowsExamined = rowsExamined,
                RowsSent = rowsSent
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解析慢查询行失败");
            return null;
        }
    }

    private ExecutionPlanMcpServerOptions ResolveServerOptions(DatabaseOptimizationEngine databaseType)
    {
        return databaseType switch
        {
            DatabaseOptimizationEngine.MySql => executionPlanOptions.MySql,
            DatabaseOptimizationEngine.PostgreSql => executionPlanOptions.PostgreSql,
            _ => throw new NotSupportedException($"不支持的数据库类型: {databaseType}")
        };
    }

    private string ResolveConnectionString(string databaseId)
    {
        var key = $"TargetDatabases:{databaseId}";
        var connectionString = configuration[key];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"未找到数据库连接字符串: {key}");
        }

        return connectionString;
    }

    private static string[] ParseArguments(string argumentsString)
    {
        if (string.IsNullOrWhiteSpace(argumentsString))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in argumentsString)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments.ToArray();
    }
}
