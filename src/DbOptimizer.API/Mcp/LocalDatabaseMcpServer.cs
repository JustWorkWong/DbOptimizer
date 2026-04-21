using System.ComponentModel;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MySqlConnector;
using Npgsql;

namespace DbOptimizer.API.Mcp;

internal enum LocalDatabaseEngine
{
    MySql,
    PostgreSql
}

internal static class LocalDatabaseMcpServer
{
    public const string ModeArgument = "--mcp-server";

    public static bool TryParse(string[] args, out LocalDatabaseEngine engine)
    {
        engine = default;

        if (args.Length < 2 || !string.Equals(args[0], ModeArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        engine = ParseEngine(args[1]);
        return true;
    }

    public static async Task RunAsync(LocalDatabaseEngine engine, CancellationToken cancellationToken = default)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing required DATABASE_URL environment variable for local MCP server.");
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new LocalDatabaseMcpContext(engine, connectionString));
        builder.Services.AddSingleton<LocalDatabaseMcpTools>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<LocalDatabaseMcpTools>();

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
    }

    public static string BuildArguments(string assemblyPath, string engine)
    {
        return $"\"{assemblyPath}\" {ModeArgument} {engine}";
    }

    public static bool ShouldUseLocalServer(string command, string arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return true;
        }

        if (arguments.Contains(ModeArgument, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return arguments.Contains("@modelcontextprotocol/server-mysql", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("@modelcontextprotocol/server-postgres", StringComparison.OrdinalIgnoreCase);
    }

    private static LocalDatabaseEngine ParseEngine(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "mysql" => LocalDatabaseEngine.MySql,
            "postgres" or "postgresql" => LocalDatabaseEngine.PostgreSql,
            _ => throw new InvalidOperationException($"Unsupported MCP database engine: {value}")
        };
    }
}

internal sealed record LocalDatabaseMcpContext(LocalDatabaseEngine Engine, string ConnectionString);

internal sealed class LocalDatabaseMcpTools(LocalDatabaseMcpContext context)
{
    [McpServerTool, Description("Execute a read-only SQL query and return the result rows as JSON.")]
    public Task<string> query([Description("SQL query text.")] string sql)
        => ExecuteQueryAsync(sql);

    [McpServerTool, Description("Describe the columns of a table and return the schema rows as JSON.")]
    public Task<string> describe_table([Description("Target table name.")] string tableName)
        => context.Engine switch
        {
            LocalDatabaseEngine.MySql => ExecuteReaderAsync(
                """
                SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY, EXTRA, COLUMN_COMMENT
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION;
                """,
                command => command.Parameters.Add(new MySqlParameter("@tableName", tableName))),
            LocalDatabaseEngine.PostgreSql => ExecuteReaderAsync(
                """
                SELECT column_name, data_type, is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = @tableName
                ORDER BY ordinal_position;
                """,
                command => command.Parameters.Add(new NpgsqlParameter("@tableName", tableName))),
            _ => throw new InvalidOperationException($"Unsupported database engine: {context.Engine}")
        };

    [McpServerTool, Description("Explain a SQL statement and return the execution plan rows as JSON.")]
    public Task<string> explain([Description("SQL query text.")] string sql)
        => context.Engine switch
        {
            LocalDatabaseEngine.MySql => ExecuteExplainAsync($"EXPLAIN {sql}"),
            LocalDatabaseEngine.PostgreSql => ExecuteExplainAsync($"EXPLAIN (FORMAT JSON) {sql}"),
            _ => throw new InvalidOperationException($"Unsupported database engine: {context.Engine}")
        };

    [McpServerTool, Description("Show table indexes and return index metadata rows as JSON.")]
    public Task<string> show_indexes([Description("Target table name.")] string tableName)
        => context.Engine switch
        {
            LocalDatabaseEngine.MySql => ExecuteReaderAsync(
                """
                SELECT INDEX_NAME, COLUMN_NAME, NON_UNIQUE, INDEX_TYPE, SEQ_IN_INDEX
                FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = @tableName
                ORDER BY INDEX_NAME, SEQ_IN_INDEX;
                """,
                command => command.Parameters.Add(new MySqlParameter("@tableName", tableName))),
            LocalDatabaseEngine.PostgreSql => ExecuteReaderAsync(
                """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND tablename = @tableName
                ORDER BY indexname;
                """,
                command => command.Parameters.Add(new NpgsqlParameter("@tableName", tableName))),
            _ => throw new InvalidOperationException($"Unsupported database engine: {context.Engine}")
        };

    [McpServerTool, Description("Collect database configuration parameters and runtime metrics as JSON.")]
    public Task<string> get_config()
        => context.Engine switch
        {
            LocalDatabaseEngine.MySql => CollectMySqlConfigAsync(),
            LocalDatabaseEngine.PostgreSql => CollectPostgreSqlConfigAsync(),
            _ => throw new InvalidOperationException($"Unsupported database engine: {context.Engine}")
        };

    private async Task<string> ExecuteQueryAsync(string sql, bool allowExplainPrefix = false)
    {
        if (!allowExplainPrefix)
        {
            EnsureReadOnlySql(sql);
        }

        return await ExecuteReaderAsync(sql, _ => { });
    }

    private async Task<string> ExecuteExplainAsync(string sql)
    {
        try
        {
            var plan = await ExecuteQueryAsync(sql, allowExplainPrefix: true);
            using var document = JsonDocument.Parse(plan);

            return JsonSerializer.Serialize(new
            {
                ok = true,
                plan = document.RootElement.Clone()
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = ex.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    private async Task<string> CollectMySqlConfigAsync()
    {
        var parametersJson = await ExecuteReaderAsync("SHOW VARIABLES;", _ => { });
        var versionJson = await ExecuteScalarRowAsync("SELECT VERSION() AS database_version;", _ => { });
        var uptimeJson = await ExecuteReaderAsync("SHOW GLOBAL STATUS LIKE 'Uptime';", _ => { });
        var activeConnectionsJson = await ExecuteReaderAsync("SHOW GLOBAL STATUS LIKE 'Threads_connected';", _ => { });
        var maxConnectionsJson = await ExecuteReaderAsync("SHOW VARIABLES LIKE 'max_connections';", _ => { });

        using var parameters = JsonDocument.Parse(parametersJson);
        using var version = JsonDocument.Parse(versionJson);
        using var uptime = JsonDocument.Parse(uptimeJson);
        using var activeConnections = JsonDocument.Parse(activeConnectionsJson);
        using var maxConnections = JsonDocument.Parse(maxConnectionsJson);

        var payload = new
        {
            parameters = parameters.RootElement.EnumerateArray()
                .Select(item => new
                {
                    name = item.GetProperty("Variable_name").GetString() ?? string.Empty,
                    value = item.GetProperty("Value").GetString() ?? string.Empty,
                    default_value = string.Empty,
                    description = string.Empty,
                    is_dynamic = false,
                    type = string.Empty,
                    min_value = (string?)null,
                    max_value = (string?)null
                })
                .ToArray(),
            metrics = BuildMetrics(
                databaseVersion: GetString(version.RootElement, "database_version"),
                uptimeSeconds: ParseStatusValue(uptime.RootElement),
                activeConnections: (int)ParseStatusValue(activeConnections.RootElement),
                maxConnections: (int)ParseStatusValue(maxConnections.RootElement))
        };

        return JsonSerializer.Serialize(payload);
    }

    private async Task<string> CollectPostgreSqlConfigAsync()
    {
        var parametersJson = await ExecuteReaderAsync(
            """
            SELECT
                name,
                setting AS value,
                boot_val AS default_value,
                COALESCE(short_desc, '') AS description,
                context <> 'internal' AS is_dynamic,
                vartype AS type,
                min_val AS min_value,
                max_val AS max_value
            FROM pg_settings
            ORDER BY name;
            """,
            _ => { });
        var versionJson = await ExecuteScalarRowAsync("SHOW server_version;", _ => { }, "server_version");
        var uptimeJson = await ExecuteScalarRowAsync(
            "SELECT CAST(EXTRACT(EPOCH FROM now() - pg_postmaster_start_time()) AS BIGINT) AS uptime_seconds;",
            _ => { },
            "uptime_seconds");
        var activeConnectionsJson = await ExecuteScalarRowAsync(
            "SELECT COUNT(*) AS active_connections FROM pg_stat_activity WHERE datname = current_database();",
            _ => { },
            "active_connections");
        var maxConnectionsJson = await ExecuteScalarRowAsync("SHOW max_connections;", _ => { }, "max_connections");

        using var parameters = JsonDocument.Parse(parametersJson);
        using var version = JsonDocument.Parse(versionJson);
        using var uptime = JsonDocument.Parse(uptimeJson);
        using var activeConnections = JsonDocument.Parse(activeConnectionsJson);
        using var maxConnections = JsonDocument.Parse(maxConnectionsJson);

        var payload = new
        {
            parameters = parameters.RootElement.EnumerateArray()
                .Select(item => new
                {
                    name = item.GetProperty("name").GetString() ?? string.Empty,
                    value = item.GetProperty("value").GetString() ?? string.Empty,
                    default_value = item.GetProperty("default_value").GetString() ?? string.Empty,
                    description = item.GetProperty("description").GetString() ?? string.Empty,
                    is_dynamic = item.GetProperty("is_dynamic").GetBoolean(),
                    type = item.GetProperty("type").GetString() ?? string.Empty,
                    min_value = item.TryGetProperty("min_value", out var minValue) ? minValue.GetString() : null,
                    max_value = item.TryGetProperty("max_value", out var maxValue) ? maxValue.GetString() : null
                })
                .ToArray(),
            metrics = BuildMetrics(
                databaseVersion: GetString(version.RootElement, "server_version"),
                uptimeSeconds: GetLong(uptime.RootElement, "uptime_seconds"),
                activeConnections: (int)GetLong(activeConnections.RootElement, "active_connections"),
                maxConnections: (int)ParseScalarStringToLong(maxConnections.RootElement, "max_connections"))
        };

        return JsonSerializer.Serialize(payload);
    }

    private object BuildMetrics(string databaseVersion, long uptimeSeconds, int activeConnections, int maxConnections)
    {
        var totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!);

        return new
        {
            cpu_cores = Environment.ProcessorCount,
            total_memory_bytes = totalMemoryBytes > 0 ? totalMemoryBytes : 0,
            available_memory_bytes = 0L,
            total_disk_bytes = drive.IsReady ? drive.TotalSize : 0L,
            available_disk_bytes = drive.IsReady ? drive.AvailableFreeSpace : 0L,
            database_version = databaseVersion,
            uptime_seconds = uptimeSeconds,
            active_connections = activeConnections,
            max_connections = maxConnections
        };
    }

    private async Task<string> ExecuteScalarRowAsync(string sql, Action<DbCommand> configureCommand, string columnName = "database_version")
    {
        var json = await ExecuteReaderAsync(sql, configureCommand);
        using var document = JsonDocument.Parse(json);
        var firstRow = document.RootElement.EnumerateArray().FirstOrDefault();
        if (firstRow.ValueKind != JsonValueKind.Object)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object?>());
        }

        var value = firstRow.EnumerateObject().FirstOrDefault().Value;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [columnName] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
        });
    }

    private async Task<string> ExecuteReaderAsync(string sql, Action<DbCommand> configureCommand)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        configureCommand(command);

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private DbConnection CreateConnection()
    {
        return context.Engine switch
        {
            LocalDatabaseEngine.MySql => new MySqlConnection(context.ConnectionString),
            LocalDatabaseEngine.PostgreSql => new NpgsqlConnection(context.ConnectionString),
            _ => throw new InvalidOperationException($"Unsupported database engine: {context.Engine}")
        };
    }

    private static void EnsureReadOnlySql(string sql)
    {
        var trimmed = sql.TrimStart();
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("EXPLAIN", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DESCRIBE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException("Only read-only SQL is allowed in the local MCP server.");
    }

    private static long ParseStatusValue(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return 0;
        }

        var item = root[0];
        if (item.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        return GetLong(item, "Value");
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static long GetLong(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetInt64(),
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }

    private static long ParseScalarStringToLong(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && long.TryParse(property.GetString(), out var value)
            ? value
            : 0;
    }
}
