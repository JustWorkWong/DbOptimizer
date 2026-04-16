using System.Collections.Concurrent;
using System.Text;
using ModelContextProtocol.Client;

namespace DbOptimizer.AgentRuntime;

/* =========================
 * 数据库 MCP 客户端
 * 设计目标：
 * 1) 统一封装 MySQL / PostgreSQL 的 MCP 调用入口
 * 2) 兼容文档中工具命名不完全一致的情况，通过别名自动匹配真实工具名
 * 3) 为 M2-02 的超时 / 重试 / 降级预留稳定包裹点
 * ========================= */
internal class DatabaseMcpClient(
    DatabaseEngine databaseEngine,
    McpServerOptions serverOptions,
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
        EnsureEnabled();

        var client = await GetClientAsync(cancellationToken);
        var toolName = await ResolveToolNameAsync(client, toolKind, cancellationToken);
        logger.LogInformation(
            "Calling MCP tool {ToolName} for database {DatabaseEngine}.",
            toolName,
            databaseEngine);

        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        return McpToolInvocationResult.FromCallResult(toolName, result);
    }

    private void EnsureEnabled()
    {
        if (!serverOptions.Enabled)
        {
            throw new InvalidOperationException($"MCP client for {databaseEngine} is disabled by configuration.");
        }
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
            throw new NotSupportedException(
                $"Transport '{serverOptions.Transport}' is not supported yet. M2-01 currently implements stdio MCP transport.");
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

internal enum McpToolKind
{
    Query,
    DescribeTable,
    Explain,
    ShowIndexes
}
