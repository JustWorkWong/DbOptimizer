# MCP 集成方案

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [MCP 概述](#1-mcp-概述)
2. [MCP 客户端设计](#2-mcp-客户端设计)
3. [超时处理](#3-超时处理)
4. [Fallback 策略](#4-fallback-策略)
5. [错误处理](#5-错误处理)
6. [连接池管理](#6-连接池管理)

---

## 1. MCP 概述

### 1.1 什么是 MCP

**Model Context Protocol (MCP)** 是一个标准化协议，用于 AI 模型与外部工具的交互。

**核心概念**：
- **MCP Server**：提供工具能力的服务（如 MySQL MCP Server）
- **MCP Client**：调用 MCP Server 的客户端
- **Tool**：MCP Server 暴露的功能（如 `GetExecutionPlan`）

### 1.2 DbOptimizer 中的 MCP 使用场景

| 场景 | MCP Server | Tools |
|------|-----------|-------|
| **MySQL 分析** | MySQL MCP Server | `GetExecutionPlan`, `GetTableIndexes`, `GetTableStats` |
| **PostgreSQL 分析** | PostgreSQL MCP Server | `GetExecutionPlan`, `GetTableIndexes`, `GetTableStats` |

---

## 2. MCP 客户端设计

### 2.1 接口定义

```csharp
public interface IMcpClient
{
    Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default);
    Task<List<IndexInfo>> GetTableIndexesAsync(string tableName, CancellationToken ct = default);
    Task<TableStats> GetTableStatsAsync(string tableName, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public class IndexInfo
{
    public string IndexName { get; set; }
    public string TableName { get; set; }
    public List<string> Columns { get; set; }
    public string IndexType { get; set; }
    public bool IsUnique { get; set; }
}

public class TableStats
{
    public string TableName { get; set; }
    public long RowCount { get; set; }
    public long DataSize { get; set; }
    public long IndexSize { get; set; }
}
```

### 2.2 实现示例

```csharp
public class MySqlMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly string _mcpServerUrl;
    private readonly ILogger<MySqlMcpClient> _logger;

    public MySqlMcpClient(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<MySqlMcpClient> logger)
    {
        _httpClient = httpClient;
        _mcpServerUrl = config["MCP:MySql:ServerUrl"];
        _logger = logger;
    }

    public async Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default)
    {
        var request = new McpRequest
        {
            Tool = "GetExecutionPlan",
            Arguments = new { sql }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_mcpServerUrl}/invoke",
            request,
            ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<McpResponse>(cancellationToken: ct);
        
        return result.Data.ToString();
    }

    public async Task<List<IndexInfo>> GetTableIndexesAsync(string tableName, CancellationToken ct = default)
    {
        var request = new McpRequest
        {
            Tool = "GetTableIndexes",
            Arguments = new { tableName }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_mcpServerUrl}/invoke",
            request,
            ct);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<McpResponse<List<IndexInfo>>>(cancellationToken: ct);
        
        return result.Data;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_mcpServerUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
```

---

## 3. 超时处理

### 3.1 超时配置

```json
{
  "MCP": {
    "MySql": {
      "ServerUrl": "http://localhost:3000",
      "Timeout": 30000,
      "RetryCount": 3,
      "RetryDelay": 1000
    },
    "PostgreSql": {
      "ServerUrl": "http://localhost:3001",
      "Timeout": 30000,
      "RetryCount": 3,
      "RetryDelay": 1000
    }
  }
}
```

### 3.2 超时处理实现

```csharp
public class McpClientWithTimeout : IMcpClient
{
    private readonly IMcpClient _inner;
    private readonly TimeSpan _timeout;
    private readonly ILogger<McpClientWithTimeout> _logger;

    public async Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        try
        {
            return await _inner.GetExecutionPlanAsync(sql, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("MCP call timed out after {Timeout}ms", _timeout.TotalMilliseconds);
            throw new McpTimeoutException($"MCP call timed out after {_timeout.TotalMilliseconds}ms");
        }
    }
}
```

---

## 4. Fallback 策略

### 4.1 Fallback 场景

| 场景 | Fallback 策略 |
|------|--------------|
| **MCP Server 不可用** | 降级到直接数据库连接 |
| **MCP 超时** | 重试 3 次，失败后降级 |
| **MCP 返回错误** | 记录错误，返回部分结果 |

### 4.2 Fallback 实现

```csharp
public class McpClientWithFallback : IMcpClient
{
    private readonly IMcpClient _mcpClient;
    private readonly IDbConnection _directConnection;
    private readonly ILogger<McpClientWithFallback> _logger;

    public async Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default)
    {
        try
        {
            // 优先使用 MCP
            return await _mcpClient.GetExecutionPlanAsync(sql, ct);
        }
        catch (McpTimeoutException ex)
        {
            _logger.LogWarning(ex, "MCP timeout, falling back to direct connection");
            return await GetExecutionPlanDirectAsync(sql, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MCP unavailable, falling back to direct connection");
            return await GetExecutionPlanDirectAsync(sql, ct);
        }
    }

    private async Task<string> GetExecutionPlanDirectAsync(string sql, CancellationToken ct)
    {
        // 直接连接数据库获取执行计划
        await using var cmd = _directConnection.CreateCommand();
        cmd.CommandText = $"EXPLAIN FORMAT=JSON {sql}";
        
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString() ?? string.Empty;
    }
}
```

---

## 5. 错误处理

### 5.1 错误分类

| 错误类型 | HTTP 状态码 | 处理策略 |
|---------|-----------|---------|
| **超时** | 408 | 重试 3 次 → Fallback |
| **MCP Server 不可用** | 503 | 立即 Fallback |
| **参数错误** | 400 | 记录错误，返回给用户 |
| **权限错误** | 403 | 记录错误，返回给用户 |
| **内部错误** | 500 | 重试 1 次 → Fallback |

### 5.2 错误处理实现

```csharp
public class McpClientWithErrorHandling : IMcpClient
{
    private readonly IMcpClient _inner;
    private readonly ILogger<McpClientWithErrorHandling> _logger;

    public async Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default)
    {
        try
        {
            return await _inner.GetExecutionPlanAsync(sql, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogError(ex, "Invalid MCP request: {Sql}", sql);
            throw new McpValidationException("Invalid SQL or parameters", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "MCP permission denied");
            throw new McpPermissionException("Permission denied", ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogError(ex, "MCP server unavailable");
            throw new McpUnavailableException("MCP server unavailable", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected MCP error");
            throw new McpException("Unexpected MCP error", ex);
        }
    }
}
```

---

## 6. 连接池管理

### 6.1 连接池设计

```csharp
public class McpClientPool
{
    private readonly ConcurrentDictionary<string, IMcpClient> _clients = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpClientPool> _logger;

    public IMcpClient GetClient(string databaseType)
    {
        return _clients.GetOrAdd(databaseType, type =>
        {
            return type.ToLower() switch
            {
                "mysql" => _serviceProvider.GetRequiredService<MySqlMcpClient>(),
                "postgresql" => _serviceProvider.GetRequiredService<PostgreSqlMcpClient>(),
                _ => throw new NotSupportedException($"Database type '{type}' not supported")
            };
        });
    }

    public async Task<bool> TestAllConnectionsAsync()
    {
        var tasks = _clients.Values.Select(client => client.TestConnectionAsync());
        var results = await Task.WhenAll(tasks);
        return results.All(r => r);
    }
}
```

### 6.2 连接池配置

```csharp
services.AddHttpClient<MySqlMcpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

services.AddHttpClient<PostgreSqlMcpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

services.AddSingleton<McpClientPool>();
```

### 6.3 Polly 重试策略

```csharp
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.RequestTimeout)
        .WaitAndRetryAsync(3, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

---

## 7. 监控与可观测性

### 7.1 指标收集

```csharp
public class McpClientWithMetrics : IMcpClient
{
    private readonly IMcpClient _inner;
    private readonly IMetrics _metrics;

    public async Task<string> GetExecutionPlanAsync(string sql, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetExecutionPlanAsync(sql, ct);
            _metrics.RecordMcpCall("GetExecutionPlan", sw.ElapsedMilliseconds, success: true);
            return result;
        }
        catch
        {
            _metrics.RecordMcpCall("GetExecutionPlan", sw.ElapsedMilliseconds, success: false);
            throw;
        }
    }
}
```

### 7.2 健康检查

```csharp
public class McpHealthCheck : IHealthCheck
{
    private readonly McpClientPool _pool;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var isHealthy = await _pool.TestAllConnectionsAsync();
        
        return isHealthy
            ? HealthCheckResult.Healthy("All MCP servers are reachable")
            : HealthCheckResult.Unhealthy("One or more MCP servers are unreachable");
    }
}
```

---

## 8. 与其他文档的映射关系

- **架构设计**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **Workflow 设计**：[WORKFLOW_DESIGN.md](./WORKFLOW_DESIGN.md)
- **数据模型**：[DATA_MODEL.md](./DATA_MODEL.md)
- **P0/P1 设计**：[P0_P1_DESIGN.md](./P0_P1_DESIGN.md)
