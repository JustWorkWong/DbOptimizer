# MAF 最佳实践指南

> **版本**: MAF 1.1.0 | **更新日期**: 2026-04-20
> **原则**: 使用框架提供的扩展点，不要重复造轮子

---

## 📋 核心原则

### ✅ 使用框架提供的扩展点

1. **Context Providers** - 实现 `IContextProvider` 接口管理上下文
2. **AgentSession 序列化** - 使用 `session.ToDict()` / `AgentSession.FromDict()`
3. **Workflow State API** - 使用 `IWorkflowContext.SetStateAsync()` / `GetStateAsync()`
4. **Workflow 状态管理** - 使用 `workflow.GetStateAsync()` / `RestoreStateAsync()`

### ❌ 不要重复造轮子

- ❌ 不要自己实现 `StateManager`（框架有 `IWorkflowContext`）
- ❌ 不要自己实现 `SessionSerializer`（框架有 `ToDict/FromDict`）
- ❌ 不要自己实现 `HistoryManager`（框架有 `InMemoryHistoryProvider`）
- ❌ 不要自己实现完整的上下文管理（扩展 `IContextProvider`）

---

## 1. Agent 上下文管理（使用 Context Provider）

### 实现自定义 Context Provider

```csharp
// ✅ 正确：实现框架的 IContextProvider 接口
public class SummarizationContextProvider : IContextProvider
{
    private readonly IChatClient _chatClient;
    private readonly int _maxTurns = 10;
    private readonly int _summaryThreshold = 30;
    
    public async Task<List<ChatMessage>> GetContextAsync(
        AgentSession session,
        CancellationToken ct = default)
    {
        var history = session.GetHistory();
        
        if (history.Count <= _summaryThreshold)
            return history;
        
        // 提取关键对话
        var keyMessages = ExtractKeyMessages(history);
        
        // 摘要中间历史
        var middleHistory = history
            .Skip(keyMessages.Count)
            .Take(history.Count - keyMessages.Count - _maxTurns)
            .ToList();
        var summary = await SummarizeAsync(middleHistory, ct);
        
        // 保留最近对话
        var recentHistory = history.TakeLast(_maxTurns).ToList();
        
        // 组合：关键信息 + 摘要 + 最近对话
        var result = new List<ChatMessage>();
        result.AddRange(keyMessages);
        result.Add(new ChatMessage(ChatRole.System, 
            $"Previous conversation summary: {summary}"));
        result.AddRange(recentHistory);
        
        return result;
    }
    
    private List<ChatMessage> ExtractKeyMessages(List<ChatMessage> history)
    {
        return history.Where(m => 
            m.Content.Contains("decision", StringComparison.OrdinalIgnoreCase) ||
            m.Content.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            m.Content.Contains("important", StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }
    
    private async Task<string> SummarizeAsync(
        List<ChatMessage> history,
        CancellationToken ct)
    {
        var historyText = string.Join("\n", history.Select(m => m.Content));
        
        var response = await _chatClient.CompleteAsync(new[]
        {
            new ChatMessage(ChatRole.System, 
                "Summarize in 3-5 sentences, focusing on key decisions."),
            new ChatMessage(ChatRole.User, historyText)
        }, cancellationToken: ct);
        
        return response.Message.Text;
    }
}
```

### 使用自定义 Context Provider

```csharp
// 创建 Agent 时注入
var agent = chatClient.CreateAIAgent(
    name: "SqlAnalyzer",
    instructions: "You are a SQL optimization expert.",
    contextProviders: new IContextProvider[]
    {
        new SummarizationContextProvider(chatClient)
    }
);

// 框架会自动调用 Provider 管理上下文
var session = agent.CreateSession();
var response = await agent.RunAsync("Analyze this SQL...", session);
```

---

## 2. AgentSession 持久化（使用框架序列化）

### 保存会话

```csharp
// ✅ 正确：使用框架的序列化方法
public async Task SaveSessionAsync(AgentSession session, CancellationToken ct)
{
    // 1. 使用框架的 ToDict() 方法
    var sessionDict = session.ToDict();
    var sessionJson = JsonSerializer.Serialize(sessionDict);
    
    // 2. 保存到数据库
    await _db.AgentSessions.AddAsync(new AgentSessionEntity
    {
        SessionId = session.SessionId,
        StateJson = sessionJson,
        CreatedAt = DateTime.UtcNow
    }, ct);
    await _db.SaveChangesAsync(ct);
}
```

### 恢复会话

```csharp
// ✅ 正确：使用框架的反序列化方法
public async Task<AgentSession> LoadSessionAsync(string sessionId, CancellationToken ct)
{
    var entity = await _db.AgentSessions.FindAsync(sessionId);
    if (entity == null) return null;
    
    // 使用框架的 FromDict() 方法
    var sessionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
        entity.StateJson);
    var session = AgentSession.FromDict(sessionDict);
    
    return session;
}
```

---

## 3. Workflow State 管理（使用框架 API）

### 在 Executor 中使用 State

```csharp
// ✅ 正确：直接使用框架的 IWorkflowContext API
[SendsMessage(typeof(AnalysisResult))]
internal sealed partial class SqlAnalyzeExecutor : Executor("SqlAnalyze")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string sqlQuery,
        IWorkflowContext context,  // 框架注入
        CancellationToken ct = default)
    {
        var analysis = AnalyzeSql(sqlQuery);
        
        // 使用框架 API 设置状态
        await context.SetStateAsync("original_query", sqlQuery, ct);
        await context.SetStateAsync("complexity", analysis.Complexity, ct);
        
        await context.SendMessageAsync(analysis, ct);
    }
}

[YieldsOutput(typeof(OptimizationResult))]
internal sealed partial class GenerateRecommendationExecutor : Executor("GenerateRecommendation")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        IndexRecommendation indexRec,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 使用框架 API 读取状态
        var originalQuery = await context.GetStateAsync<string>("original_query", ct);
        var complexity = await context.GetStateAsync<int>("complexity", ct);
        
        var result = new OptimizationResult
        {
            OriginalQuery = originalQuery,
            Complexity = complexity,
            IndexRecommendations = indexRec.Recommendations
        };
        
        await context.YieldOutputAsync(result, ct);
    }
}
```

### State 持久化

```csharp
// ✅ 正确：使用框架 API + 自己实现持久化
public async Task SaveWorkflowStateAsync(
    string sessionId,
    IWorkflowContext context,
    CancellationToken ct)
{
    // 1. 使用框架 API 获取所有状态
    var state = await context.GetAllStateAsync(ct);
    
    // 2. 持久化到数据库
    var stateJson = JsonSerializer.Serialize(state);
    await _db.WorkflowStates.AddAsync(new WorkflowStateEntity
    {
        SessionId = sessionId,
        StateJson = stateJson,
        UpdatedAt = DateTime.UtcNow
    }, ct);
    await _db.SaveChangesAsync(ct);
}

public async Task RestoreWorkflowStateAsync(
    string sessionId,
    IWorkflowContext context,
    CancellationToken ct)
{
    var entity = await _db.WorkflowStates
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
    if (entity == null) return;
    
    var state = JsonSerializer.Deserialize<Dictionary<string, object>>(
        entity.StateJson);
    
    // 使用框架 API 恢复状态
    foreach (var (key, value) in state)
    {
        await context.SetStateAsync(key, value, ct);
    }
}
```

---

## 4. Checkpoint 管理（基于框架 API）

### 保存 Checkpoint

```csharp
// ✅ 正确：基于框架 API 实现 Checkpoint
public async Task<string> SaveCheckpointAsync(
    Workflow workflow,
    string sessionId,
    CancellationToken ct)
{
    // 1. 使用框架 API 获取 Workflow 状态
    var workflowState = await workflow.GetStateAsync(ct);
    
    // 2. 使用框架 API 获取所有 Agent 会话
    var agentSessions = new List<Dictionary<string, object>>();
    foreach (var agent in workflow.GetAgents())
    {
        var session = await agent.GetSessionAsync(sessionId, ct);
        if (session != null)
        {
            // 使用框架的序列化方法
            agentSessions.Add(session.ToDict());
        }
    }
    
    // 3. 构建 Checkpoint 数据
    var checkpoint = new
    {
        CheckpointId = Guid.NewGuid().ToString(),
        WorkflowId = workflow.Id,
        SessionId = sessionId,
        SharedState = workflowState.SharedState,
        AgentSessions = agentSessions,
        CurrentExecutorId = workflowState.CurrentExecutorId,
        CreatedAt = DateTime.UtcNow
    };
    
    // 4. 持久化到数据库
    var checkpointJson = JsonSerializer.Serialize(checkpoint);
    await _db.WorkflowCheckpoints.AddAsync(new WorkflowCheckpointEntity
    {
        CheckpointId = checkpoint.CheckpointId,
        SessionId = sessionId,
        DataJson = checkpointJson,
        CreatedAt = checkpoint.CreatedAt
    }, ct);
    await _db.SaveChangesAsync(ct);
    
    return checkpoint.CheckpointId;
}
```

### 恢复 Checkpoint

```csharp
// ✅ 正确：使用框架 API 恢复
public async Task<Workflow> RestoreCheckpointAsync(
    string checkpointId,
    CancellationToken ct)
{
    // 1. 从数据库加载
    var entity = await _db.WorkflowCheckpoints
        .FirstOrDefaultAsync(c => c.CheckpointId == checkpointId, ct);
    
    var checkpoint = JsonSerializer.Deserialize<CheckpointData>(entity.DataJson);
    
    // 2. 重建 Workflow
    var workflow = await _workflowFactory.CreateWorkflowAsync(
        checkpoint.WorkflowId, ct);
    
    // 3. 使用框架 API 恢复 Workflow 状态
    await workflow.RestoreStateAsync(checkpoint.SharedState, ct);
    
    // 4. 使用框架 API 恢复所有 Agent 会话
    foreach (var agentSessionDict in checkpoint.AgentSessions)
    {
        var session = AgentSession.FromDict(agentSessionDict);
        var agent = workflow.GetAgent(session.SessionId);
        if (agent != null)
        {
            await agent.RestoreSessionAsync(session, ct);
        }
    }
    
    // 5. 使用框架 API 设置执行位置
    await workflow.SetCurrentExecutorAsync(checkpoint.CurrentExecutorId, ct);
    
    return workflow;
}
```

---

## 5. 数据库设计

### 核心表结构

```sql
-- 1. Agent 会话表（存储序列化的 AgentSession）
CREATE TABLE agent_sessions (
    session_id UUID PRIMARY KEY,
    workflow_session_id UUID,
    agent_name VARCHAR(100) NOT NULL,
    state_json JSONB NOT NULL,  -- AgentSession.ToDict() 的结果
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_agent_sessions_workflow ON agent_sessions(workflow_session_id);

-- 2. Workflow 状态表（存储 IWorkflowContext.GetAllStateAsync() 的结果）
CREATE TABLE workflow_states (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL UNIQUE,
    workflow_id VARCHAR(100) NOT NULL,
    state_json JSONB NOT NULL,  -- GetAllStateAsync() 的结果
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_workflow_states_session ON workflow_states(session_id);

-- 3. Workflow Checkpoint 表
CREATE TABLE workflow_checkpoints (
    checkpoint_id UUID PRIMARY KEY,
    session_id UUID NOT NULL,
    workflow_id VARCHAR(100) NOT NULL,
    data_json JSONB NOT NULL,  -- 包含 WorkflowState + AgentSessions
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_checkpoints_session ON workflow_checkpoints(session_id);
CREATE INDEX idx_checkpoints_created ON workflow_checkpoints(created_at DESC);

-- 4. Agent 执行记录表（用于调试和成本追踪）
CREATE TABLE agent_executions (
    execution_id UUID PRIMARY KEY,
    session_id UUID NOT NULL,
    agent_name VARCHAR(100) NOT NULL,
    prompt_version_id UUID,
    input TEXT,
    output TEXT,
    input_tokens INT,
    output_tokens INT,
    total_tokens INT,
    started_at TIMESTAMP NOT NULL,
    completed_at TIMESTAMP,
    duration_ms INT,
    status VARCHAR(20) NOT NULL,
    error_message TEXT
);

CREATE INDEX idx_agent_exec_session ON agent_executions(session_id);
CREATE INDEX idx_agent_exec_agent ON agent_executions(agent_name);

-- 5. Workflow 执行记录表
CREATE TABLE workflow_executions (
    execution_id UUID PRIMARY KEY,
    workflow_id VARCHAR(100) NOT NULL,
    session_id UUID NOT NULL,
    input TEXT,
    output TEXT,
    started_at TIMESTAMP NOT NULL,
    completed_at TIMESTAMP,
    status VARCHAR(20) NOT NULL,
    total_tokens INT,
    estimated_cost DECIMAL(10, 4)
);

CREATE INDEX idx_workflow_exec_session ON workflow_executions(session_id);
CREATE INDEX idx_workflow_exec_workflow ON workflow_executions(workflow_id);
```

---

## 6. 完整实现示例

### MafWorkflowRuntime（集成所有最佳实践）

```csharp
public class MafWorkflowRuntime : IMafWorkflowRuntime
{
    private readonly IWorkflowFactory _workflowFactory;
    private readonly IAgentSessionRepository _sessionRepo;
    private readonly IWorkflowStateRepository _stateRepo;
    private readonly ICheckpointRepository _checkpointRepo;
    private readonly ISseService _sseService;
    
    public async Task<WorkflowResult> ExecuteAsync(
        WorkflowRequest request,
        CancellationToken ct)
    {
        // 1. 创建或恢复 Workflow
        Workflow workflow;
        if (!string.IsNullOrEmpty(request.CheckpointId))
        {
            workflow = await RestoreCheckpointAsync(request.CheckpointId, ct);
        }
        else
        {
            workflow = await _workflowFactory.CreateWorkflowAsync(
                request.WorkflowId, ct);
        }
        
        // 2. 执行 Workflow（流式）
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow,
            input: request.Input,
            cancellationToken: ct
        );
        
        var result = new WorkflowResult { SessionId = request.SessionId };
        
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case WorkflowStepEvent step:
                    // 发送 SSE 事件
                    await _sseService.SendEventAsync(request.SessionId, new
                    {
                        Type = "workflow_step",
                        Step = step.StepName
                    });
                    
                    // 检查是否需要保存 Checkpoint
                    if (ShouldSaveCheckpoint(step.StepName))
                    {
                        await SaveCheckpointAsync(workflow, request.SessionId, ct);
                    }
                    break;
                
                case WorkflowOutputEvent output:
                    result.Output = output.Data?.ToString();
                    result.Status = WorkflowStatus.Completed;
                    break;
                
                case WorkflowErrorEvent error:
                    result.Status = WorkflowStatus.Failed;
                    result.Error = error.Message;
                    break;
            }
        }
        
        // 3. 保存最终状态
        await SaveWorkflowStateAsync(request.SessionId, workflow, ct);
        
        return result;
    }
    
    private bool ShouldSaveCheckpoint(string executorId)
    {
        // 关键步骤后保存
        var criticalExecutors = new[] 
        { 
            "SqlAnalyzeExecutor", 
            "IndexAnalyzeExecutor",
            "WaitForReviewExecutor"  // 人工审核前
        };
        return criticalExecutors.Contains(executorId);
    }
}
```

---

## 总结

### ✅ 使用框架提供的

1. `IContextProvider` - 上下文管理扩展点
2. `AgentSession.ToDict()` / `FromDict()` - 会话序列化
3. `IWorkflowContext.SetStateAsync()` / `GetStateAsync()` - 状态管理
4. `Workflow.GetStateAsync()` / `RestoreStateAsync()` - Workflow 状态
5. `AIAgent.GetSessionAsync()` / `RestoreSessionAsync()` - Agent 会话

### ⚠️ 需要自己实现

1. 自定义 `IContextProvider`（智能摘要策略）
2. 数据库持久化层
3. Checkpoint 管理逻辑
4. Agent/Workflow 执行记录

### 🎯 关键原则

**不要重复造轮子！优先使用框架提供的 API 和扩展点！**
