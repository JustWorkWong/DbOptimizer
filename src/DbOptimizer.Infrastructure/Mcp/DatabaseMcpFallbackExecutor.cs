using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace DbOptimizer.Infrastructure.Mcp;

public interface IDatabaseMcpFallbackExecutor
{
    Task<McpToolInvocationResult> ExecuteAsync(
        DatabaseEngine databaseEngine,
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        int attemptCount,
        string diagnosticTag,
        CancellationToken cancellationToken = default);
}

/* =========================
 * MCP 直连降级执行器
 * 设计目标：
 * 1) 在 MCP 超时或连接异常时，提供受配置控制的直连数据库兜底能力
 * 2) 仅覆盖当前任务要求的四类工具：query / describe / explain / show_indexes
 * 3) 所有降级结果统一序列化为文本，便于后续审计与展示复用
 * ========================= */
public sealed class DatabaseMcpFallbackExecutor(
    McpFallbackOptions fallbackOptions,
    McpOptions mcpOptions,
    ILogger<DatabaseMcpFallbackExecutor> logger) : IDatabaseMcpFallbackExecutor
{
    public async Task<McpToolInvocationResult> ExecuteAsync(
        DatabaseEngine databaseEngine,
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        int attemptCount,
        string diagnosticTag,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var rawText = databaseEngine switch
        {
            DatabaseEngine.MySql => await ExecuteWithMySqlAsync(toolKind, arguments, cancellationToken),
            DatabaseEngine.PostgreSql => await ExecuteWithPostgreSqlAsync(toolKind, arguments, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseEngine), databaseEngine, "Unsupported database engine.")
        };

        logger.LogWarning(
            "MCP fallback executed for {DatabaseEngine}/{ToolKind}. DiagnosticTag={DiagnosticTag}, AttemptCount={AttemptCount}.",
            databaseEngine,
            toolKind,
            diagnosticTag,
            attemptCount);

        return McpToolInvocationResult.FromFallback(
            toolKind.ToString(),
            rawText,
            attemptCount,
            diagnosticTag,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<string> ExecuteWithMySqlAsync(
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(fallbackOptions.MySqlConnectionString);
        await connection.OpenAsync(cancellationToken);

        return toolKind switch
        {
            McpToolKind.Query => await ExecuteReaderAsync(
                connection,
                RequireSql(arguments),
                command => command.CommandTimeout = mcpOptions.TimeoutSeconds,
                cancellationToken),
            McpToolKind.DescribeTable => await ExecuteReaderAsync(
                connection,
                """
                SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION;
                """,
                command =>
                {
                    command.CommandTimeout = mcpOptions.TimeoutSeconds;
                    command.Parameters.Add(new MySqlParameter("@tableName", RequireTableName(arguments)));
                },
                cancellationToken),
            McpToolKind.Explain => await ExecuteReaderAsync(
                connection,
                $"EXPLAIN {RequireSql(arguments)}",
                command => command.CommandTimeout = mcpOptions.TimeoutSeconds,
                cancellationToken),
            McpToolKind.ShowIndexes => await ExecuteReaderAsync(
                connection,
                """
                SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE, INDEX_TYPE, SEQ_IN_INDEX
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                ORDER BY INDEX_NAME, SEQ_IN_INDEX;
                """,
                command =>
                {
                    command.CommandTimeout = mcpOptions.TimeoutSeconds;
                    command.Parameters.Add(new MySqlParameter("@tableName", RequireTableName(arguments)));
                },
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(toolKind), toolKind, "Unsupported MCP tool kind.")
        };
    }

    private async Task<string> ExecuteWithPostgreSqlAsync(
        McpToolKind toolKind,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(fallbackOptions.PostgreSqlConnectionString);
        await connection.OpenAsync(cancellationToken);

        return toolKind switch
        {
            McpToolKind.Query => await ExecuteReaderAsync(
                connection,
                RequireSql(arguments),
                command => command.CommandTimeout = mcpOptions.TimeoutSeconds,
                cancellationToken),
            McpToolKind.DescribeTable => await ExecuteReaderAsync(
                connection,
                """
                SELECT column_name, data_type, is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = @tableName
                ORDER BY ordinal_position;
                """,
                command =>
                {
                    command.CommandTimeout = mcpOptions.TimeoutSeconds;
                    command.Parameters.Add(new NpgsqlParameter("@tableName", RequireTableName(arguments)));
                },
                cancellationToken),
            McpToolKind.Explain => await ExecuteReaderAsync(
                connection,
                $"EXPLAIN (FORMAT JSON) {RequireSql(arguments)}",
                command => command.CommandTimeout = mcpOptions.TimeoutSeconds,
                cancellationToken),
            McpToolKind.ShowIndexes => await ExecuteReaderAsync(
                connection,
                """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = @tableName
                ORDER BY indexname;
                """,
                command =>
                {
                    command.CommandTimeout = mcpOptions.TimeoutSeconds;
                    command.Parameters.Add(new NpgsqlParameter("@tableName", RequireTableName(arguments)));
                },
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(toolKind), toolKind, "Unsupported MCP tool kind.")
        };
    }

    private static async Task<string> ExecuteReaderAsync(
        DbConnection connection,
        string sql,
        Action<DbCommand> configureCommand,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configureCommand(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken)
                    ? null
                    : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private static string RequireSql(IReadOnlyDictionary<string, object?> arguments)
    {
        return RequireString(arguments, "sql", "query");
    }

    private static string RequireTableName(IReadOnlyDictionary<string, object?> arguments)
    {
        return RequireString(arguments, "tableName", "table", "name");
    }

    private static string RequireString(IReadOnlyDictionary<string, object?> arguments, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (arguments.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        throw new InvalidOperationException($"Missing required MCP fallback argument. Expected one of: {string.Join(", ", keys)}.");
    }
}
