# P0 + P1 详细设计文档

**项目名称**：DbOptimizer - P0/P1 优先级设计补充  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [P0 必须实现的功能](#1-p0-必须实现的功能)
2. [P1 应该实现的功能](#2-p1-应该实现的功能)
3. [实现细节](#3-实现细节)

---

## 1. P0 必须实现的功能

### 1.1 Checkpoint 恢复机制

#### 1.1.1 设计目标

- API 进程重启后，能够恢复未完成的 Workflow
- 用户刷新页面后，能够重新连接到正在运行的 Workflow
- 支持长时间运行的 Workflow（如等待人工审核）

#### 1.1.2 存储策略

**双层存储**：
- **PostgreSQL**：持久化存储，用于进程重启后恢复
- **Redis**：热点缓存，加速恢复

**数据结构**：

```csharp
public class WorkflowCheckpoint
{
    public string SessionId { get; set; }
    public string WorkflowType { get; set; }  // "SqlAnalysis" / "DbConfigOptimization"
    public WorkflowStatus Status { get; set; }  // Running / WaitingForReview / Completed / Failed
    public string CurrentExecutor { get; set; }  // 当前执行到哪个 Executor
    public Dictionary<string, JsonElement> Context { get; set; } = new();  // 共享上下文（类型安全快照）
    public List<string> CompletedExecutors { get; set; }  // 已完成的 Executor
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastCheckpointAt { get; set; }
}

public enum WorkflowStatus
{
    Running,           // 正在运行
    WaitingForReview,  // 等待人工审核
    Completed,         // 已完成
    Failed,            // 失败
    Cancelled          // 已取消
}
```

#### 1.1.3 Checkpoint 保存时机与序列化规范

```csharp
public class CheckpointStorage
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConnectionMultiplexer _redis;

    // 1. 每个 Executor 执行完成后保存
    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint)
    {
        // 保存到 PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.WorkflowSessions.FindAsync(checkpoint.SessionId);
        
        session.Status = checkpoint.Status.ToString();
        session.State = JsonSerializer.Serialize(checkpoint);
        session.UpdatedAt = DateTime.UtcNow;
        
        await db.SaveChangesAsync();

        // 缓存到 Redis（TTL 1 小时）
        var redisDb = _redis.GetDatabase();
        await redisDb.StringSetAsync(
            $"checkpoint:{checkpoint.SessionId}",
            JsonSerializer.Serialize(checkpoint),
            TimeSpan.FromHours(1)
        );
    }

    // 2. 从 Redis 或 PostgreSQL 恢复
    public async Task<WorkflowCheckpoint?> LoadCheckpointAsync(string sessionId)
    {
        // 先尝试从 Redis 读取
        var redisDb = _redis.GetDatabase();
        var cached = await redisDb.StringGetAsync($"checkpoint:{sessionId}");
        
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<WorkflowCheckpoint>(cached!);
        }

        // Redis 未命中，从 PostgreSQL 读取
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.WorkflowSessions.FindAsync(sessionId);
        
        if (session == null || string.IsNullOrEmpty(session.State))
        {
            return null;
        }

        var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpoint>(session.State);

        // 回写到 Redis
        await redisDb.StringSetAsync(
            $"checkpoint:{sessionId}",
            session.State,
            TimeSpan.FromHours(1)
        );

        return checkpoint;
    }

    // 3. 删除 Checkpoint（Workflow 完成后）
    public async Task DeleteCheckpointAsync(string sessionId)
    {
        var redisDb = _redis.GetDatabase();
        await redisDb.KeyDeleteAsync($"checkpoint:{sessionId}");
    }
}
```

**序列化约束（必须）**：
- `Context` 仅允许 JSON 可序列化值，禁止直接存储运行时对象实例
- 读取快照后通过 `JsonSerializer.Deserialize<T>(jsonElement)` 做显式类型还原
- Checkpoint Schema 变更时递增 `checkpointVersion`，并提供向后兼容迁移器

#### 1.1.4 Workflow 恢复逻辑

```csharp
public class WorkflowSessionManager
{
    private readonly CheckpointStorage _checkpointStorage;
    private readonly IWorkflowEngine _workflowEngine;

    // 恢复 Workflow（仅恢复可执行状态）
    public async Task<WorkflowSession> ResumeWorkflowAsync(string sessionId)
    {
        var checkpoint = await _checkpointStorage.LoadCheckpointAsync(sessionId)
            ?? throw new InvalidOperationException($"Checkpoint not found: {sessionId}");

        if (checkpoint.Status is WorkflowStatus.Completed or WorkflowStatus.Failed or WorkflowStatus.Cancelled)
        {
            throw new InvalidOperationException($"Workflow is terminal: {sessionId}, status={checkpoint.Status}");
        }

        // WaitingForReview: 不重放 Executor，恢复展示态（含结果）
        if (checkpoint.Status == WorkflowStatus.WaitingForReview)
        {
            var finalResult = checkpoint.Context.TryGetValue("FinalResult", out var resultElement)
                ? JsonSerializer.Deserialize<object>(resultElement.GetRawText())
                : null;

            return new WorkflowSession
            {
                SessionId = sessionId,
                Status = WorkflowStatus.WaitingForReview,
                Result = finalResult
            };
        }

        // Running: 从当前 Executor 继续
        var context = new WorkflowContext
        {
            SessionId = sessionId,
            Data = checkpoint.Context
        };

        var result = await _workflowEngine.ResumeFromExecutorAsync(
            checkpoint.WorkflowType,
            checkpoint.CurrentExecutor,
            context
        );

        return new WorkflowSession
        {
            SessionId = sessionId,
            Status = WorkflowStatus.Running,
            Result = result
        };
    }

    // 启动时仅自动恢复 Running；WaitingForReview 由用户动作驱动
    public async Task RecoverPendingWorkflowsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var resumableSessions = await db.WorkflowSessions
            .Where(s => s.Status == "Running")
            .ToListAsync();

        foreach (var session in resumableSessions)
        {
            try
            {
                await ResumeWorkflowAsync(session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover workflow: {SessionId}", session.Id);
            }
        }
    }
}
```

---

### 1.2 MCP 超时 + 错误处理

#### 1.2.1 超时策略

```csharp
public class McpClient
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<McpClient> _logger;
    private readonly IMcpFallbackService _fallback;

    public async Task<McpResponse> CallToolAsync(
        string toolName,
        object args,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        try
        {
            _logger.LogInformation("Calling MCP tool: {ToolName}", toolName);

            var response = await _mcpServer.CallToolAsync(toolName, args, linkedCts.Token);

            _logger.LogInformation("MCP tool succeeded: {ToolName}", toolName);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            // 仅在 MCP 超时时降级
            _logger.LogWarning("MCP timeout, fallback to ADO.NET: {ToolName}", toolName);
            return await _fallback.ExecuteAsync(toolName, args, GetDatabaseType(args), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消：直接透传，禁止误降级
            _logger.LogInformation("MCP call canceled by caller: {ToolName}", toolName);
            throw;
        }
        catch (McpException ex) when (ex.Code == "permission_denied")
        {
            _logger.LogError(ex, "MCP permission denied: {ToolName}", toolName);
            throw new InvalidOperationException($"MCP permission denied: {toolName}", ex);
        }
        catch (McpException ex) when (ex.Code == "connection_error")
        {
            _logger.LogWarning(ex, "MCP connection error, fallback: {ToolName}", toolName);
            return await _fallback.ExecuteAsync(toolName, args, GetDatabaseType(args), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP unexpected error: {ToolName}", toolName);
            throw;
        }
    }

    // 从 args 提取 databaseType（用于 Fallback）
    private string GetDatabaseType(object args)
    {
        var json = JsonSerializer.Serialize(args);
        var doc = JsonDocument.Parse(json);
        
        if (doc.RootElement.TryGetProperty("databaseType", out var dbType))
        {
            return dbType.GetString() ?? throw new InvalidOperationException("databaseType is null");
        }

        throw new InvalidOperationException("databaseType not found in MCP tool args");
    }
}
```

#### 1.2.2 Fallback 实现

```csharp
public interface IMcpFallbackService
{
    Task<McpResponse> ExecuteAsync(
        string toolName,
        object args,
        string databaseType,
        CancellationToken cancellationToken = default);
}

public class AdoNetFallbackService : IMcpFallbackService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public async Task<McpResponse> ExecuteAsync(
        string toolName,
        object args,
        string databaseType,
        CancellationToken cancellationToken = default)
    {
        return toolName switch
        {
            "get_table_indexes" => await GetTableIndexesAsync(args, databaseType, cancellationToken),
            "get_execution_plan" => await GetExecutionPlanAsync(args, databaseType, cancellationToken),
            "get_table_schema" => await GetTableSchemaAsync(args, databaseType, cancellationToken),
            _ => throw new NotSupportedException($"Fallback not supported: {toolName}")
        };
    }

    private async Task<McpResponse> GetTableIndexesAsync(
        object args,
        string databaseType,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<GetTableIndexesRequest>(JsonSerializer.Serialize(args))!;

        await using var conn = await _connectionFactory.CreateConnectionAsync(databaseType, cancellationToken);
        await using var cmd = conn.CreateCommand();

        // MySQL
        if (databaseType == "mysql")
        {
            cmd.CommandText = @"
                SELECT 
                    INDEX_NAME,
                    COLUMN_NAME,
                    NON_UNIQUE,
                    SEQ_IN_INDEX
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @table
                ORDER BY INDEX_NAME, SEQ_IN_INDEX";
        }
        // PostgreSQL
        else
        {
            cmd.CommandText = @"
                SELECT 
                    i.relname AS index_name,
                    a.attname AS column_name,
                    NOT ix.indisunique AS non_unique,
                    a.attnum AS seq_in_index
                FROM pg_index ix
                JOIN pg_class i ON i.oid = ix.indexrelid
                JOIN pg_class t ON t.oid = ix.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
                WHERE t.relname = @table
                ORDER BY i.relname, a.attnum";
        }

        var schemaParam = cmd.CreateParameter();
        schemaParam.ParameterName = "@schema";
        schemaParam.Value = request.Schema;
        cmd.Parameters.Add(schemaParam);

        var tableParam = cmd.CreateParameter();
        tableParam.ParameterName = "@table";
        tableParam.Value = request.Table;
        cmd.Parameters.Add(tableParam);

        var indexes = new List<IndexInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(new IndexInfo
            {
                IndexName = reader.GetString(0),
                ColumnName = reader.GetString(1),
                NonUnique = reader.GetBoolean(2),
                SeqInIndex = reader.GetInt32(3)
            });
        }

        return new McpResponse
        {
            Success = true,
            Data = indexes
        };
    }

    // 其他 Fallback 方法...
}
```

---

## 2. P1 应该实现的功能

### 2.1 SSE 断线重连

#### 2.1.1 前端实现

```typescript
// src/composables/useSSE.ts
import { ref, onUnmounted } from 'vue';

interface WorkflowEvent {
  eventType: string;
  sessionId: string;
  sequence: number;
  timestamp: string;
  payload: unknown;
}

export function useSSE(sessionId: string) {
  const eventSource = ref<EventSource | null>(null);
  const isConnected = ref(false);
  const reconnectAttempts = ref(0);
  const maxReconnectAttempts = 5;
  const lastSequence = ref(0);
  let pollingTimer: number | null = null;

  const connect = () => {
    if (eventSource.value) {
      eventSource.value.close();
    }

    // 服务端每条事件必须输出: id: {sequence}
    eventSource.value = new EventSource(`/api/workflows/${sessionId}/events`);

    eventSource.value.onopen = () => {
      isConnected.value = true;
      reconnectAttempts.value = 0;
    };

    eventSource.value.onmessage = async (event) => {
      const data = JSON.parse(event.data) as WorkflowEvent;

      // 丢事件检测：sequence 必须连续
      if (lastSequence.value > 0 && data.sequence > lastSequence.value + 1) {
        await replayMissingEvents(lastSequence.value);
      }

      lastSequence.value = data.sequence;
      handleMessage(data);
    };

    eventSource.value.onerror = () => {
      isConnected.value = false;
      eventSource.value?.close();

      // 指数退避重连
      if (reconnectAttempts.value < maxReconnectAttempts) {
        const delay = Math.min(1000 * Math.pow(2, reconnectAttempts.value), 30000);
        window.setTimeout(() => {
          reconnectAttempts.value++;
          connect();
        }, delay);
      } else {
        // 降级到轮询（统一复用快照接口）
        startPolling();
      }
    };
  };

  const replayMissingEvents = async (fromSequence: number) => {
    const response = await fetch(`/api/workflows/${sessionId}/timeline?cursor=seq_${fromSequence}&limit=200`);
    const result = await response.json();
    for (const event of result.data.events) {
      handleMessage(event);
      lastSequence.value = event.sequence;
    }
  };

  const startPolling = () => {
    stopPolling();

    pollingTimer = window.setInterval(async () => {
      try {
        const response = await fetch(`/api/workflows/${sessionId}`);
        const result = await response.json();
        const snapshot = result.data;

        handleSnapshot(snapshot);

        // 终态停止轮询
        if (['Completed', 'Failed', 'Cancelled'].includes(snapshot.status)) {
          stopPolling();
        }
      } catch (error) {
        console.error('Polling error:', error);
      }
    }, 3000);
  };

  const stopPolling = () => {
    if (pollingTimer !== null) {
      window.clearInterval(pollingTimer);
      pollingTimer = null;
    }
  };

  const handleMessage = (data: WorkflowEvent) => {
    console.log('SSE message:', data);
  };

  const handleSnapshot = (snapshot: unknown) => {
    console.log('Workflow snapshot:', snapshot);
  };

  const disconnect = () => {
    eventSource.value?.close();
    stopPolling();
    isConnected.value = false;
  };

  onUnmounted(() => {
    disconnect();
  });

  return {
    connect,
    disconnect,
    isConnected,
    reconnectAttempts,
    lastSequence
  };
}
```

#### 2.1.2 后端支持（含 Last-Event-ID 补发）

```csharp
// SSE 发布器（支持断线续传）
public class SSEPublisher
{
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();
    private readonly IEventStore _eventStore;
    private const int EventReplayWindowMinutes = 60; // 与 Redis Checkpoint TTL 对齐

    public async Task PublishAsync(string sessionId, object data)
    {
        if (_connections.TryGetValue(sessionId, out var connection))
        {
            await connection.WriteAsync(data);
        }

        // 持久化事件用于补发
        await _eventStore.SaveEventAsync(sessionId, data, TimeSpan.FromMinutes(EventReplayWindowMinutes));
    }

    // 处理 SSE 连接（支持 Last-Event-ID）
    public async Task HandleConnectionAsync(HttpContext context, string sessionId)
    {
        var lastEventId = context.Request.Headers["Last-Event-ID"].ToString();

        if (!string.IsNullOrEmpty(lastEventId) && int.TryParse(lastEventId, out var lastSequence))
        {
            var missedEvents = await _eventStore.GetEventsAfterAsync(sessionId, lastSequence);

            if (missedEvents == null)
            {
                // 补发窗口已过期
                context.Response.StatusCode = 409;
                await context.Response.WriteAsync("Event replay window expired, use /timeline endpoint");
                return;
            }

            // 补发丢失事件
            foreach (var evt in missedEvents)
            {
                await context.Response.WriteAsync($"id: {evt.Sequence}\ndata: {JsonSerializer.Serialize(evt)}\n\n");
            }
        }

        // 建立新连接
        var connection = new SseConnection(context);
        _connections[sessionId] = connection;
    }

    // 定期发送心跳
    public async Task SendHeartbeatAsync(string sessionId)
    {
        if (_connections.TryGetValue(sessionId, out var connection))
        {
            await connection.WriteAsync(new
            {
                eventType = "heartbeat",
                sessionId,
                timestamp = DateTime.UtcNow
            });
        }
    }

    // 后台任务：每 30 秒发送一次心跳
    public class HeartbeatBackgroundService : BackgroundService
    {
        private readonly SSEPublisher _publisher;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var sessionId in _publisher._connections.Keys)
                {
                    await _publisher.SendHeartbeatAsync(sessionId);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
```

---

### 2.2 JSONB 索引优化

```sql
-- 幂等创建索引（避免重复执行迁移失败）
CREATE INDEX IF NOT EXISTS idx_tool_calls_arguments ON tool_calls USING GIN (arguments);
CREATE INDEX IF NOT EXISTS idx_tool_calls_result ON tool_calls USING GIN (result);

CREATE INDEX IF NOT EXISTS idx_agent_messages_content ON agent_messages USING GIN (content);

CREATE INDEX IF NOT EXISTS idx_decision_records_evidence ON decision_records USING GIN (evidence_references);

-- 查询示例：查找所有调用了 get_table_indexes 的记录
SELECT * FROM tool_calls
WHERE arguments @> '{"tool_name": "get_table_indexes"}';

-- 查询示例：查找所有包含特定证据的决策记录
SELECT * FROM decision_records
WHERE evidence_references @> '[{"type": "execution_plan"}]';
```

---

### 2.3 Token 成本监控

#### 2.3.1 数据模型扩展

```sql
-- 幂等扩展 agent_executions 表
ALTER TABLE agent_executions
ADD COLUMN IF NOT EXISTS tokens_used INTEGER DEFAULT 0,
ADD COLUMN IF NOT EXISTS cost_usd DECIMAL(10, 6) DEFAULT 0;

-- 幂等创建索引
CREATE INDEX IF NOT EXISTS idx_agent_executions_tokens ON agent_executions(tokens_used);
CREATE INDEX IF NOT EXISTS idx_agent_executions_cost ON agent_executions(cost_usd);

-- 幂等刷新统计视图定义
CREATE OR REPLACE VIEW v_cost_statistics AS
SELECT
    DATE(created_at) AS date,
    agent_name,
    COUNT(*) AS execution_count,
    SUM(tokens_used) AS total_tokens,
    SUM(cost_usd) AS total_cost
FROM agent_executions
GROUP BY DATE(created_at), agent_name
ORDER BY date DESC, total_cost DESC;
```

#### 2.3.2 Token 计算逻辑

```csharp
public class TokenCostCalculator
{
    // 定价按“每 1K tokens”定义
    private const decimal InputCostPer1KTokens = 0.03m;   // $0.03 / 1K tokens
    private const decimal OutputCostPer1KTokens = 0.06m;  // $0.06 / 1K tokens

    public TokenUsage CalculateTokenUsage(AgentResponse response)
    {
        var inputTokens = response.Usage.PromptTokens;
        var outputTokens = response.Usage.CompletionTokens;
        var totalTokens = inputTokens + outputTokens;

        var inputCost = (inputTokens / 1000m) * InputCostPer1KTokens;
        var outputCost = (outputTokens / 1000m) * OutputCostPer1KTokens;
        var totalCost = inputCost + outputCost;

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            InputCost = inputCost,
            OutputCost = outputCost,
            TotalCost = totalCost
        };
    }
}

public class AgentExecutionService
{
    private readonly TokenCostCalculator _costCalculator;

    public async Task<AgentExecutionResult> ExecuteAgentAsync(AgentRequest request)
    {
        var response = await _agentClient.ExecuteAsync(request);

        // 计算 Token 成本
        var tokenUsage = _costCalculator.CalculateTokenUsage(response);

        // 保存到数据库
        await using var db = await _dbFactory.CreateDbContextAsync();
        
        var execution = new AgentExecution
        {
            AgentName = request.AgentName,
            TokensUsed = tokenUsage.TotalTokens,
            CostUsd = tokenUsage.TotalCost,
            // ... 其他字段
        };

        db.AgentExecutions.Add(execution);
        await db.SaveChangesAsync();

        return new AgentExecutionResult
        {
            Response = response,
            TokenUsage = tokenUsage
        };
    }
}
```

---

## 3. 实现细节

### 3.1 Workflow Context 数据结构

```csharp
public class WorkflowContext
{
    public string SessionId { get; set; }
    public Dictionary<string, JsonElement> Data { get; set; } = new();

    // 类型安全读取（从 JsonElement 显式反序列化）
    public T Get<T>(string key)
    {
        if (!Data.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Key not found: {key}");
        }

        return JsonSerializer.Deserialize<T>(value.GetRawText())!;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (Data.TryGetValue(key, out var raw))
        {
            value = JsonSerializer.Deserialize<T>(raw.GetRawText());
            return true;
        }

        value = default;
        return false;
    }

    public void Set<T>(string key, T value)
    {
        Data[key] = JsonSerializer.SerializeToElement(value);
    }
}
```

### 3.2 Executor 数据传递

```csharp
public abstract class BaseExecutor
{
    protected readonly ILogger _logger;

    public async Task<ExecutorResult> ExecuteAsync(WorkflowContext context)
    {
        _logger.LogInformation("Executor started: {ExecutorName}", GetType().Name);

        try
        {
            var result = await ExecuteInternalAsync(context);

            _logger.LogInformation("Executor completed: {ExecutorName}", GetType().Name);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executor failed: {ExecutorName}", GetType().Name);
            throw;
        }
    }

    protected abstract Task<ExecutorResult> ExecuteInternalAsync(WorkflowContext context);
}

// SQL Parser Executor
public class SqlParserExecutor : BaseExecutor
{
    protected override async Task<ExecutorResult> ExecuteInternalAsync(WorkflowContext context)
    {
        var sql = context.Get<string>("InputSql");

        // 调用 Agent 解析 SQL
        var parseResult = await _agent.ParseSqlAsync(sql);

        // 保存到 Context
        context.Set("ParsedSql", parseResult);

        return ExecutorResult.Success(parseResult);
    }
}

// Execution Plan Executor
public class ExecutionPlanExecutor : BaseExecutor
{
    protected override async Task<ExecutorResult> ExecuteInternalAsync(WorkflowContext context)
    {
        // 依赖 SqlParserExecutor 的结果
        var parsedSql = context.Get<ParsedSql>("ParsedSql");
        var connectionString = context.Get<string>("ConnectionString");

        // 获取执行计划
        var plan = await _mcpClient.GetExecutionPlanAsync(parsedSql.Sql, connectionString);

        // 保存到 Context
        context.Set("ExecutionPlan", plan);

        return ExecutorResult.Success(plan);
    }
}

// Coordinator Executor
public class CoordinatorExecutor : BaseExecutor
{
    protected override async Task<ExecutorResult> ExecuteInternalAsync(WorkflowContext context)
    {
        // 聚合所有结果
        var parsedSql = context.Get<ParsedSql>("ParsedSql");
        var executionPlan = context.Get<ExecutionPlan>("ExecutionPlan");
        var indexRecommendations = context.Get<List<IndexRecommendation>>("IndexRecommendations");

        // 调用 Coordinator Agent 生成最终建议
        var finalResult = await _agent.CoordinateAsync(parsedSql, executionPlan, indexRecommendations);

        // 保存到 Context
        context.Set("FinalResult", finalResult);

        return ExecutorResult.Success(finalResult);
    }
}
```

### 3.3 Human Review Executor

```csharp
public class HumanReviewExecutor : BaseExecutor
{
    private readonly IReviewService _reviewService;

    protected override async Task<ExecutorResult> ExecuteInternalAsync(WorkflowContext context)
    {
        var finalResult = context.Get<OptimizationResult>("FinalResult");

        // 1. 保存到审核队列
        var reviewId = await _reviewService.CreateReviewAsync(context.SessionId, finalResult);

        // 2. 更新 Workflow 状态为 WaitingForReview
        context.Set("ReviewId", reviewId);
        context.Set("Status", WorkflowStatus.WaitingForReview);

        // 3. 发送 SSE 通知前端
        await _ssePublisher.PublishAsync(context.SessionId, new
        {
            eventType = "review.required",
            sessionId = context.SessionId,
            payload = new
            {
                reviewId,
                recommendations = finalResult.Recommendations
            }
        });

        // 4. 等待审核结果（通过轮询或回调）
        var reviewResult = await _reviewService.WaitForReviewAsync(reviewId);

        // 5. 如果驳回，触发 Regeneration Executor
        if (reviewResult.Status == ReviewStatus.Rejected)
        {
            context.Set("RejectionReason", reviewResult.Reason);
            return ExecutorResult.Rejected(reviewResult.Reason);
        }

        return ExecutorResult.Success(reviewResult);
    }
}
```

---

## 4. 总结

### P0 实现清单

- [x] Checkpoint 恢复机制（双层存储 + 恢复逻辑）
- [x] MCP 超时处理（30s 超时 + Fallback）
- [x] MCP 错误处理（权限不足 / 连接错误）

### P1 实现清单

- [x] SSE 断线重连（指数退避 + 降级轮询）
- [x] JSONB 索引优化（GIN 索引）
- [x] Token 成本监控（记录 + 统计视图）

### 下一步

1. 实现 P0 功能（Checkpoint + MCP 超时）
2. 实现 P1 功能（SSE 重连 + JSONB 索引 + Token 监控）
3. 编写单元测试
4. 集成测试
