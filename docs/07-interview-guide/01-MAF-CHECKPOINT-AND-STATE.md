# MAF Workflow Checkpoint 与状态管理

**面试重点**：展示对 MAF 状态持久化、恢复机制、三层状态分离的深度理解

---

## 一、核心问题

### Q1: 为什么需要 Checkpoint？

**标准答案**：

Checkpoint 解决三个核心问题：

1. **进程重启恢复**：API 进程崩溃或重启后，能从上次执行点继续
2. **长时间运行**：支持 Human-in-the-loop 场景，workflow 可能等待数小时甚至数天
3. **资源释放**：workflow 挂起时释放内存，避免占用过多资源

**反例**：

如果没有 checkpoint，所有 workflow 状态只在内存中：
- 进程重启 → 所有运行中的 workflow 丢失
- 等待审核的 workflow → 必须一直占用内存
- 无法横向扩展 → 单进程承载所有 workflow

---

## 二、三层状态分离设计

### 架构图

```
┌─────────────────────────────────────────────────────────────┐
│                      API / UI 查询层                          │
│  GET /api/workflows/{sessionId}                              │
│  → 返回: status, progress, currentStep, result               │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│              业务状态层 (workflow_sessions)                    │
│  - session_id                                                │
│  - workflow_type: "SqlAnalysis" / "DbConfigOptimization"     │
│  - status: "Running" / "WaitingForReview" / "Completed"      │
│  - progress: 0.6                                             │
│  - current_step: "IndexAdvisor"                              │
│  - result: WorkflowResultEnvelope (JSON)                     │
│  - source_type: "manual" / "slow-query"                      │
│  - source_ref_id: slow_query_id                              │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│              MAF 运行态层 (engine_* 字段)                      │
│  - engine_type: "MAF"                                        │
│  - engine_run_id: MAF 内部 run ID                            │
│  - engine_checkpoint_ref: checkpoint 存储引用                 │
│  - engine_state: MAF 序列化的完整状态 (JSONB)                │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│              领域结果层 (业务对象)                             │
│  SqlAnalysisWorkflowState:                                   │
│    - databaseId, sqlText                                     │
│    - draftResult: WorkflowResultEnvelope                     │
│    - finalResult: WorkflowResultEnvelope                     │
│                                                              │
│  DbConfigWorkflowState:                                      │
│    - databaseId, databaseType                                │
│    - draftResult, finalResult                                │
└─────────────────────────────────────────────────────────────┘
```

### 为什么要分三层？

| 层级 | 职责 | 消费者 | 变更频率 |
|------|------|--------|---------|
| **业务状态层** | 面向 API/UI 的可查询状态 | 前端、Dashboard、History | 每个 executor 完成后更新 |
| **MAF 运行态层** | 面向 MAF resume 的运行态引用 | MAF Runtime | 每个 checkpoint 保存时更新 |
| **领域结果层** | 面向业务逻辑的强类型对象 | Executor、Domain Service | workflow 执行过程中频繁读写 |

**反模式**：

❌ 把三层混在一起：
```csharp
// 错误示例
public class WorkflowSession
{
    public Guid SessionId { get; set; }
    public string Status { get; set; }  // 业务状态
    public string MafRunId { get; set; }  // MAF 运行态
    public OptimizationReport Result { get; set; }  // 领域结果
    // 三层耦合，无法独立演进
}
```

✅ 分层设计：
```csharp
// 正确示例
public class WorkflowSessionEntity  // 业务状态层
{
    public Guid SessionId { get; set; }
    public string Status { get; set; }
    public string EngineRunId { get; set; }  // 指向 MAF 运行态
    public string EngineCheckpointRef { get; set; }
}

public class MafCheckpointEnvelope  // MAF 运行态层
{
    public string RunId { get; set; }
    public JsonElement SharedState { get; set; }  // 包含领域结果
}

public class SqlAnalysisWorkflowState  // 领域结果层
{
    public WorkflowResultEnvelope? DraftResult { get; set; }
}
```

---

## 三、Checkpoint 保存时机

### 关键时机

```csharp
public class SqlParserMafExecutor : IExecutor<SqlAnalysisWorkflowCommand, SqlParsingCompletedMessage>
{
    public async ValueTask<SqlParsingCompletedMessage> HandleAsync(
        SqlAnalysisWorkflowCommand command,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // 1. 执行业务逻辑
        var parseResult = await _sqlParser.ParseAsync(command.SqlText);
        
        // 2. 更新 workflow state
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        state.ParseResult = parseResult;
        context.Set("state", state);
        
        // 3. MAF 自动保存 checkpoint（每个 executor 完成后）
        // 无需手动调用，MAF runtime 会在 executor 返回后自动触发
        
        return new SqlParsingCompletedMessage(parseResult);
    }
}
```

### Checkpoint 内容

```json
{
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "workflowType": "SqlAnalysis",
  "engineType": "MAF",
  "runId": "maf-run-12345",
  "checkpointRef": "checkpoint-67890",
  "status": "Running",
  "currentNode": "IndexAdvisor",
  "sharedState": {
    "state": {
      "sessionId": "550e8400-e29b-41d4-a716-446655440000",
      "databaseId": "mysql-prod-01",
      "sqlText": "SELECT * FROM users WHERE email = 'test@example.com'",
      "parseResult": { ... },
      "executionPlan": { ... }
    }
  },
  "pendingRequests": [],
  "updatedAt": "2026-04-17T10:30:00Z"
}
```

---

## 四、恢复流程

### 场景 1: 进程重启后恢复

```csharp
public class MafWorkflowRuntime : IMafWorkflowRuntime
{
    public async Task<WorkflowResumeResponse> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        // 1. 从数据库读取 checkpoint
        var checkpoint = await _stateStore.GetCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint == null)
        {
            return WorkflowResumeResponse.NotFound(sessionId);
        }
        
        // 2. 重建 workflow 实例
        var workflow = checkpoint.WorkflowType switch
        {
            "SqlAnalysis" => _factory.BuildSqlAnalysisWorkflow(),
            "DbConfigOptimization" => _factory.BuildDbConfigWorkflow(),
            _ => throw new InvalidOperationException($"Unknown workflow type: {checkpoint.WorkflowType}")
        };
        
        // 3. 恢复 shared state
        var context = new WorkflowContext();
        foreach (var kvp in checkpoint.SharedState.EnumerateObject())
        {
            context.Set(kvp.Name, kvp.Value);
        }
        
        // 4. 从 currentNode 继续执行
        await workflow.ResumeFromAsync(checkpoint.CurrentNode, context, cancellationToken);
        
        return WorkflowResumeResponse.Success(sessionId);
    }
}
```

### 场景 2: 审核提交后恢复

```csharp
public class ReviewApi
{
    [HttpPost("api/reviews/{taskId}/submit")]
    public async Task<IActionResult> SubmitReview(Guid taskId, [FromBody] SubmitReviewRequest request)
    {
        // 1. 读取 review task（包含 correlation 字段）
        var reviewTask = await _reviewService.GetAsync(taskId);
        
        // 2. 构造 response message
        var responseMessage = new ReviewDecisionResponseMessage
        {
            RequestId = reviewTask.RequestId,  // 关键：关联到原 request
            Decision = request.Decision,
            Reason = request.Reason,
            Adjustments = request.Adjustments
        };
        
        // 3. 恢复 workflow（MAF 会根据 requestId 找到挂起点）
        await _workflowRuntime.ResumeWithResponseAsync(
            reviewTask.SessionId,
            responseMessage,
            cancellationToken);
        
        return Ok();
    }
}
```

---

## 五、双层存储策略

### 为什么需要双层？

| 存储层 | 用途 | 数据结构 | TTL |
|--------|------|---------|-----|
| **PostgreSQL** | 持久化存储，进程重启后恢复 | `workflow_sessions.engine_state` (JSONB) | 永久 |
| **Redis** | 热点缓存，加速恢复 | `checkpoint:{sessionId}` (JSON String) | 24 小时 |

### 实现

```csharp
public class MafRunStateStore : IMafRunStateStore
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    private readonly IConnectionMultiplexer _redis;
    
    public async Task SaveAsync(MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(checkpoint);
        
        // 1. 保存到 PostgreSQL（持久化）
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.WorkflowSessions.FindAsync(checkpoint.SessionId);
        session.EngineRunId = checkpoint.RunId;
        session.EngineCheckpointRef = checkpoint.CheckpointRef;
        session.EngineState = json;
        session.UpdatedAt = checkpoint.UpdatedAt;
        await db.SaveChangesAsync(cancellationToken);
        
        // 2. 保存到 Redis（热点缓存）
        var redisDb = _redis.GetDatabase();
        await redisDb.StringSetAsync(
            $"checkpoint:{checkpoint.SessionId}",
            json,
            TimeSpan.FromHours(24));
    }
    
    public async Task<MafCheckpointEnvelope?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // 1. 先查 Redis
        var redisDb = _redis.GetDatabase();
        var cached = await redisDb.StringGetAsync($"checkpoint:{sessionId}");
        if (cached.HasValue)
        {
            return JsonSerializer.Deserialize<MafCheckpointEnvelope>(cached!);
        }
        
        // 2. Redis miss，查 PostgreSQL
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.WorkflowSessions.FindAsync(sessionId);
        if (session?.EngineState == null)
        {
            return null;
        }
        
        var checkpoint = JsonSerializer.Deserialize<MafCheckpointEnvelope>(session.EngineState);
        
        // 3. 回填 Redis
        if (checkpoint != null)
        {
            await redisDb.StringSetAsync(
                $"checkpoint:{sessionId}",
                session.EngineState,
                TimeSpan.FromHours(24));
        }
        
        return checkpoint;
    }
}
```

---

## 六、面试追问点

### Q: Checkpoint 太大怎么办？

**答**：

1. **分离大对象**：把执行计划、索引分析结果等大对象单独存储，checkpoint 只保存引用
2. **压缩**：使用 Brotli 压缩 JSONB 字段
3. **增量 checkpoint**：只保存变更部分（MAF 未来可能支持）

```csharp
// 分离大对象示例
public class SqlAnalysisWorkflowState
{
    public Guid SessionId { get; init; }
    public string SqlText { get; init; }
    
    // 不直接存储大对象
    public Guid? ExecutionPlanId { get; set; }  // 引用
    public Guid? IndexAnalysisId { get; set; }  // 引用
}

// 大对象单独存储
public class ExecutionPlanEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string PlanJson { get; set; }  // 可能几 MB
}
```

### Q: 如何保证 checkpoint 一致性？

**答**：

1. **事务边界**：checkpoint 保存与业务状态更新在同一事务中
2. **版本号**：checkpoint 带版本号，避免并发覆盖
3. **幂等性**：恢复操作必须幂等，重复恢复不会产生副作用

```csharp
public async Task SaveCheckpointAsync(MafCheckpointEnvelope checkpoint)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    await using var transaction = await db.Database.BeginTransactionAsync();
    
    try
    {
        // 1. 更新 workflow_sessions
        var session = await db.WorkflowSessions.FindAsync(checkpoint.SessionId);
        session.EngineState = JsonSerializer.Serialize(checkpoint);
        session.Version++;  // 乐观锁
        
        // 2. 更新 review_tasks（如果有）
        if (checkpoint.PendingRequests.Any())
        {
            var reviewTask = await db.ReviewTasks
                .FirstOrDefaultAsync(t => t.SessionId == checkpoint.SessionId && t.Status == "Pending");
            if (reviewTask != null)
            {
                reviewTask.EngineRunId = checkpoint.RunId;
                reviewTask.EngineCheckpointRef = checkpoint.CheckpointRef;
            }
        }
        
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
```

### Q: 如何处理 checkpoint 过期？

**答**：

1. **TTL 策略**：Redis 24 小时，PostgreSQL 保留 30 天
2. **清理任务**：定时清理已完成/已取消的 checkpoint
3. **归档**：重要 workflow 的 checkpoint 归档到对象存储

```csharp
public class CheckpointCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            
            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
            
            // 清理 30 天前已完成的 checkpoint
            var cutoff = DateTime.UtcNow.AddDays(-30);
            await db.WorkflowSessions
                .Where(s => s.Status == "Completed" && s.UpdatedAt < cutoff)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EngineState, (string?)null), stoppingToken);
        }
    }
}
```

---

## 七、关键代码位置

| 文件 | 职责 |
|------|------|
| `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs` | Checkpoint 存储接口 |
| `src/DbOptimizer.Infrastructure/Maf/Runtime/MafRunStateStore.cs` | 双层存储实现 |
| `src/DbOptimizer.Infrastructure/Workflows/State/IWorkflowStateStore.cs` | 业务状态存储接口 |
| `src/DbOptimizer.Infrastructure/Persistence/Entities/WorkflowSessionEntity.cs` | 业务状态实体 |
| `docs/03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md` | 设计文档 |

---

## 八、总结

### 核心要点

1. **三层分离**：业务状态、MAF 运行态、领域结果各司其职
2. **双层存储**：PostgreSQL 持久化 + Redis 热点缓存
3. **自动保存**：MAF 在每个 executor 完成后自动触发 checkpoint
4. **恢复机制**：支持进程重启恢复、审核提交恢复

### 面试加分项

- 能画出三层状态分离架构图
- 能解释为什么需要双层存储
- 能说出 checkpoint 一致性保证方案
- 能举例说明大对象分离策略
