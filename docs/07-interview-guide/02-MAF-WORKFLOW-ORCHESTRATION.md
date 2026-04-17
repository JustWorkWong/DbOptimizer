# MAF Workflow 编排与 Executor 设计

**面试重点**：展示对 MAF graph-based workflow、typed executor、request/response 模式的深度理解

---

## 一、核心概念

### Q1: MAF Workflow 与传统状态机的区别？

**标准答案**：

| 维度 | 传统状态机 | MAF Workflow |
|------|-----------|--------------|
| **编排方式** | 硬编码状态转移 | Graph-based 声明式编排 |
| **类型安全** | 弱类型消息 | Typed executor + typed message |
| **暂停/恢复** | 需要自己实现 | 原生支持 checkpoint |
| **可观测性** | 需要手动埋点 | 自动记录 executor 执行轨迹 |
| **Human-in-the-loop** | 需要轮询数据库 | 原生 request/response 模式 |

**示例对比**：

❌ 传统状态机：
```csharp
public async Task RunWorkflowAsync(WorkflowContext context)
{
    while (context.Status != "Completed")
    {
        switch (context.CurrentStep)
        {
            case "Parse":
                await ParseSqlAsync(context);
                context.CurrentStep = "ExecutionPlan";
                break;
            case "ExecutionPlan":
                await GetExecutionPlanAsync(context);
                context.CurrentStep = "IndexAdvisor";
                break;
            // 硬编码，难以扩展
        }
        await SaveCheckpointAsync(context);  // 手动保存
    }
}
```

✅ MAF Workflow：
```csharp
var workflow = new WorkflowBuilder()
    .AddExecutor<SqlInputValidationExecutor>()
    .AddExecutor<SqlParserMafExecutor>()
    .AddExecutor<ExecutionPlanMafExecutor>()
    .AddExecutor<IndexAdvisorMafExecutor>()
    .AddExecutor<SqlRewriteMafExecutor>()
    .AddExecutor<SqlCoordinatorMafExecutor>()
    .AddExecutor<SqlHumanReviewGateExecutor>()
    .Build();

// 声明式编排，MAF 自动处理执行顺序、checkpoint、恢复
```

---

## 二、SQL 分析 Workflow 完整编排

### 架构图

```
┌─────────────────────────────────────────────────────────────┐
│                    SQL Analysis Workflow                     │
└─────────────────────────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlInputValidationExecutor             │
        │  Input: SqlAnalysisWorkflowCommand      │
        │  Output: SqlValidatedMessage            │
        └─────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlParserMafExecutor                   │
        │  Input: SqlValidatedMessage             │
        │  Output: SqlParsingCompletedMessage     │
        │  职责: 解析 SQL，提取表名、字段、JOIN   │
        └─────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  ExecutionPlanMafExecutor               │
        │  Input: SqlParsingCompletedMessage      │
        │  Output: ExecutionPlanCompletedMessage  │
        │  职责: 通过 MCP 获取执行计划            │
        └─────────────────────────────────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
    ┌───────────────────────┐   ┌───────────────────────┐
    │ IndexAdvisorExecutor  │   │ SqlRewriteExecutor    │
    │ (可选，根据 option)    │   │ (可选，根据 option)    │
    └───────────────────────┘   └───────────────────────┘
                    ↓                   ↓
                    └─────────┬─────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlCoordinatorMafExecutor              │
        │  Input: IndexRecommendationCompleted +  │
        │         SqlRewriteCompleted             │
        │  Output: SqlOptimizationDraftReady      │
        │  职责: 聚合结果，生成 draft report       │
        └─────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlHumanReviewGateExecutor             │
        │  Input: SqlOptimizationDraftReady       │
        │  Output: ReviewDecisionResponse         │
        │  职责: 创建 review task，挂起 workflow  │
        └─────────────────────────────────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
            ┌───────────────┐   ┌───────────────┐
            │   Approved    │   │   Rejected    │
            └───────────────┘   └───────────────┘
                    ↓                   ↓
            ┌───────────────┐   ┌───────────────┐
            │  Completed    │   │ Regeneration  │
            └───────────────┘   └───────────────┘
```

### 代码实现

```csharp
public class MafWorkflowFactory : IMafWorkflowFactory
{
    public Workflow BuildSqlAnalysisWorkflow()
    {
        var builder = new WorkflowBuilder();
        
        // 1. 输入验证
        builder.AddExecutor<SqlInputValidationExecutor>();
        
        // 2. SQL 解析
        builder.AddExecutor<SqlParserMafExecutor>();
        
        // 3. 执行计划分析
        builder.AddExecutor<ExecutionPlanMafExecutor>();
        
        // 4. 并行分支：索引推荐 + SQL 重写
        builder.AddExecutor<IndexAdvisorMafExecutor>();
        builder.AddExecutor<SqlRewriteMafExecutor>();
        
        // 5. 结果聚合
        builder.AddExecutor<SqlCoordinatorMafExecutor>();
        
        // 6. Human-in-the-loop gate
        builder.AddExecutor<SqlHumanReviewGateExecutor>();
        
        return builder.Build();
    }
}
```

---

## 三、Typed Executor 设计

### 核心接口

```csharp
public interface IExecutor<TIn, TOut>
{
    ValueTask<TOut> HandleAsync(
        TIn input,
        IWorkflowContext context,
        CancellationToken cancellationToken);
}
```

### 示例：SQL 解析 Executor

```csharp
public class SqlParserMafExecutor : IExecutor<SqlValidatedMessage, SqlParsingCompletedMessage>
{
    private readonly ISqlParser _sqlParser;
    private readonly ILogger<SqlParserMafExecutor> _logger;
    
    public async ValueTask<SqlParsingCompletedMessage> HandleAsync(
        SqlValidatedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "开始解析 SQL，SessionId: {SessionId}",
            input.SessionId);
        
        // 1. 从 context 读取 workflow state
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 2. 执行业务逻辑（复用现有 deterministic service）
        var parseResult = await _sqlParser.ParseAsync(
            state.SqlText,
            state.DatabaseEngine,
            cancellationToken);
        
        // 3. 更新 state
        state.ParseResult = parseResult;
        context.Set("state", state);
        
        // 4. 返回 typed message
        return new SqlParsingCompletedMessage
        {
            SessionId = input.SessionId,
            Tables = parseResult.Tables,
            Columns = parseResult.Columns,
            JoinConditions = parseResult.JoinConditions,
            WhereConditions = parseResult.WhereConditions
        };
    }
}
```

### 关键设计原则

#### 1. 单一职责

每个 executor 只做一件事：

✅ **好的设计**：
- `SqlParserMafExecutor`：只负责解析 SQL
- `ExecutionPlanMafExecutor`：只负责获取执行计划
- `IndexAdvisorMafExecutor`：只负责索引推荐

❌ **坏的设计**：
```csharp
public class SqlAnalysisExecutor  // 违反单一职责
{
    public async ValueTask<SqlAnalysisResult> HandleAsync(...)
    {
        var parseResult = await ParseSqlAsync();
        var plan = await GetExecutionPlanAsync();
        var indexes = await RecommendIndexesAsync();
        var rewrite = await RewriteSqlAsync();
        // 一个 executor 做了太多事情，无法独立测试和复用
    }
}
```

#### 2. 复用现有领域服务

Executor 是薄层，不应包含复杂业务逻辑：

```csharp
public class IndexAdvisorMafExecutor : IExecutor<ExecutionPlanCompletedMessage, IndexRecommendationCompletedMessage>
{
    private readonly IIndexAdvisor _indexAdvisor;  // 复用现有服务
    
    public async ValueTask<IndexRecommendationCompletedMessage> HandleAsync(
        ExecutionPlanCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 检查 option
        if (!state.Options.EnableIndexRecommendation)
        {
            return new IndexRecommendationCompletedMessage
            {
                SessionId = input.SessionId,
                Recommendations = Array.Empty<IndexRecommendation>()
            };
        }
        
        // 委托给领域服务
        var recommendations = await _indexAdvisor.AnalyzeAsync(
            state.ParseResult,
            state.ExecutionPlan,
            cancellationToken);
        
        return new IndexRecommendationCompletedMessage
        {
            SessionId = input.SessionId,
            Recommendations = recommendations
        };
    }
}
```

#### 3. Option 控制流程

通过 option 控制 gate 行为，而不是改变 graph 结构：

```csharp
public class SqlRewriteMafExecutor : IExecutor<ExecutionPlanCompletedMessage, SqlRewriteCompletedMessage>
{
    public async ValueTask<SqlRewriteCompletedMessage> HandleAsync(
        ExecutionPlanCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // Option gate: 如果禁用 SQL 重写，返回空结果
        if (!state.Options.EnableSqlRewrite)
        {
            return new SqlRewriteCompletedMessage
            {
                SessionId = input.SessionId,
                Suggestions = Array.Empty<SqlRewriteSuggestion>()
            };
        }
        
        // 执行 SQL 重写逻辑
        var suggestions = await _sqlRewriteAdvisor.AnalyzeAsync(
            state.ParseResult,
            state.ExecutionPlan,
            cancellationToken);
        
        return new SqlRewriteCompletedMessage
        {
            SessionId = input.SessionId,
            Suggestions = suggestions
        };
    }
}
```

---

## 四、消息流设计

### 消息类型

```csharp
// 1. Command（启动 workflow）
public sealed record SqlAnalysisWorkflowCommand
{
    public required Guid SessionId { get; init; }
    public required string DatabaseId { get; init; }
    public required string DatabaseEngine { get; init; }
    public required string SqlText { get; init; }
    public required SqlAnalysisOptions Options { get; init; }
}

// 2. Event（executor 之间传递）
public sealed record SqlParsingCompletedMessage
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<string> Tables { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<JoinCondition> JoinConditions { get; init; }
}

public sealed record ExecutionPlanCompletedMessage
{
    public required Guid SessionId { get; init; }
    public required ExecutionPlan Plan { get; init; }
}

public sealed record IndexRecommendationCompletedMessage
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<IndexRecommendation> Recommendations { get; init; }
}

public sealed record SqlRewriteCompletedMessage
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<SqlRewriteSuggestion> Suggestions { get; init; }
}

// 3. Draft Ready（进入 review gate）
public sealed record SqlOptimizationDraftReadyMessage
{
    public required Guid SessionId { get; init; }
    public required WorkflowResultEnvelope DraftResult { get; init; }
}

// 4. Request/Response（Human-in-the-loop）
public sealed record ReviewDecisionResponseMessage
{
    public required Guid SessionId { get; init; }
    public required string Decision { get; init; }  // "Approved" / "Rejected"
    public required string? Reason { get; init; }
}

// 5. Completion（workflow 结束）
public sealed record SqlOptimizationCompletedMessage
{
    public required Guid SessionId { get; init; }
    public required WorkflowResultEnvelope FinalResult { get; init; }
}
```

### 消息流转示例

```
SqlAnalysisWorkflowCommand
  ↓
SqlValidatedMessage
  ↓
SqlParsingCompletedMessage
  ↓
ExecutionPlanCompletedMessage
  ↓ (并行)
  ├─→ IndexRecommendationCompletedMessage
  └─→ SqlRewriteCompletedMessage
  ↓ (聚合)
SqlOptimizationDraftReadyMessage
  ↓ (挂起，等待审核)
ReviewDecisionResponseMessage
  ↓
SqlOptimizationCompletedMessage
```

---

## 五、Coordinator Executor 设计

### 职责

Coordinator 负责聚合多个并行分支的结果：

```csharp
public class SqlCoordinatorMafExecutor : IExecutor<
    (IndexRecommendationCompletedMessage, SqlRewriteCompletedMessage),
    SqlOptimizationDraftReadyMessage>
{
    private readonly IWorkflowResultSerializer _serializer;
    
    public async ValueTask<SqlOptimizationDraftReadyMessage> HandleAsync(
        (IndexRecommendationCompletedMessage indexMsg, SqlRewriteCompletedMessage rewriteMsg) input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 1. 聚合结果
        var report = new OptimizationReport
        {
            Summary = GenerateSummary(input.indexMsg, input.rewriteMsg),
            IndexRecommendations = input.indexMsg.Recommendations,
            SqlRewriteSuggestions = input.rewriteMsg.Suggestions,
            ExecutionPlan = state.ExecutionPlan,
            OverallConfidence = CalculateConfidence(input.indexMsg, input.rewriteMsg),
            Warnings = CollectWarnings(state.ExecutionPlan)
        };
        
        // 2. 转换为统一结果壳
        var envelope = _serializer.ToEnvelope(
            report,
            state.DatabaseId,
            state.DatabaseEngine);
        
        // 3. 保存 draft result
        state.DraftResult = envelope;
        context.Set("state", state);
        
        return new SqlOptimizationDraftReadyMessage
        {
            SessionId = state.SessionId,
            DraftResult = envelope
        };
    }
    
    private string GenerateSummary(
        IndexRecommendationCompletedMessage indexMsg,
        SqlRewriteCompletedMessage rewriteMsg)
    {
        var parts = new List<string>();
        
        if (indexMsg.Recommendations.Any())
        {
            parts.Add($"发现 {indexMsg.Recommendations.Count} 个索引优化建议");
        }
        
        if (rewriteMsg.Suggestions.Any())
        {
            parts.Add($"发现 {rewriteMsg.Suggestions.Count} 个 SQL 重写建议");
        }
        
        return parts.Any()
            ? string.Join("，", parts)
            : "未发现明显优化点";
    }
}
```

---

## 六、Human-in-the-loop Gate 设计

### Review Gate Executor

```csharp
public class SqlHumanReviewGateExecutor : IExecutor<
    SqlOptimizationDraftReadyMessage,
    ReviewDecisionResponseMessage>
{
    private readonly IWorkflowReviewTaskGateway _reviewGateway;
    
    public async ValueTask<ReviewDecisionResponseMessage> HandleAsync(
        SqlOptimizationDraftReadyMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 1. 检查是否需要人工审核
        if (!state.Options.RequireHumanReview)
        {
            // 直接通过
            return new ReviewDecisionResponseMessage
            {
                SessionId = state.SessionId,
                Decision = "Approved",
                Reason = "自动通过（未启用人工审核）"
            };
        }
        
        // 2. 创建 review task
        var reviewTask = await _reviewGateway.CreateReviewTaskAsync(
            sessionId: state.SessionId,
            workflowType: "SqlAnalysis",
            draftResult: input.DraftResult,
            cancellationToken: cancellationToken);
        
        // 3. 生成 request（MAF 会自动挂起 workflow）
        var request = new ReviewRequest
        {
            RequestId = reviewTask.RequestId,
            TaskId = reviewTask.TaskId,
            SessionId = state.SessionId,
            DraftResult = input.DraftResult
        };
        
        // 4. 发送 request 并等待 response
        // MAF 会在这里挂起 workflow，保存 checkpoint
        var response = await context.SendRequestAsync<ReviewRequest, ReviewDecisionResponseMessage>(
            request,
            cancellationToken);
        
        // 5. 处理审核结果
        if (response.Decision == "Rejected")
        {
            // 驳回：触发重新生成逻辑
            state.RejectionReason = response.Reason;
            context.Set("state", state);
            
            // 可以在这里触发 regeneration executor
            // 或者直接标记为 failed
        }
        
        return response;
    }
}
```

### Request/Response 关联

```csharp
public class WorkflowReviewTaskGateway : IWorkflowReviewTaskGateway
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    
    public async Task<ReviewTaskCorrelation> CreateReviewTaskAsync(
        Guid sessionId,
        string workflowType,
        WorkflowResultEnvelope draftResult,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        // 1. 生成 correlation IDs
        var requestId = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid();
        
        // 2. 从 workflow_sessions 读取 MAF 运行态
        var session = await db.WorkflowSessions.FindAsync(
            new object[] { sessionId },
            cancellationToken);
        
        // 3. 创建 review task，持久化 correlation
        var reviewTask = new ReviewTaskEntity
        {
            Id = taskId,
            SessionId = sessionId,
            WorkflowType = workflowType,
            Recommendations = JsonSerializer.Serialize(draftResult),
            Status = "Pending",
            RequestId = requestId,  // 关键：持久化 request ID
            EngineRunId = session.EngineRunId,  // 关键：持久化 run ID
            EngineCheckpointRef = session.EngineCheckpointRef,  // 关键：持久化 checkpoint ref
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        db.ReviewTasks.Add(reviewTask);
        await db.SaveChangesAsync(cancellationToken);
        
        return new ReviewTaskCorrelation
        {
            TaskId = taskId,
            RequestId = requestId,
            SessionId = sessionId
        };
    }
}
```

---

## 七、面试高频问题

### Q1: 如何保证 executor 的幂等性？

**答案**：

1. **Checkpoint 机制**：MAF 在每个 executor 完成后保存 checkpoint，重试时从上次成功的 executor 继续
2. **消息去重**：每个消息携带 `SessionId`，可以通过 session 状态判断是否已处理
3. **领域服务幂等**：底层领域服务（如 SQL 解析）本身是纯函数，天然幂等

### Q2: 如何处理 executor 执行失败？

**答案**：

```csharp
public class ExecutionPlanMafExecutor : IExecutor<SqlParsingCompletedMessage, ExecutionPlanCompletedMessage>
{
    public async ValueTask<ExecutionPlanCompletedMessage> HandleAsync(
        SqlParsingCompletedMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var plan = await _mcpClient.GetExecutionPlanAsync(...);
            return new ExecutionPlanCompletedMessage { Plan = plan };
        }
        catch (McpTimeoutException ex)
        {
            // 1. 记录错误
            _logger.LogWarning(ex, "MCP 超时，使用 fallback");
            
            // 2. 使用 fallback
            var fallbackPlan = await _fallbackStrategy.GetExecutionPlanAsync(...);
            
            // 3. 标记为降级
            context.Set("usedFallback", true);
            
            return new ExecutionPlanCompletedMessage
            {
                Plan = fallbackPlan,
                IsFallback = true
            };
        }
        catch (Exception ex)
        {
            // 致命错误：抛出异常，MAF 会标记 workflow 为 failed
            _logger.LogError(ex, "执行计划获取失败");
            throw;
        }
    }
}
```

### Q3: 如何实现并行执行？

**答案**：

MAF 支持声明式并行：

```csharp
var builder = new WorkflowBuilder();

// 串行
builder.AddExecutor<SqlParserMafExecutor>();
builder.AddExecutor<ExecutionPlanMafExecutor>();

// 并行：两个 executor 同时消费 ExecutionPlanCompletedMessage
builder.AddExecutor<IndexAdvisorMafExecutor>();  // 并行分支 1
builder.AddExecutor<SqlRewriteMafExecutor>();    // 并行分支 2

// 聚合：等待两个分支都完成
builder.AddExecutor<SqlCoordinatorMafExecutor>();
```

### Q4: 如何测试 executor？

**答案**：

```csharp
public class SqlParserMafExecutorTests
{
    [Fact]
    public async Task HandleAsync_ShouldParseSimpleSelect()
    {
        // Arrange
        var mockParser = new Mock<ISqlParser>();
        mockParser.Setup(x => x.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new SqlParseResult
            {
                Tables = new[] { "users" },
                Columns = new[] { "id", "email" }
            });
        
        var executor = new SqlParserMafExecutor(mockParser.Object, Mock.Of<ILogger<SqlParserMafExecutor>>());
        
        var input = new SqlValidatedMessage
        {
            SessionId = Guid.NewGuid(),
            SqlText = "SELECT id, email FROM users"
        };
        
        var context = new TestWorkflowContext();
        context.Set("state", new SqlAnalysisWorkflowState
        {
            SessionId = input.SessionId,
            SqlText = input.SqlText,
            DatabaseEngine = "mysql"
        });
        
        // Act
        var result = await executor.HandleAsync(input, context, default);
        
        // Assert
        result.Tables.Should().Contain("users");
        result.Columns.Should().Contain("id", "email");
        
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        state.ParseResult.Should().NotBeNull();
    }
}
```

---

## 八、总结

### 核心优势

1. **声明式编排**：graph-based，易于理解和维护
2. **类型安全**：typed executor + typed message，编译时检查
3. **原生 checkpoint**：自动保存/恢复，无需手动实现
4. **可观测性**：自动记录 executor 执行轨迹
5. **Human-in-the-loop**：原生 request/response，无需轮询

### 设计原则

1. **单一职责**：每个 executor 只做一件事
2. **复用领域服务**：executor 是薄层，不包含复杂业务逻辑
3. **Option 控制流程**：通过 option 控制 gate 行为，而不是改变 graph 结构
4. **幂等性**：利用 checkpoint 机制保证幂等
5. **错误处理**：区分可恢复错误（fallback）和致命错误（抛出异常）

### 面试加分项

- 能画出完整的 workflow graph
- 能解释 checkpoint 保存时机和恢复流程
- 能说明 request/response 如何实现 Human-in-the-loop
- 能对比传统状态机和 MAF workflow 的优劣
- 能举例说明如何测试 executor
