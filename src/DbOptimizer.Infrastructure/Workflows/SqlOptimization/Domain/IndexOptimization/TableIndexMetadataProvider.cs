using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MySqlConnector;
using Npgsql;

namespace DbOptimizer.Infrastructure.Workflows;

public interface ITableIndexMetadataProvider
{
    Task<IndexMetadataInvocationResult> GetIndexesAsync(
        DatabaseOptimizationEngine databaseEngine,
        string tableName,
        CancellationToken cancellationToken = default);
}

/* =========================
 * 表索引元数据获取器
 * 规则与 ExecutionPlanProvider 保持一致：先 MCP，再按配置降级直连。
 * ========================= */
public sealed class TableIndexMetadataProvider(
    IConfiguration configuration,
    ExecutionPlanOptions executionPlanOptions,
    ILogger<TableIndexMetadataProvider> logger) : ITableIndexMetadataProvider
{
    private static readonly string[] ShowIndexesToolAliases = ["show_indexes", "get_table_indexes", "GetTableIndexes"];

    public async Task<IndexMetadataInvocationResult> GetIndexesAsync(
        DatabaseOptimizationEngine databaseEngine,
        string tableName,
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
                    return await InvokeMcpAsync(serverOptions, tableName, attempt, cancellationToken);
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
                        "表索引 MCP 调用失败。DatabaseEngine={DatabaseEngine}, Table={TableName}, Attempt={Attempt}/{MaxAttempts}",
                        databaseEngine,
                        tableName,
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
                $"表索引 MCP 调用失败，且未开启直连降级。DatabaseEngine={databaseEngine}, Table={tableName}",
                lastException);
        }

        return await ExecuteFallbackAsync(
            databaseEngine,
            tableName,
            executionPlanOptions.RetryCount + 1,
            diagnosticTag ?? "mcp_failure",
            cancellationToken);
    }

    private async Task<IndexMetadataInvocationResult> InvokeMcpAsync(
        ExecutionPlanMcpServerOptions serverOptions,
        string tableName,
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
            Name = "DbOptimizer ShowIndexes MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments)
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync(cancellationToken: linkedCts.Token);
        var toolName = ResolveToolName(tools.Select(tool => tool.Name));
        var result = await client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>
            {
                ["tableName"] = tableName,
                ["table"] = tableName,
                ["name"] = tableName
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
            throw new InvalidOperationException($"MCP show_indexes 返回错误：{rawText}");
        }

        return new IndexMetadataInvocationResult
        {
            ToolName = toolName,
            RawText = rawText,
            AttemptCount = attemptCount,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task<IndexMetadataInvocationResult> ExecuteFallbackAsync(
        DatabaseOptimizationEngine databaseEngine,
        string tableName,
        int attemptCount,
        string diagnosticTag,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var rawText = databaseEngine switch
        {
            DatabaseOptimizationEngine.PostgreSql => await LoadPostgreSqlIndexesAsync(tableName, cancellationToken),
            DatabaseOptimizationEngine.MySql => await LoadMySqlIndexesAsync(tableName, cancellationToken),
            _ => throw new InvalidOperationException("Unknown database engine for index metadata fallback.")
        };

        logger.LogWarning(
            "表索引读取走数据库直连降级。DatabaseEngine={DatabaseEngine}, Table={TableName}, DiagnosticTag={DiagnosticTag}",
            databaseEngine,
            tableName,
            diagnosticTag);

        return new IndexMetadataInvocationResult
        {
            ToolName = "show_indexes",
            RawText = rawText,
            UsedFallback = true,
            AttemptCount = attemptCount,
            DiagnosticTag = diagnosticTag,
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    private async Task<string> LoadPostgreSqlIndexesAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ResolvePostgreSqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        return await ExecuteReaderAsync(
            connection,
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = @tableName
            ORDER BY indexname;
            """,
            executionPlanOptions.TimeoutSeconds,
            command => command.Parameters.Add(new NpgsqlParameter("@tableName", tableName)),
            cancellationToken);
    }

    private async Task<string> LoadMySqlIndexesAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(ResolveMySqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        return await ExecuteReaderAsync(
            connection,
            """
            SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE, INDEX_TYPE, SEQ_IN_INDEX
            FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tableName
            ORDER BY INDEX_NAME, SEQ_IN_INDEX;
            """,
            executionPlanOptions.TimeoutSeconds,
            command => command.Parameters.Add(new MySqlParameter("@tableName", tableName)),
            cancellationToken);
    }

    private static async Task<string> ExecuteReaderAsync(
        DbConnection connection,
        string sqlText,
        int timeoutSeconds,
        Action<DbCommand> configureCommand,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sqlText;
        command.CommandTimeout = timeoutSeconds;
        configureCommand(command);

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
            _ => throw new InvalidOperationException("Unknown database engine for index metadata MCP call.")
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

        throw new InvalidOperationException("Missing PostgreSQL connection string for show_indexes.");
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

        throw new InvalidOperationException("Missing MySQL connection string for show_indexes.");
    }

    private static string ResolveToolName(IEnumerable<string> toolNames)
    {
        var availableTools = toolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in ShowIndexesToolAliases)
        {
            if (availableTools.Contains(alias))
            {
                return alias;
            }
        }

        throw new InvalidOperationException($"No show_indexes tool alias matched. Available tools: {string.Join(", ", availableTools.OrderBy(name => name))}");
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
