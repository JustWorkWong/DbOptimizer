# MCP 集成与 Fallback 策略

**面试重点**：展示对 MCP 协议、超时处理、降级策略、错误恢复的深度理解

---

## 一、核心问题

### Q1: 什么是 MCP（Model Context Protocol）？

**标准答案**：

MCP 是 Anthropic 提出的标准化协议，用于 AI 应用与外部工具/数据源的集成。

**核心概念**：

```
┌─────────────────────────────────────────────────────────────┐
│                      AI Application                          │
│  (DbOptimizer Backend)                                       │
└─────────────────────────────────────────────────────────────┘
                              ↓ MCP Protocol
┌─────────────────────────────────────────────────────────────┐
│                      MCP Server                              │
│  - MySQL MCP Server                                          │
│  - PostgreSQL MCP Server                                     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                   Target Database                            │
│  - MySQL 5.7+                                                │
│  - PostgreSQL 13+                                            │
└─────────────────────────────────────────────────────────────┘
```

**MCP 提供的能力**：

1. **标准化工具调用**：统一的 JSON-RPC 接口
2. **类型安全**：工具参数和返回值有明确的 schema
3. **权限控制**：MCP Server 可以限制工具的访问权限
4. **可观测性**：自动记录工具调用日志

**DbOptimizer 使用 MCP 的场景**：

- 获取数据库执行计划（`EXPLAIN` 查询）
- 获取表结构信息（`SHOW CREATE TABLE`）
- 获取索引信息（`SHOW INDEX`）
- 获取数据库配置（`SHOW VARIABLES`）

---

## 二、MCP 集成架构

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                    MAF Workflow Executor                     │
│  ExecutionPlanMafExecutor                                    │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                    MCP Client Wrapper                        │
│  - 超时控制（30s）                                            │
│  - 重试策略（3 次）                                           │
│  - 错误分类                                                   │
└─────────────────────────────────────────────────────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
        ┌───────────────────┐   ┌───────────────────┐
        │  MCP 调用成功      │   │  MCP 调用失败      │
        └───────────────────┘   └───────────────────┘
                    ↓                   ↓
        ┌───────────────────┐   ┌───────────────────┐
        │  返回真实数据      │   │  触发 Fallback     │
        └───────────────────┘   └───────────────────┘
                                        ↓
                            ┌───────────────────────┐
                            │  Fallback Strategy    │
                            │  - ADO.NET 直连       │
                            │  - 模拟数据           │
                            │  - 缓存数据           │
                            └───────────────────────┘
```

### 2.2 MCP Client 接口

```csharp
public interface IMcpClient
{
    Task<ExecutionPlanResult> GetExecutionPlanAsync(
        string databaseId,
        string sqlText,
        CancellationToken cancellationToken);
    
    Task<TableSchemaResult> GetTableSchemaAsync(
        string databaseId,
        string tableName,
        CancellationToken cancellationToken);
    
    Task<List<IndexInfo>> GetTableIndexesAsync(
        string databaseId,
        string tableName,
        CancellationToken cancellationToken);
    
    Task<Dictionary<string, string>> GetDatabaseConfigAsync(
        string databaseId,
        CancellationToken cancellationToken);
}
```

---

## 三、超时与重试策略

### 3.1 超时控制

```csharp
public class McpClientWrapper : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpClientWrapper> _logger;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    
    public async Task<ExecutionPlanResult> GetExecutionPlanAsync(
        string databaseId,
        string sqlText,
        CancellationToken cancellationToken)
    {
        // 1. 创建带超时的 CancellationToken
        using var timeoutCts = new CancellationTokenSource(_defaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        
        try
        {
            // 2. 调用 MCP Server
            var request = new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString(),
                method = "tools/call",
                @params = new
                {
                    name = "get_execution_plan",
                    arguments = new
                    {
                        database_id = databaseId,
                        sql_text = sqlText
                    }
                }
            };
            
            var response = await _httpClient.PostAsJsonAsync(
                "/mcp",
                request,
                linkedCts.Token);
            
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<McpResponse<ExecutionPlanResult>>(
                cancellationToken: linkedCts.Token);
            
            return result.Result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "MCP 调用超时，DatabaseId: {DatabaseId}, Timeout: {Timeout}s",
                databaseId,
                _defaultTimeout.TotalSeconds);
            
            throw new McpTimeoutException(
                $"MCP 调用超时（{_defaultTimeout.TotalSeconds}s）",
                databaseId);
        }
    }
}
```

### 3.2 重试策略（Polly）

```csharp
public class McpClientWithRetry : IMcpClient
{
    private readonly IMcpClient _innerClient;
    private readonly IAsyncPolicy<ExecutionPlanResult> _retryPolicy;
    
    public McpClientWithRetry(IMcpClient innerClient)
    {
        _innerClient = innerClient;
        
        // 配置重试策略
        _retryPolicy = Policy<ExecutionPlanResult>
            .Handle<HttpRequestException>()  // 网络错误
            .Or<McpTimeoutException>()       // 超时
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),  // 指数退避
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "MCP 调用失败，第 {RetryCount} 次重试，等待 {Delay}s",
                        retryCount,
                        timespan.TotalSeconds);
                });
    }
    
    public async Task<ExecutionPlanResult> GetExecutionPlanAsync(
        string databaseId,
        string sqlText,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(
            ct => _innerClient.GetExecutionPlanAsync(databaseId, sqlText, ct),
            cancellationToken);
    }
}
```

---

## 四、Fallback 策略

### 4.1 Fallback Strategy 接口

```csharp
public interface IMcpFallbackStrategy
{
    Task<T> ExecuteWithFallbackAsync<T>(
        Func<Task<T>> mcpCall,
        Func<Task<T>> fallback,
        CancellationToken cancellationToken);
}

public class McpFallbackStrategy : IMcpFallbackStrategy
{
    private readonly ILogger<McpFallbackStrategy> _logger;
    
    public async Task<T> ExecuteWithFallbackAsync<T>(
        Func<Task<T>> mcpCall,
        Func<Task<T>> fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. 尝试 MCP 调用
            return await mcpCall();
        }
        catch (McpTimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "MCP 超时，切换到 fallback 策略");
            
            // 2. 超时 → 使用 fallback
            return await fallback();
        }
        catch (McpPermissionDeniedException ex)
        {
            _logger.LogError(
                ex,
                "MCP 权限不足，无法执行 fallback");
            
            // 3. 权限错误 → 直接抛出（无法 fallback）
            throw;
        }
        catch (McpConnectionException ex)
        {
            _logger.LogWarning(
                ex,
                "MCP 连接失败，切换到 fallback 策略");
            
            // 4. 连接错误 → 使用 fallback
            return await fallback();
        }
    }
}
```

### 4.2 Fallback 实现：ADO.NET 直连

```csharp
public class ExecutionPlanMafExecutor : IExecutor<SqlParsingCompletedMessage, ExecutionPlanCompletedMessage>
{
    private readonly IMcpClient _mcpClient;
    private readonly IMcpFallbackStrategy _fallbackStrategy;
    private readonly IDbConnectionFactory _dbConnectionFactory;
    
    public async ValueTask<ExecutionPlanCompletedMessage> HandleAsync(
        SqlParsingCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 使用 fallback 策略
        var planResult = await _fallbackStrategy.ExecuteWithFallbackAsync(
            // 主路径：MCP 调用
            mcpCall: () => _mcpClient.GetExecutionPlanAsync(
                state.DatabaseId,
                state.SqlText,
                cancellationToken),
            
            // Fallback 路径：ADO.NET 直连
            fallback: async () =>
            {
                await using var connection = await _dbConnectionFactory.CreateConnectionAsync(
                    state.DatabaseId,
                    cancellationToken);
                
                await connection.OpenAsync(cancellationToken);
                
                // 执行 EXPLAIN 查询
                await using var command = connection.CreateCommand();
                command.CommandText = $"EXPLAIN {state.SqlText}";
                
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                
                var plan = new ExecutionPlanResult
                {
                    Source = "fallback-ado-net",
                    Steps = new List<ExecutionPlanStep>()
                };
                
                while (await reader.ReadAsync(cancellationToken))
                {
                    plan.Steps.Add(new ExecutionPlanStep
                    {
                        Type = reader.GetString("type"),
                        Table = reader.GetString("table"),
                        Rows = reader.GetInt64("rows"),
                        Extra = reader.GetString("Extra")
                    });
                }
                
                return plan;
            },
            cancellationToken);
        
        return new ExecutionPlanCompletedMessage(planResult);
    }
}
```

### 4.3 Fallback 实现：模拟数据

```csharp
public class ConfigCollectorMafExecutor : IExecutor<DbConfigWorkflowCommand, ConfigSnapshotCollectedMessage>
{
    private readonly IMcpClient _mcpClient;
    private readonly IMcpFallbackStrategy _fallbackStrategy;
    
    public async ValueTask<ConfigSnapshotCollectedMessage> HandleAsync(
        DbConfigWorkflowCommand command,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var options = context.Get<DbConfigOptions>("options");
        
        var configSnapshot = await _fallbackStrategy.ExecuteWithFallbackAsync(
            // 主路径：MCP 调用
            mcpCall: () => _mcpClient.GetDatabaseConfigAsync(
                command.DatabaseId,
                cancellationToken),
            
            // Fallback 路径：使用默认配置快照
            fallback: async () =>
            {
                if (!options.AllowFallbackSnapshot)
                {
                    throw new InvalidOperationException(
                        "MCP 调用失败，且不允许使用 fallback snapshot");
                }
                
                // 返回默认配置（用于演示或测试）
                return await Task.FromResult(new Dictionary<string, string>
                {
                    ["max_connections"] = "151",
                    ["innodb_buffer_pool_size"] = "134217728",  // 128MB
                    ["query_cache_size"] = "0",
                    ["tmp_table_size"] = "16777216"  // 16MB
                });
            },
            cancellationToken);
        
        return new ConfigSnapshotCollectedMessage(configSnapshot);
    }
}
```

---

## 五、错误分类与处理

### 5.1 MCP 错误类型

```csharp
// 超时错误（可 fallback）
public class McpTimeoutException : Exception
{
    public string DatabaseId { get; }
    
    public McpTimeoutException(string message, string databaseId)
        : base(message)
    {
        DatabaseId = databaseId;
    }
}

// 连接错误（可 fallback）
public class McpConnectionException : Exception
{
    public string DatabaseId { get; }
    
    public McpConnectionException(string message, string databaseId, Exception innerException)
        : base(message, innerException)
    {
        DatabaseId = databaseId;
    }
}

// 权限错误（不可 fallback）
public class McpPermissionDeniedException : Exception
{
    public string DatabaseId { get; }
    public string ToolName { get; }
    
    public McpPermissionDeniedException(string message, string databaseId, string toolName)
        : base(message)
    {
        DatabaseId = databaseId;
        ToolName = toolName;
    }
}

// 数据格式错误（不可 fallback）
public class McpDataFormatException : Exception
{
    public string RawResponse { get; }
    
    public McpDataFormatException(string message, string rawResponse)
        : base(message)
    {
        RawResponse = rawResponse;
    }
}
```

### 5.2 错误处理决策树

```
MCP 调用失败
    ↓
    ├─ 超时错误 (McpTimeoutException)
    │   → 使用 fallback（ADO.NET 直连或模拟数据）
    │
    ├─ 连接错误 (McpConnectionException)
    │   → 使用 fallback（ADO.NET 直连或模拟数据）
    │
    ├─ 权限错误 (McpPermissionDeniedException)
    │   → 直接失败，记录错误，通知用户
    │
    └─ 数据格式错误 (McpDataFormatException)
        → 直接失败，记录错误，通知开发团队
```

---

## 六、可观测性

### 6.1 MCP 调用日志

```csharp
public class McpClientWithLogging : IMcpClient
{
    private readonly IMcpClient _innerClient;
    private readonly ILogger<McpClientWithLogging> _logger;
    
    public async Task<ExecutionPlanResult> GetExecutionPlanAsync(
        string databaseId,
        string sqlText,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation(
                "开始 MCP 调用，Tool: get_execution_plan, DatabaseId: {DatabaseId}",
                databaseId);
            
            var result = await _innerClient.GetExecutionPlanAsync(
                databaseId,
                sqlText,
                cancellationToken);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "MCP 调用成功，Tool: get_execution_plan, DatabaseId: {DatabaseId}, Duration: {Duration}ms",
                databaseId,
                stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(
                ex,
                "MCP 调用失败，Tool: get_execution_plan, DatabaseId: {DatabaseId}, Duration: {Duration}ms",
                databaseId,
                stopwatch.ElapsedMilliseconds);
            
            throw;
        }
    }
}
```

### 6.2 MCP 调用统计

```csharp
public class McpCallStatistics
{
    public string ToolName { get; set; }
    public int TotalCalls { get; set; }
    public int SuccessCalls { get; set; }
    public int FailedCalls { get; set; }
    public int TimeoutCalls { get; set; }
    public int FallbackCalls { get; set; }
    public double AverageDurationMs { get; set; }
    public double P95DurationMs { get; set; }
    public double P99DurationMs { get; set; }
}

public interface IMcpStatisticsRecorder
{
    Task RecordCallAsync(
        string toolName,
        bool success,
        bool usedFallback,
        long durationMs,
        CancellationToken cancellationToken);
    
    Task<List<McpCallStatistics>> GetStatisticsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
```

---

## 七、面试问答

### Q1: MCP 超时设置为 30s 的原因？

**答案**：

1. **执行计划查询**：复杂 SQL 的 EXPLAIN 可能需要 10-20s
2. **用户体验**：超过 30s 用户会认为系统卡死
3. **资源释放**：避免长时间占用数据库连接
4. **Fallback 时间**：留出时间执行 fallback 策略

### Q2: 为什么权限错误不能 fallback？

**答案**：

权限错误说明：
1. MCP Server 配置错误（没有授予工具权限）
2. 数据库用户权限不足（无法执行 EXPLAIN）

这两种情况下，ADO.NET 直连也会失败，因为：
- 如果 MCP Server 没权限，说明数据库用户本身没权限
- Fallback 使用相同的数据库连接，权限问题依然存在

正确做法：
- 记录错误日志
- 通知用户检查权限配置
- 不要尝试 fallback（避免浪费时间）

### Q3: 如何测试 MCP Fallback 策略？

**答案**：

```csharp
[Fact]
public async Task ExecuteWithFallback_McpTimeout_UsesFallback()
{
    // Arrange
    var mockMcpClient = new Mock<IMcpClient>();
    mockMcpClient
        .Setup(x => x.GetExecutionPlanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new McpTimeoutException("timeout", "db-1"));
    
    var fallbackStrategy = new McpFallbackStrategy(Mock.Of<ILogger<McpFallbackStrategy>>());
    
    var fallbackResult = new ExecutionPlanResult { Source = "fallback" };
    
    // Act
    var result = await fallbackStrategy.ExecuteWithFallbackAsync(
        mcpCall: () => mockMcpClient.Object.GetExecutionPlanAsync("db-1", "SELECT 1", default),
        fallback: () => Task.FromResult(fallbackResult),
        cancellationToken: default);
    
    // Assert
    Assert.Equal("fallback", result.Source);
}
```

---

## 八、总结

### 核心要点

1. **MCP 是标准化协议**：统一 AI 应用与外部工具的集成
2. **超时控制必不可少**：30s 超时 + 3 次重试
3. **Fallback 策略分层**：ADO.NET 直连 → 模拟数据 → 失败
4. **错误分类处理**：超时/连接错误可 fallback，权限/格式错误不可
5. **可观测性**：记录每次 MCP 调用的耗时、成功率、fallback 率

### 面试加分项

- 提到 Polly 重试策略（指数退避）
- 提到 Circuit Breaker 模式（熔断器）
- 提到 MCP 调用统计和监控
- 提到 ADO.NET 直连作为 fallback 的优缺点
