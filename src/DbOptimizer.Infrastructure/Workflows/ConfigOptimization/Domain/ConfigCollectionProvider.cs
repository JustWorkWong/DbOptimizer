using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DbOptimizer.Infrastructure.Workflows;

internal interface IConfigCollectionProvider
{
    Task<DbConfigSnapshot> CollectConfigAsync(
        DatabaseOptimizationEngine databaseEngine,
        string databaseId,
        CancellationToken cancellationToken = default);
}

/* =========================
 * ConfigCollectionProvider
 * 职责：
 * 1) 通过 MCP 调用收集数据库配置参数和系统指标
 * 2) MySQL: SHOW VARIABLES + SHOW STATUS
 * 3) PostgreSQL: SELECT * FROM pg_settings + pg_stat_database
 * 4) 错误处理：MCP 超时/失败时标记 UsedFallback
 * ========================= */
internal sealed class ConfigCollectionProvider(
    IConfiguration configuration,
    ConfigCollectionOptions configCollectionOptions,
    ILogger<ConfigCollectionProvider> logger) : IConfigCollectionProvider
{
    private static readonly string[] ConfigToolAliases = ["get_config", "get_configuration", "GetConfiguration", "show_variables"];

    public async Task<DbConfigSnapshot> CollectConfigAsync(
        DatabaseOptimizationEngine databaseEngine,
        string databaseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serverOptions = ResolveServerOptions(databaseEngine);
        var databaseType = databaseEngine.ToString();

        if (!serverOptions.Enabled)
        {
            return CreateFallbackSnapshot(databaseId, databaseType, "MCP 未启用");
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
                    "配置收集 MCP 调用失败。DatabaseEngine={DatabaseEngine}, Attempt={Attempt}/{MaxAttempts}",
                    databaseEngine,
                    attempt,
                    configCollectionOptions.RetryCount + 1);

                if (attempt > configCollectionOptions.RetryCount)
                {
                    return CreateFallbackSnapshot(databaseId, databaseType, $"MCP 调用失败: {ex.Message}");
                }
            }

            if (attempt <= configCollectionOptions.RetryCount && configCollectionOptions.RetryDelayMilliseconds > 0)
            {
                var delay = configCollectionOptions.RetryDelayMilliseconds * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
            }
        }

        return CreateFallbackSnapshot(databaseId, databaseType, "MCP 调用失败");
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
            throw new NotSupportedException($"暂不支持 MCP transport: {serverOptions.Transport}");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DbOptimizer ConfigCollection MCP Client",
            Command = serverOptions.Command,
            Arguments = ParseArguments(serverOptions.Arguments)
        });

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
            throw new InvalidOperationException($"MCP config collection 返回错误：{rawText}");
        }

        var snapshot = ParseConfigResponse(databaseId, databaseType, rawText);

        logger.LogInformation(
            "配置收集成功。DatabaseId={DatabaseId}, DatabaseType={DatabaseType}, ParameterCount={ParameterCount}, Elapsed={ElapsedMs}ms",
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
            var match = toolList.FirstOrDefault(t =>
                string.Equals(t, alias, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        throw new InvalidOperationException(
            $"MCP 服务器未提供配置收集工具。可用工具：{string.Join(", ", toolList)}");
    }

    private ConfigCollectionMcpServerOptions ResolveServerOptions(DatabaseOptimizationEngine databaseEngine)
    {
        var sectionName = databaseEngine switch
        {
            DatabaseOptimizationEngine.MySql => "DbOptimizer:ConfigCollection:MySql",
            DatabaseOptimizationEngine.PostgreSql => "DbOptimizer:ConfigCollection:PostgreSql",
            _ => throw new NotSupportedException($"不支持的数据库引擎：{databaseEngine}")
        };

        var section = configuration.GetSection(sectionName);
        return new ConfigCollectionMcpServerOptions
        {
            Enabled = section.GetValue<bool>("Enabled"),
            Transport = section.GetValue<string>("Transport") ?? "stdio",
            Command = section.GetValue<string>("Command") ?? string.Empty,
            Arguments = section.GetValue<string>("Arguments") ?? string.Empty
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

    private DbConfigSnapshot ParseConfigResponse(string databaseId, string databaseType, string responseText)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(responseText);
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
                        MinValue = param.TryGetProperty("min_value", out var minVal)
                            ? minVal.GetString()
                            : null,
                        MaxValue = param.TryGetProperty("max_value", out var maxVal)
                            ? maxVal.GetString()
                            : null
                    });
                }
            }

            var metrics = new SystemMetrics();
            if (root.TryGetProperty("metrics", out var metricsElement))
            {
                metrics = new SystemMetrics
                {
                    CpuCores = metricsElement.TryGetProperty("cpu_cores", out var cpuCores)
                        ? cpuCores.GetInt32()
                        : 0,
                    TotalMemoryBytes = metricsElement.TryGetProperty("total_memory_bytes", out var totalMem)
                        ? totalMem.GetInt64()
                        : 0,
                    AvailableMemoryBytes = metricsElement.TryGetProperty("available_memory_bytes", out var availMem)
                        ? availMem.GetInt64()
                        : 0,
                    TotalDiskBytes = metricsElement.TryGetProperty("total_disk_bytes", out var totalDisk)
                        ? totalDisk.GetInt64()
                        : 0,
                    AvailableDiskBytes = metricsElement.TryGetProperty("available_disk_bytes", out var availDisk)
                        ? availDisk.GetInt64()
                        : 0,
                    DatabaseVersion = metricsElement.TryGetProperty("database_version", out var dbVer)
                        ? dbVer.GetString() ?? string.Empty
                        : string.Empty,
                    UptimeSeconds = metricsElement.TryGetProperty("uptime_seconds", out var uptime)
                        ? uptime.GetInt64()
                        : 0,
                    ActiveConnections = metricsElement.TryGetProperty("active_connections", out var activeConn)
                        ? activeConn.GetInt32()
                        : 0,
                    MaxConnections = metricsElement.TryGetProperty("max_connections", out var maxConn)
                        ? maxConn.GetInt32()
                        : 0
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
            logger.LogWarning(ex, "解析配置响应失败。DatabaseId={DatabaseId}", databaseId);
            return CreateFallbackSnapshot(databaseId, databaseType, $"解析响应失败: {ex.Message}");
        }
    }

    private static DbConfigSnapshot CreateFallbackSnapshot(string databaseId, string databaseType, string reason)
    {
        return new DbConfigSnapshot
        {
            DatabaseType = databaseType,
            DatabaseId = databaseId,
            Parameters = Array.Empty<ConfigParameter>(),
            Metrics = new SystemMetrics(),
            CollectedAt = DateTimeOffset.UtcNow,
            UsedFallback = true,
            FallbackReason = reason
        };
    }
}
