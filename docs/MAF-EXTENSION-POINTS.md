# MAF 框架扩展点指南

> **版本**: MAF 1.1.0 | **更新日期**: 2026-04-20
> **目的**: 说明 MAF 框架已提供的扩展点，避免重复造轮子

---

## 核心原则

**不要重复造轮子！** MAF 框架已经提供了以下扩展点：

1. ✅ **Context Providers** - 上下文管理扩展点
2. ✅ **History Providers** - 对话历史管理扩展点
3. ✅ **AgentSession 序列化** - 内置的会话持久化支持
4. ✅ **Workflow State API** - 共享状态管理
5. ⚠️ **Checkpoint** - 需要自己实现（框架提供基础 API）

---

## 1. Context Providers（上下文管理扩展点）

### 框架提供的接口

MAF 框架提供了 `IContextProvider` 接口，用于扩展 Agent 的上下文管理。

```csharp
// 框架提供的接口（推测）
public interface IContextProvider
{
    Task<List<ChatMessage>> GetContextAsync(
        AgentSession session, 
        CancellationToken ct = default);
    
    Task UpdateContextAsync(
        AgentSession session, 
        List<ChatMessage> messages, 
        CancellationToken ct = default);
}
```

### 内置的 History Provider

框架已经提供了 `InMemoryHistoryProvider`：

```csharp
// Python 示例（.NET 类似）
from agent_framework import ChatAgent, InMemoryHistoryProvider

# 使用内置的 InMemoryHistoryProvider
agent = ChatAgent(
    chat_client=client,
    name="assistant",
    context_providers=[
        InMemoryHistoryProvider(source_id="memory")
    ]
)

# 创建会话并自动管理历史
session = agent.create_session()
response = await agent.run("Hello!", session=session)
response = await agent.run("What did I say?", session=session)  # 自动记住历史
```

### 自定义 Context Provider（推荐方式）

**不要自己实现完整的上下文管理，而是扩展框架提供的 Provider！**

```csharp
// 自定义 Context Provider：实现智能摘要
public class SummarizationContextProvider : IContextProvider
{
    private readonly IChatClient _chatClient;
    private readonly int _maxTurns;
    private readonly int _summaryThreshold;
    
    public SummarizationContextProvider(
        IChatClient chatClient,
        int maxTurns = 10,
        int summaryThreshold = 30)
    {
        _chatClient = chatClient;
        _maxTurns = maxTurns;
        _summaryThreshold = summaryThreshold;
    }
    
    public async Task<List<ChatMessage>> GetContextAsync(
        AgentSession session,
        CancellationToken ct = default)
    {
        var history = session.GetHistory();
        
        // 如果历史不长，直接返回
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
    
    public async Task UpdateContextAsync(
        AgentSession session,
        List<ChatMessage> messages,
        CancellationToken ct = default)
    {
        // 更新会话历史
        session.AddMessages(messages);
    }
    
    private List<ChatMessage> ExtractKeyMessages(List<ChatMessage> history)
    {
        // 提取包含决策、错误、重要发现的消息
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
                "Summarize the following conversation in 3-5 sentences, " +
                "focusing on key decisions and findings."),
            new ChatMessage(ChatRole.User, historyText)
        }, cancellationToken: ct);
        
        return response.Message.Text;
    }
}
```

### 使用自定义 Context Provider

```csharp
// 创建 Agent 时注入自定义 Provider
var agent = chatClient.CreateAIAgent(
    name: "SqlAnalyzer",
    instructions: "You are a SQL optimization expert.",
    contextProviders: new IContextProvider[]
    {
        new SummarizationContextProvider(chatClient, maxTurns: 10, summaryThreshold: 30)
    }
);

// 框架会自动调用 Provider 管理上下文
var session = agent.CreateSession();
var response = await agent.RunAsync("Analyze this SQL...", session);
```

---

## 2. AgentSession 序列化（内置支持）

### 框架已提供的序列化方法

**不要自己实现序列化逻辑！** 框架已经提供了 `to_dict()` 和 `from_dict()` 方法。

```csharp
// .NET 版本（基于 Python 文档推测）
public class AgentSession
{
    public string SessionId { get; set; }
    public string? ServiceSessionId { get; set; }
    public Dictionary<string, object> State { get; set; }
    
    // 框架提供的序列化方法
    public Dictionary<string, object> ToDict()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "session",
            ["session_id"] = SessionId,
            ["service_session_id"] = ServiceSessionId,
            ["state"] = State
        };
    }
    
    // 框架提供的反序列化方法
    public static AgentSession FromDict(Dictionary<string, object> data)
    {
        var session = new AgentSession
        {
            SessionId = data["session_id"].ToString(),
            ServiceSessionId = data.ContainsKey("service_session_id") 
                ? data["service_session_id"]?.ToString() 
                : null,
            State = data.ContainsKey("state") 
                ? (Dictionary<string, object>)data["state"] 
                : new Dictionary<string, object>()
        };
        return session;
    }
}
```

### 正确的持久化方式

```csharp
// ✅ 正确：使用框架提供的方法
public async Task SaveSessionAsync(AgentSession session, CancellationToken ct)
{
    // 1. 使用框架的序列化方法
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

public async Task<AgentSession> LoadSessionAsync(string sessionId, CancellationToken ct)
{
    // 1. 从数据库加载
    var entity = await _db.AgentSessions.FindAsync(sessionId);
    if (entity == null) return null;
    
    // 2. 使用框架的反序列化方法
    var sessionDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
        entity.StateJson);
    var session = AgentSession.FromDict(sessionDict);
    
    return session;
}

// ❌ 错误：不要自己实现序列化逻辑
// 不要写自己的 SerializeSession / DeserializeSession 方法
```

---

## 3. Workflow State API（框架提供）

### 框架已提供的 State 管理 API

**不要自己实现 State 管理！** 框架的 `IWorkflowContext` 已经提供了完整的 API。

```csharp
// 框架提供的接口
public interface IWorkflowContext
{
    // ✅ 框架已提供
    Task SetStateAsync<T>(string key, T value, CancellationToken ct = default);
    Task<T?> GetStateAsync<T>(string key, CancellationToken ct = default);
    Task RemoveStateAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, object>> GetAllStateAsync(CancellationToken ct = default);
    
    // ✅ 框架已提供
    Task SendMessageAsync<T>(T message, CancellationToken ct = default);
    Task YieldOutputAsync<T>(T output, CancellationToken ct = default);
}
```

### 正确的使用方式

```csharp
// ✅ 正确：直接使用框架 API
[SendsMessage(typeof(AnalysisResult))]
internal sealed partial class AnalyzeExecutor : Executor("Analyze")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string input,
        IWorkflowContext context,  // 框架注入
        CancellationToken ct = default)
    {
        var analysis = Analyze(input);
        
        // 直接使用框架的 State API
        await context.SetStateAsync("analysis_result", analysis, ct);
        await context.SetStateAsync("complexity", analysis.Complexity, ct);
        
        await context.SendMessageAsync(analysis, ct);
    }
}

// ❌ 错误：不要自己实现 State 管理
// 不要写 StateManager、StateStore 等类
```

### State 持久化（需要自己实现）

框架提供了 State API，但**持久化到数据库需要自己实现**：

```csharp
// ✅ 正确：使用框架 API + 自己实现持久化
public async Task SaveWorkflowStateAsync(
    string sessionId,
    IWorkflowContext context,
    CancellationToken ct)
{
    // 1. 使用框架 API 获取所有状态
    var state = await context.GetAllStateAsync(ct);
    
    // 2. 自己实现持久化逻辑
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
    // 1. 从数据库加载
    var entity = await _db.WorkflowStates
        .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
    if (entity == null) return;
    
    var state = JsonSerializer.Deserialize<Dictionary<string, object>>(
        entity.StateJson);
    
    // 2. 使用框架 API 恢复状态
    foreach (var (key, value) in state)
    {
        await context.SetStateAsync(key, value, ct);
    }
}
```

---

## 4. Checkpoint（需要自己实现）

### 框架提供的基础 API

MAF 框架**没有内置的 Checkpoint 机制**，但提供了基础 API：

```csharp
// 框架提供的基础 API
public class Workflow
{
    // ✅ 框架提供：获取当前状态
    public Task<WorkflowState> GetStateAsync(CancellationToken ct = default);
    
    // ✅ 框架提供：恢复状态
    public Task RestoreStateAsync(
        Dictionary<string, object> state, 
        CancellationToken ct = default);
    
    // ✅ 框架提供：获取所有 Agent
    public List<AIAgent> GetAgents();
    
    // ✅ 框架提供：设置当前执行位置
    public Task SetCurrentExecutorAsync(
        string executorId, 
        CancellationToken ct = default);
}

public class AIAgent
{
    // ✅ 框架提供：获取会话
    public Task<AgentSession> GetSessionAsync(
        string sessionId, 
        CancellationToken ct = default);
    
    // ✅ 框架提供：恢复会话
    public Task RestoreSessionAsync(
        AgentSession session, 
        CancellationToken ct = default);
}
```

### 正确的 Checkpoint 实现方式

**使用框架提供的 API，自己实现 Checkpoint 逻辑：**

```csharp
// ✅ 正确：基于框架 API 实现 Checkpoint
public class WorkflowCheckpointManager
{
    private readonly IWorkflowCheckpointRepository _repo;
    
    // 保存 Checkpoint
    public async Task<string> SaveCheckpointAsync(
        Workflow workflow,
        string sessionId,
        CancellationToken ct)
    {
        // 1. 使用框架 API 获取 Workflow 状态
        var workflowState = await workflow.GetStateAsync(ct);
        
        // 2. 使用框架 API 获取所有 Agent 会话
        var agentSessions = new List<AgentSession>();
        foreach (var agent in workflow.GetAgents())
        {
            var session = await agent.GetSessionAsync(sessionId, ct);
            if (session != null)
            {
                // 使用框架的序列化方法
                agentSessions.Add(session);
            }
        }
        
        // 3. 构建 Checkpoint 数据
        var checkpoint = new WorkflowCheckpointData
        {
            CheckpointId = Guid.NewGuid().ToString(),
            WorkflowId = workflow.Id,
            SessionId = sessionId,
            SharedState = workflowState.SharedState,
            AgentSessions = agentSessions.Select(s => s.ToDict()).ToList(),
            CurrentExecutorId = workflowState.CurrentExecutorId,
            CreatedAt = DateTime.UtcNow
        };
        
        // 4. 持久化到数据库
        await _repo.SaveAsync(checkpoint, ct);
        
        return checkpoint.CheckpointId;
    }
    
    // 恢复 Checkpoint
    public async Task<Workflow> RestoreCheckpointAsync(
        string checkpointId,
        IWorkflowFactory workflowFactory,
        CancellationToken ct)
    {
        // 1. 从数据库加载
        var checkpoint = await _repo.LoadAsync(checkpointId, ct);
        
        // 2. 重建 Workflow
        var workflow = await workflowFactory.CreateWorkflowAsync(
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
}

// ❌ 错误：不要自己实现 Workflow.GetState() 等方法
// 不要写 WorkflowStateManager、WorkflowSerializer 等类
```

---

## 5. 对话历史压缩（Compaction）

### 框架提供的 Compaction 支持

根据官方文档，MAF 框架提供了 **Conversation Compaction** 功能：

- 📚 官方文档：[Conversation Compaction](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/compaction)
- 📚 博客文章：[Managing Chat History for LLMs](https://devblogs.microsoft.com/agent-framework/managing-chat-history-for-large-language-models-llms/)

**框架可能提供了以下扩展点**（需要查看最新文档确认）：

```csharp
// 推测的框架接口
public interface IConversationCompactor
{
    Task<List<ChatMessage>> CompactAsync(
        List<ChatMessage> history,
        int maxTokens,
        CancellationToken ct = default);
}

// 可能的内置实现
public class SlidingWindowCompactor : IConversationCompactor
{
    // 保留最近 N 条消息
}

public class SummarizationCompactor : IConversationCompactor
{
    // 使用 LLM 摘要历史
}
```

### 推荐做法

1. **先查看框架文档**：确认框架是否已提供 Compaction 功能
2. **使用框架提供的 Compactor**：如果有，直接使用
3. **实现自定义 Compactor**：如果框架提供了接口，实现自己的 Compactor
4. **集成到 Context Provider**：将 Compactor 集成到自定义的 Context Provider 中

---

## 总结：什么需要自己实现，什么不需要

### ✅ 框架已提供（不要重复造轮子）

1. **Context Providers 接口** - 扩展上下文管理
2. **InMemoryHistoryProvider** - 内置的历史管理
3. **AgentSession.ToDict() / FromDict()** - 会话序列化
4. **IWorkflowContext State API** - 共享状态管理
5. **Workflow.GetStateAsync() / RestoreStateAsync()** - 状态获取和恢复
6. **AIAgent.GetSessionAsync() / RestoreSessionAsync()** - 会话获取和恢复
7. **Conversation Compaction**（可能） - 对话压缩

### ⚠️ 需要自己实现（基于框架 API）

1. **自定义 Context Provider** - 实现智能摘要、滑动窗口等策略
2. **State 持久化** - 将 State 保存到数据库
3. **Checkpoint 管理** - 保存和恢复 Checkpoint
4. **Agent 执行记录** - 记录 Agent 调用、Token 使用
5. **Workflow 执行记录** - 记录 Workflow 执行过程
6. **决策记录** - 记录 Agent 决策和证据链

### 🎯 关键原则

1. **优先使用框架 API**：不要自己实现框架已提供的功能
2. **扩展而非替换**：实现自定义 Provider，而不是替换框架机制
3. **持久化是你的责任**：框架提供内存管理，持久化需要自己实现
4. **查看最新文档**：MAF 1.1.0 可能有新功能，优先查看官方文档

---

## 参考资源

- [Conversation Management](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/)
- [Conversation Compaction](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/compaction)
- [Managing Chat History for LLMs](https://devblogs.microsoft.com/agent-framework/managing-chat-history-for-large-language-models-llms/)
- [Semantic Kernel Python Context Management](https://devblogs.microsoft.com/agent-framework/semantic-kernel-python-context-management/)

---

## 下一步行动

1. ✅ 查看 MAF 1.1.0 官方文档，确认框架提供的扩展点
2. ✅ 实现自定义 `SummarizationContextProvider`
3. ✅ 实现 `WorkflowCheckpointManager`（基于框架 API）
4. ✅ 实现数据库持久化层
5. ✅ 集成到 `MafWorkflowRuntime`

**记住：不要重复造轮子，优先使用框架提供的 API 和扩展点！**
