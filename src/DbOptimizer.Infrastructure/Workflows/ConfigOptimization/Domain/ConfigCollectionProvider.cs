using DbOptimizer.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Workflows;

public interface IConfigCollectionProvider
{
    Task<DbConfigSnapshot> CollectConfigAsync(
        DbOptimizer.Core.Models.DatabaseOptimizationEngine databaseEngine,
        string databaseId,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigCollectionProvider(
    IConfiguration configuration,
    ConfigCollectionOptions configCollectionOptions,
    ILogger<ConfigCollectionProvider> logger) : IConfigCollectionProvider
{
    private static readonly string[] ConfigToolAliases =
        ["get_config", "get_configuration", "GetConfiguration", "show_variables"];

    public async Task<DbConfigSnapshot> CollectConfigAsync(
        DbOptimizer.Core.Models.DatabaseOptimizationEngine databaseEngine,
        string databaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serverOptions = ResolveServerOptions(databaseEngine);
        var databaseType = databaseEngine.ToString();

        if (!serverOptions.Enabled)
        {
            throw new InvalidOperationException(
                $"Config collection MCP is disabled. DatabaseEngine={databaseEngine}, DatabaseId={databaseId}");
        }

        for (var attempt = 1; attempt <= configCollectionOptions.RetryCount + 1; attempt++)
        {
            try
            {
                return await InvokeMcpAsync(serverOptions, databaseId, databaseType, attempt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Config collection MCP call failed. DatabaseEngine={DatabaseEngine}, Attempt={Attempt}/{MaxAttempts}",
                    databaseEngine,
                    attempt,
                    configCollectionOptions.RetryCount + 1);

                if (attempt > configCollectionOptions.RetryCount)
                {
                    throw new InvalidOperationException(
                        $"Config collection MCP call failed. DatabaseEngine={databaseEngine}, DatabaseId={databaseId}",
                        ex);
                }
            }

            if (attempt <= configCollectionOptions.RetryCount && configCollectionOptions.RetryDelayMilliseconds > 0)
            {
                var delay = configCollectionOptions.RetryDelayMilliseconds * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Config collection MCP call failed. DatabaseEngine={databaseEngine}, DatabaseId={databaseId}");
    }

    private async Task<DbConfigSnapshot> InvokeMcpAsync(
        ConfigCollectionMcpServerOptions serverOptions,
        string databaseId,
        string databaseType,
        int attemptCount,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(configCollectionOptions.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var stopwatch = Stopwatch.StartNew();

        if (!serverOptions.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported MCP transport: {serverOptions.Transport}");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DbOptimizer ConfigCollection MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = ResolveConnectionString(databaseId, databaseType)
            }
        });

        logger.LogInformation(
            "Starting config collection MCP call. DatabaseId={DatabaseId}, DatabaseType={DatabaseType}, Attempt={Attempt}, Command={Command}, Arguments={Arguments}",
            databaseId,
            databaseType,
            attemptCount,
            serverOptions.Command,
            serverOptions.Arguments);

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync(cancellationToken: linkedCts.Token);
        var toolName = ResolveConfigToolName(tools.Select(tool => tool.Name));

        var result = await client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>(),
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
            throw new InvalidOperationException($"MCP config collection returned an error: {rawText}");
        }

        var snapshot = ParseConfigResponse(databaseId, databaseType, rawText);

        logger.LogInformation(
            "Config collection succeeded. DatabaseId={DatabaseId}, DatabaseType={DatabaseType}, ParameterCount={ParameterCount}, Elapsed={ElapsedMs}ms",
            databaseId,
            databaseType,
            snapshot.Parameters.Count,
            stopwatch.ElapsedMilliseconds);

        return snapshot;
    }

    private static string ResolveConfigToolName(IEnumerable<string> availableTools)
    {
        var toolList = availableTools.ToList();

        foreach (var alias in ConfigToolAliases)
        {
            var match = toolList.FirstOrDefault(t => string.Equals(t, alias, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        throw new InvalidOperationException(
            $"MCP server does not expose a config collection tool. Available tools: {string.Join(", ", toolList)}");
    }

    private ConfigCollectionMcpServerOptions ResolveServerOptions(DbOptimizer.Core.Models.DatabaseOptimizationEngine databaseEngine)
    {
        var serverOptions = databaseEngine switch
        {
            DbOptimizer.Core.Models.DatabaseOptimizationEngine.MySql => configCollectionOptions.MySql,
            DbOptimizer.Core.Models.DatabaseOptimizationEngine.PostgreSql => configCollectionOptions.PostgreSql,
            _ => throw new NotSupportedException($"Unsupported database engine: {databaseEngine}")
        };

        return new ConfigCollectionMcpServerOptions
        {
            Enabled = serverOptions.Enabled,
            Transport = serverOptions.Transport,
            Command = serverOptions.Command,
            Arguments = serverOptions.Arguments
        };
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

    private string ResolveConnectionString(string databaseId, string databaseType)
    {
        var targetDatabase = configuration[$"TargetDatabases:{databaseId}"];
        if (!string.IsNullOrWhiteSpace(targetDatabase))
        {
            return targetDatabase;
        }

        return databaseType.Contains("postgres", StringComparison.OrdinalIgnoreCase)
            ? ResolvePostgreSqlConnectionString()
            : ResolveMySqlConnectionString();
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

        throw new InvalidOperationException("Missing PostgreSQL connection string for config collection.");
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

        throw new InvalidOperationException("Missing MySQL connection string for config collection.");
    }

    private DbConfigSnapshot ParseConfigResponse(string databaseId, string databaseType, string responseText)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(responseText);
            var root = jsonDoc.RootElement;

            var parameters = new List<ConfigParameter>();
            if (root.TryGetProperty("parameters", out var parametersElement))
            {
                foreach (var param in parametersElement.EnumerateArray())
                {
                    parameters.Add(new ConfigParameter
                    {
                        Name = param.GetProperty("name").GetString() ?? string.Empty,
                        Value = param.GetProperty("value").GetString() ?? string.Empty,
                        DefaultValue = param.TryGetProperty("default_value", out var defaultVal)
                            ? defaultVal.GetString() ?? string.Empty
                            : string.Empty,
                        Description = param.TryGetProperty("description", out var desc)
                            ? desc.GetString() ?? string.Empty
                            : string.Empty,
                        IsDynamic = param.TryGetProperty("is_dynamic", out var isDynamic) && isDynamic.GetBoolean(),
                        Type = param.TryGetProperty("type", out var type)
                            ? type.GetString() ?? string.Empty
                            : string.Empty,
                        MinValue = param.TryGetProperty("min_value", out var minVal) ? minVal.GetString() : null,
                        MaxValue = param.TryGetProperty("max_value", out var maxVal) ? maxVal.GetString() : null
                    });
                }
            }

            var metrics = new SystemMetrics();
            if (root.TryGetProperty("metrics", out var metricsElement))
            {
                metrics = new SystemMetrics
                {
                    CpuCores = metricsElement.TryGetProperty("cpu_cores", out var cpuCores) ? cpuCores.GetInt32() : 0,
                    TotalMemoryBytes = metricsElement.TryGetProperty("total_memory_bytes", out var totalMem) ? totalMem.GetInt64() : 0,
                    AvailableMemoryBytes = metricsElement.TryGetProperty("available_memory_bytes", out var availMem) ? availMem.GetInt64() : 0,
                    TotalDiskBytes = metricsElement.TryGetProperty("total_disk_bytes", out var totalDisk) ? totalDisk.GetInt64() : 0,
                    AvailableDiskBytes = metricsElement.TryGetProperty("available_disk_bytes", out var availDisk) ? availDisk.GetInt64() : 0,
                    DatabaseVersion = metricsElement.TryGetProperty("database_version", out var dbVer) ? dbVer.GetString() ?? string.Empty : string.Empty,
                    UptimeSeconds = metricsElement.TryGetProperty("uptime_seconds", out var uptime) ? uptime.GetInt64() : 0,
                    ActiveConnections = metricsElement.TryGetProperty("active_connections", out var activeConn) ? activeConn.GetInt32() : 0,
                    MaxConnections = metricsElement.TryGetProperty("max_connections", out var maxConn) ? maxConn.GetInt32() : 0
                };
            }

            return new DbConfigSnapshot
            {
                DatabaseType = databaseType,
                DatabaseId = databaseId,
                Parameters = parameters,
                Metrics = metrics,
                CollectedAt = DateTimeOffset.UtcNow,
                UsedFallback = false
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse config response. DatabaseId={DatabaseId}", databaseId);
            throw new InvalidOperationException(
                $"Failed to parse config collection response. DatabaseId={databaseId}",
                ex);
        }
    }
}
