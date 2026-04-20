# MAF 数据持久化补充指南

> 本文档是 `MAF-PERSISTENCE-GUIDE.md` 的补充，重点说明 Agent 上下文管理、Workflow State、Checkpoint 和数据库设计

---

## Agent 上下文管理

### 上下文管理的挑战

1. **Token 限制**: LLM 有上下文窗口限制（如 GPT-4 128K tokens）
2. **成本问题**: 长上下文 = 高成本
3. **性能问题**: 长上下文 = 慢响应
4. **信息丢失**: 超出窗口的历史会丢失

### 解决方案：上下文摘要和缩减

#### 策略 1: 滑动窗口（保留最近 N 轮对话）

```csharp
public class SlidingWindowContextManager
{
    private readonly int _maxTurns;
    
    public SlidingWindowContextManager(int maxTurns = 10)
    {
        _maxTurns = maxTurns;
    }
    
    public List<ConversationTurn> ManageContext(List<ConversationTurn> history)
    {
        if (history.Count <= _maxTurns)
            return history;
        
        // 保留系统消息 + 最近 N 轮
        var systemMessages = history.Where(t => 
            t.Messages.Any(m => m.Role == "system")).ToList();
        var recentTurns = history.TakeLast(_maxTurns).ToList();
        
        return systemMessages.Concat(recentTurns).ToList();
    }
}
```

#### 策略 2: 智能摘要（使用 LLM 压缩历史）

```csharp
public class SummarizationContextManager
{
    private readonly IChatClient _chatClient;
    private readonly int _summaryThreshold;
    
    public async Task<List<ConversationTurn>> ManageContextAsync(
        List<ConversationTurn> history,
        CancellationToken ct = default)
    {
        if (history.Count <= _summaryThreshold)
            return history;
        
        // 1. 分割：旧历史 + 最近对话
        var oldHistory = history.Take(history.Count - 10).ToList();
        var recentHistory = history.TakeLast(10).ToList();
        
        // 2. 摘要旧历史
        var summary = await SummarizeHistoryAsync(oldHistory, ct);
        
        // 3. 构建新上下文：摘要 + 最近对话
        var newHistory = new List<ConversationTurn>
        {
            new ConversationTurn
            {
                Type = "request",
                Messages = new List<ChatMessage>
                {
                    new ChatMessage
                    {
                        Role = "system",
                        Contents = new List<MessageContent>
                        {
                            new TextContent 
                            { 
                                Text = $"Previous conversation summary: {summary}" 
                            }
                        }
                    }
                }
            }
        };
        newHistory.AddRange(recentHistory);
        
        return newHistory;
    }
    
    private async Task<string> SummarizeHistoryAsync(
        List<ConversationTurn> history,
        CancellationToken ct)
    {
        var historyText = string.Join("\n", history.SelectMany(t => 
            t.Messages.SelectMany(m => 
                m.Contents.OfType<TextContent>().Select(c => c.Text))));
        
        var response = await _chatClient.CompleteAsync(new[]
        {
            new ChatMessage(ChatRole.System, 
                "Summarize the following conversation history in 3-5 sentences, " +
                "focusing on key decisions, findings, and context."),
            new ChatMessage(ChatRole.User, historyText)
        }, cancellationToken: ct);
        
        return response.Message.Text;
    }
}
```

#### 策略 3: 混合策略（推荐）

```csharp
public class HybridContextManager
{
    private readonly IChatClient _chatClient;
    private readonly int _recentTurns = 10;
    private readonly int _summaryThreshold = 30;
    
    public async Task<List<ConversationTurn>> ManageContextAsync(
        List<ConversationTurn> history,
        CancellationToken ct = default)
    {
        if (history.Count <= _summaryThreshold)
            return history;
        
        // 1. 提取关键信息（决策、错误、重要发现）
        var keyTurns = ExtractKeyTurns(history);
        
        // 2. 摘要中间历史
        var middleHistory = history
            .Skip(keyTurns.Count)
            .Take(history.Count - keyTurns.Count - _recentTurns)
            .ToList();
        var summary = await SummarizeHistoryAsync(middleHistory, ct);
        
        // 3. 保留最近对话
        var recentHistory = history.TakeLast(_recentTurns).ToList();
        
        // 4. 组合：关键信息 + 摘要 + 最近对话
        var newHistory = new List<ConversationTurn>();
        newHistory.AddRange(keyTurns);
        newHistory.Add(CreateSummaryTurn(summary));
        newHistory.AddRange(recentHistory);
        
        return newHistory;
    }
    
    private List<ConversationTurn> ExtractKeyTurns(List<ConversationTurn> history)
    {
        return history.Where(t => 
            t.Messages.Any(m => 
                m.Contents.OfType<TextContent>().Any(c => 
                    c.Text.Contains("decision", StringComparison.OrdinalIgnoreCase) ||
                    c.Text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    c.Text.Contains("important", StringComparison.OrdinalIgnoreCase)
                )
            )
        ).ToList();
    }
}
```

### 上下文管理最佳实践

1. ✅ **监控 Token 使用**: 每次调用记录 Token 数
2. ✅ **动态调整策略**: 根据 Token 使用情况选择策略
3. ✅ **保留关键信息**: 决策、错误、重要发现不能丢失
4. ✅ **定期摘要**: 不要等到超限才摘要
5. ✅ **持久化原始历史**: 摘要后仍保存完整历史到数据库

---

## Workflow State 管理

### State 管理 API（MAF 1.1.0）

```csharp
// IWorkflowContext 提供的 State API
public interface IWorkflowContext
{
    // 设置状态
    Task SetStateAsync<T>(string key, T value, CancellationToken ct = default);
    
    // 获取状态
    Task<T?> GetStateAsync<T>(string key, CancellationToken ct = default);
    
    // 删除状态
    Task RemoveStateAsync(string key, CancellationToken ct = default);
    
    // 获取所有状态
    Task<Dictionary<string, object>> GetAllStateAsync(CancellationToken ct = default);
}
```

### State 使用示例（完整流程）

```csharp
// Executor 1: 分析 SQL 并设置状态
[SendsMessage(typeof(AnalysisResult))]
internal sealed partial class SqlAnalyzeExecutor : Executor("SqlAnalyze")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string sqlQuery,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        var analysis = AnalyzeSql(sqlQuery);
        
        // 保存分析结果到共享状态
        await context.SetStateAsync("original_query", sqlQuery, ct);
        await context.SetStateAsync("table_names", analysis.Tables, ct);
        await context.SetStateAsync("complexity_score", analysis.ComplexityScore, ct);
        await context.SetStateAsync("query_type", analysis.QueryType, ct);
        
        await context.SendMessageAsync(analysis, ct);
    }
}

// Executor 2: 索引分析（读取状态）
[SendsMessage(typeof(IndexRecommendation))]
internal sealed partial class IndexAnalyzeExecutor : Executor("IndexAnalyze")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        AnalysisResult analysis,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 读取共享状态
        var tables = await context.GetStateAsync<List<string>>("table_names", ct);
        var complexity = await context.GetStateAsync<int>("complexity_score", ct);
        
        // 根据复杂度决定分析策略
        var strategy = complexity > 80 ? "deep" : "quick";
        await context.SetStateAsync("analysis_strategy", strategy, ct);
        
        var recommendation = AnalyzeIndexes(tables, strategy);
        await context.SendMessageAsync(recommendation, ct);
    }
}

// Executor 3: 生成优化建议（读取所有状态）
[YieldsOutput(typeof(OptimizationResult))]
internal sealed partial class GenerateRecommendationExecutor : Executor("GenerateRecommendation")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        IndexRecommendation indexRec,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 读取所有相关状态
        var originalQuery = await context.GetStateAsync<string>("original_query", ct);
        var complexity = await context.GetStateAsync<int>("complexity_score", ct);
        var queryType = await context.GetStateAsync<string>("query_type", ct);
        var strategy = await context.GetStateAsync<string>("analysis_strategy", ct);
        
        // 生成最终建议
        var result = new OptimizationResult
        {
            OriginalQuery = originalQuery,
            Complexity = complexity,
            QueryType = queryType,
            Strategy = strategy,
            IndexRecommendations = indexRec.Recommendations,
            EstimatedImprovement = CalculateImprovement(complexity, indexRec)
        };
        
        await context.YieldOutputAsync(result, ct);
    }
}
```

### State 持久化

```csharp
// 保存 Workflow State 到数据库
public async Task SaveWorkflowStateAsync(
    string sessionId,
    IWorkflowContext context,
    CancellationToken ct)
{
    var state = await context.GetAllStateAsync(ct);
    var stateJson = JsonSerializer.Serialize(state);
    
    var entity = await _db.WorkflowStates
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
    
    if (entity == null)
    {
        await _db.WorkflowStates.AddAsync(new WorkflowStateEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            StateJson = stateJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);
    }
    else
    {
        entity.StateJson = stateJson;
        entity.UpdatedAt = DateTime.UtcNow;
    }
    
    await _db.SaveChangesAsync(ct);
}

// 恢复 Workflow State
public async Task RestoreWorkflowStateAsync(
    string sessionId,
    IWorkflowContext context,
    CancellationToken ct)
{
    var entity = await _db.WorkflowStates
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
    
    if (entity == null) return;
    
    var state = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.StateJson);
    foreach (var (key, value) in state)
    {
        await context.SetStateAsync(key, value, ct);
    }
}
```

---

## Checkpoint 最佳实践

### 何时保存 Checkpoint

```csharp
public class CheckpointStrategy
{
    private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(5);
    private DateTime _lastCheckpoint = DateTime.UtcNow;
    
    public bool ShouldSaveCheckpoint(
        string currentExecutorId,
        WorkflowExecutionContext context)
    {
        // 1. 时间间隔触发
        if (DateTime.UtcNow - _lastCheckpoint >= _autoSaveInterval)
            return true;
        
        // 2. 关键步骤完成后
        var criticalExecutors = new[] 
        { 
            "SqlAnalyzeExecutor", 
            "IndexAnalyzeExecutor", 
            "GenerateRecommendationExecutor" 
        };
        if (criticalExecutors.Contains(currentExecutorId))
            return true;
        
        // 3. 人工审核前
        if (currentExecutorId == "WaitForReviewExecutor")
            return true;
        
        // 4. Agent 调用完成后（Token 使用较多）
        if (context.LastExecutorType == "agent")
            return true;
        
        return false;
    }
}
```

### Checkpoint 完整实现

```csharp
// 保存 Checkpoint
public async Task<string> SaveCheckpointAsync(
    Workflow workflow,
    string sessionId,
    CancellationToken ct)
{
    // 1. 获取 Workflow 状态
    var workflowState = await workflow.GetStateAsync(ct);
    
    // 2. 获取所有 Agent 会话
    var agentSessions = new List<AgentSessionState>();
    foreach (var agent in workflow.GetAgents())
    {
        var session = await agent.GetSessionAsync(sessionId, ct);
        if (session != null)
        {
            agentSessions.Add(session);
        }
    }
    
    // 3. 构建 Checkpoint
    var checkpoint = new WorkflowCheckpointData
    {
        CheckpointId = Guid.NewGuid().ToString(),
        WorkflowId = workflow.Id,
        SessionId = sessionId,
        SchemaVersion = "1.0.0",
        SharedState = workflowState.SharedState,
        AgentSessions = agentSessions,
        CurrentExecutorId = workflowState.CurrentExecutorId,
        Status = workflowState.Status,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "system"
    };
    
    // 4. 序列化并保存到数据库
    var checkpointJson = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    });
    
    await _db.WorkflowCheckpoints.AddAsync(new WorkflowCheckpointEntity
    {
        CheckpointId = checkpoint.CheckpointId,
        SessionId = sessionId,
        WorkflowId = workflow.Id,
        DataJson = checkpointJson,
        SchemaVersion = checkpoint.SchemaVersion,
        CreatedAt = checkpoint.CreatedAt,
        CreatedBy = checkpoint.CreatedBy
    }, ct);
    
    await _db.SaveChangesAsync(ct);
    
    _logger.LogInformation(
        "Checkpoint saved: {CheckpointId} for session {SessionId}",
        checkpoint.CheckpointId, sessionId);
    
    return checkpoint.CheckpointId;
}

// 恢复 Checkpoint
public async Task<Workflow> RestoreFromCheckpointAsync(
    string checkpointId,
    CancellationToken ct)
{
    // 1. 从数据库加载 Checkpoint
    var entity = await _db.WorkflowCheckpoints
        .FirstOrDefaultAsync(c => c.CheckpointId == checkpointId, ct);
    
    if (entity == null)
        throw new InvalidOperationException($"Checkpoint {checkpointId} not found");
    
    var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpointData>(
        entity.DataJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    
    // 2. 重建 Workflow
    var workflow = await _workflowFactory.CreateWorkflowAsync(
        checkpoint.WorkflowId, 
        ct);
    
    // 3. 恢复共享状态
    await workflow.RestoreStateAsync(checkpoint.SharedState, ct);
    
    // 4. 恢复所有 Agent 会话
    foreach (var agentSession in checkpoint.AgentSessions)
    {
        var agent = workflow.GetAgent(agentSession.SessionId);
        if (agent != null)
        {
            await agent.RestoreSessionAsync(agentSession, ct);
        }
    }
    
    // 5. 设置当前执行位置
    await workflow.SetCurrentExecutorAsync(checkpoint.CurrentExecutorId, ct);
    
    _logger.LogInformation(
        "Checkpoint restored: {CheckpointId}, resuming from {ExecutorId}",
        checkpointId, checkpoint.CurrentExecutorId);
    
    return workflow;
}
```

---

## 数据库设计（完整 SQL）

```sql
-- 1. Agent 会话表
CREATE TABLE agent_sessions (
    session_id UUID PRIMARY KEY,
    workflow_session_id UUID,
    agent_name VARCHAR(100) NOT NULL,
    state_json JSONB NOT NULL,
    total_tokens INT DEFAULT 0,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_agent_sessions_workflow ON agent_sessions(workflow_session_id);
CREATE INDEX idx_agent_sessions_agent ON agent_sessions(agent_name);
CREATE INDEX idx_agent_sessions_updated ON agent_sessions(updated_at DESC);

-- 2. Workflow 状态表
CREATE TABLE workflow_states (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL UNIQUE,
    workflow_id VARCHAR(100) NOT NULL,
    state_json JSONB NOT NULL,
    current_executor_id VARCHAR(100),
    status VARCHAR(20) NOT NULL,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_workflow_states_session ON workflow_states(session_id);
CREATE INDEX idx_workflow_states_workflow ON workflow_states(workflow_id);
CREATE INDEX idx_workflow_states_status ON workflow_states(status);

-- 3. Workflow Checkpoint 表
CREATE TABLE workflow_checkpoints (
    checkpoint_id UUID PRIMARY KEY,
    session_id UUID NOT NULL,
    workflow_id VARCHAR(100) NOT NULL,
    data_json JSONB NOT NULL,
    schema_version VARCHAR(20) DEFAULT '1.0.0',
    created_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(100)
);

CREATE INDEX idx_checkpoints_session ON workflow_checkpoints(session_id);
CREATE INDEX idx_checkpoints_workflow ON workflow_checkpoints(workflow_id);
CREATE INDEX idx_checkpoints_created ON workflow_checkpoints(created_at DESC);

-- 4. Agent 执行记录表
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
    error_message TEXT,
    tool_calls_json JSONB
);

CREATE INDEX idx_agent_exec_session ON agent_executions(session_id);
CREATE INDEX idx_agent_exec_agent ON agent_executions(agent_name);
CREATE INDEX idx_agent_exec_started ON agent_executions(started_at DESC);
CREATE INDEX idx_agent_exec_status ON agent_executions(status);

-- 5. Workflow 执行记录表
CREATE TABLE workflow_executions (
    execution_id UUID PRIMARY KEY,
    workflow_id VARCHAR(100) NOT NULL,
    session_id UUID NOT NULL,
    input TEXT,
    output TEXT,
    started_at TIMESTAMP NOT NULL,
    completed_at TIMESTAMP,
    duration_ms INT,
    status VARCHAR(20) NOT NULL,
    error_message TEXT,
    steps_json JSONB,
    checkpoint_ids_json JSONB,
    total_tokens INT,
    estimated_cost DECIMAL(10, 4)
);

CREATE INDEX idx_workflow_exec_session ON workflow_executions(session_id);
CREATE INDEX idx_workflow_exec_workflow ON workflow_executions(workflow_id);
CREATE INDEX idx_workflow_exec_started ON workflow_executions(started_at DESC);
CREATE INDEX idx_workflow_exec_status ON workflow_executions(status);

-- 6. 决策记录表
CREATE TABLE decision_records (
    decision_id UUID PRIMARY KEY,
    session_id UUID NOT NULL,
    agent_name VARCHAR(100) NOT NULL,
    decision_type VARCHAR(50) NOT NULL,
    question TEXT NOT NULL,
    answer TEXT NOT NULL,
    reasoning TEXT,
    confidence DECIMAL(3, 2),
    evidences_json JSONB,
    created_at TIMESTAMP DEFAULT NOW(),
    created_by VARCHAR(100)
);

CREATE INDEX idx_decisions_session ON decision_records(session_id);
CREATE INDEX idx_decisions_agent ON decision_records(agent_name);
CREATE INDEX idx_decisions_type ON decision_records(decision_type);
CREATE INDEX idx_decisions_confidence ON decision_records(confidence);
CREATE INDEX idx_decisions_created ON decision_records(created_at DESC);

-- 7. Prompt 版本表（已存在，补充说明）
CREATE TABLE prompt_versions (
    id UUID PRIMARY KEY,
    agent_name VARCHAR(100) NOT NULL,
    version INT NOT NULL,
    content TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT NOW(),
    is_active BOOLEAN DEFAULT TRUE,
    UNIQUE(agent_name, version)
);

CREATE INDEX idx_prompt_versions_agent ON prompt_versions(agent_name);
CREATE INDEX idx_prompt_versions_active ON prompt_versions(agent_name, is_active);
```

---

## 总结

### 必须持久化的 6 大类数据

1. **AgentSession**: 对话历史、上下文、Token 使用
2. **WorkflowState**: 共享状态、执行位置
3. **WorkflowCheckpoint**: 完整快照、支持恢复
4. **AgentExecution**: Agent 调用记录、Tool 调用
5. **WorkflowExecution**: Workflow 执行记录、步骤追踪
6. **DecisionRecord**: 决策记录、置信度、证据链

### 关键最佳实践

1. **上下文管理**: 使用混合策略（摘要 + 滑动窗口 + 关键信息保留）
2. **Checkpoint**: 定期保存、关键步骤后保存、人工审核前保存、Agent 调用后保存
3. **State 管理**: 跨 Executor 共享数据，保持消息流清晰
4. **完整记录**: 所有执行细节都要记录，便于调试和优化
5. **成本追踪**: 记录 Token 使用，分析成本

### 避免的坑

- ❌ 不要只保存最终结果，中间状态也要保存
- ❌ 不要忽略 Token 使用统计
- ❌ 不要在消息中传递大量上下文（用 State）
- ❌ 不要等到超限才做上下文管理
- ❌ 不要忘记持久化 Agent 会话
- ❌ 不要忘记 Checkpoint 包含所有 Agent 会话

遵循本文档，你的 DbOptimizer 项目将拥有完整的 MAF 数据持久化能力！
