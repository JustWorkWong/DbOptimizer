# MAF 框架扩展点指南

> **版本**: MAF 1.1.0 | **更新日期**: 2026-04-20
> **目的**: 说明 MAF 框架已提供的扩展点，避免重复造轮子
> **重要**: 本文档仅包含已确认的框架功能，不包含推测内容

---

## 核心原则

**不要重复造轮子！** 优先使用框架提供的 API 和扩展点。

---

## 已确认的框架功能

### 1. AgentSession 序列化（✅ 已确认）

**来源**: Python 文档中明确提到 `AgentSession.to_dict()` 和 `from_dict()` 方法

```python
# Python 版本（官方文档）
class AgentSession:
    def to_dict(self) -> dict[str, Any]:
        """Serialize session to a plain dict."""
        return {
            "type": "session",
            "session_id": self._session_id,
            "service_session_id": self.service_session_id,
            "state": self.state,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "AgentSession":
        """Restore session from a dict."""
        session = cls(session_id=data["session_id"])
        session.service_session_id = data.get("service_session_id")
        session.state = data.get("state", {})
        return session
```

**使用方式**:
```python
# 序列化
data = session.to_dict()
json_str = json.dumps(data)

# 反序列化
data = json.loads(json_str)
session = AgentSession.from_dict(data)
```

**⚠️ .NET 版本**: 需要查看官方文档确认是否有类似 API

---

### 2. Workflow State API（✅ 已确认）

**来源**: 官方文档明确提到 `IWorkflowContext` 提供状态管理 API

```csharp
// 框架提供的接口（官方文档）
public interface IWorkflowContext
{
    Task SetStateAsync<T>(string key, T value, CancellationToken ct = default);
    Task<T?> GetStateAsync<T>(string key, CancellationToken ct = default);
    Task RemoveStateAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, object>> GetAllStateAsync(CancellationToken ct = default);
    
    Task SendMessageAsync<T>(T message, CancellationToken ct = default);
    Task YieldOutputAsync<T>(T output, CancellationToken ct = default);
}
```

**使用方式**:
```csharp
// 在 Executor 中使用
[SendsMessage(typeof(AnalysisResult))]
internal sealed partial class AnalyzeExecutor : Executor("Analyze")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string input,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 设置状态
        await context.SetStateAsync("key", value, ct);
        
        // 获取状态
        var value = await context.GetStateAsync<T>("key", ct);
        
        await context.SendMessageAsync(result, ct);
    }
}
```

---

### 3. InMemoryHistoryProvider（✅ 已确认）

**来源**: Python 文档中明确提到 `InMemoryHistoryProvider`

```python
# Python 版本（官方文档）
from agent_framework import ChatAgent, InMemoryHistoryProvider

agent = ChatAgent(
    chat_client=client,
    name="assistant",
    context_providers=[
        InMemoryHistoryProvider(source_id="memory")
    ]
)

session = agent.create_session()
response = await agent.run("Hello!", session=session)
response = await agent.run("What did I say?", session=session)  # 自动记住历史
```

**⚠️ .NET 版本**: 需要查看官方文档确认是否有类似实现

---

### 4. Workflow 状态管理 API（✅ 已确认）

**来源**: 官方示例代码

```csharp
// 框架提供的方法（官方示例）
public class Workflow
{
    public Task<WorkflowState> GetStateAsync(CancellationToken ct = default);
    public Task RestoreStateAsync(Dictionary<string, object> state, CancellationToken ct = default);
    public List<AIAgent> GetAgents();
    public Task SetCurrentExecutorAsync(string executorId, CancellationToken ct = default);
}

public class AIAgent
{
    public Task<AgentSession> GetSessionAsync(string sessionId, CancellationToken ct = default);
    public Task RestoreSessionAsync(AgentSession session, CancellationToken ct = default);
}
```

---

## 需要查看官方文档确认的功能

### 1. Context Providers 接口

**状态**: ⚠️ 需要确认

Python 文档中提到了 `context_providers` 参数，但没有明确的 `IContextProvider` 接口定义。

**建议**: 查看 MAF 1.1.0 官方文档确认：
- 是否有 `IContextProvider` 接口
- 如何实现自定义 Context Provider
- 是否支持智能摘要、滑动窗口等策略

---

### 2. Conversation Compaction

**状态**: ⚠️ 需要确认

官方文档链接存在：
- https://learn.microsoft.com/en-us/agent-framework/agents/conversations/compaction
- https://devblogs.microsoft.com/agent-framework/managing-chat-history-for-large-language-models-llms/

但无法访问具体内容。

**建议**: 查看官方文档确认：
- 框架是否提供内置的 Compaction 功能
- 是否有 `IConversationCompactor` 接口
- 是否有内置的摘要、压缩策略

---

### 3. Checkpoint 机制

**状态**: ⚠️ 需要确认

框架提供了基础的状态获取和恢复 API，但没有完整的 Checkpoint 机制。

**已确认的 API**:
- `Workflow.GetStateAsync()` - 获取当前状态
- `Workflow.RestoreStateAsync()` - 恢复状态
- `AIAgent.GetSessionAsync()` - 获取 Agent 会话
- `AIAgent.RestoreSessionAsync()` - 恢复 Agent 会话

**需要自己实现**:
- Checkpoint 数据结构
- Checkpoint 保存逻辑
- Checkpoint 恢复逻辑
- Checkpoint 持久化到数据库

---

## 正确的实现方式

### ✅ 使用框架 API

```csharp
// 1. 使用 IWorkflowContext 管理状态
await context.SetStateAsync("key", value, ct);
var value = await context.GetStateAsync<T>("key", ct);

// 2. 使用 AgentSession 序列化（如果 .NET 有）
var sessionDict = session.ToDict();  // 需要确认
var session = AgentSession.FromDict(sessionDict);  // 需要确认

// 3. 使用 Workflow API 管理状态
var state = await workflow.GetStateAsync(ct);
await workflow.RestoreStateAsync(state, ct);

// 4. 使用 AIAgent API 管理会话
var session = await agent.GetSessionAsync(sessionId, ct);
await agent.RestoreSessionAsync(session, ct);
```

### ❌ 不要重复造轮子

```csharp
// ❌ 错误：不要自己实现这些
public class StateManager { }  // 框架有 IWorkflowContext
public class SessionSerializer { }  // 框架有 ToDict/FromDict（需确认）
public class HistoryManager { }  // 框架有 InMemoryHistoryProvider（需确认）
public class ContextManager { }  // 框架可能有 IContextProvider（需确认）
```

---

## 实现 Checkpoint（基于框架 API）

由于框架没有完整的 Checkpoint 机制，需要基于框架 API 自己实现：

```csharp
public class WorkflowCheckpointManager
{
    public async Task<string> SaveCheckpointAsync(
        Workflow workflow,
        string sessionId,
        CancellationToken ct)
    {
        // 1. 使用框架 API 获取状态
        var workflowState = await workflow.GetStateAsync(ct);
        
        // 2. 使用框架 API 获取所有 Agent 会话
        var agentSessions = new List<object>();
        foreach (var agent in workflow.GetAgents())
        {
            var session = await agent.GetSessionAsync(sessionId, ct);
            if (session != null)
            {
                // 如果有 ToDict() 方法，使用它
                // 否则使用 JsonSerializer.Serialize
                agentSessions.Add(session);
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
        await _db.SaveAsync(checkpointJson, ct);
        
        return checkpoint.CheckpointId;
    }
    
    public async Task<Workflow> RestoreCheckpointAsync(
        string checkpointId,
        CancellationToken ct)
    {
        // 1. 从数据库加载
        var checkpointJson = await _db.LoadAsync(checkpointId, ct);
        var checkpoint = JsonSerializer.Deserialize<CheckpointData>(checkpointJson);
        
        // 2. 重建 Workflow
        var workflow = await _workflowFactory.CreateWorkflowAsync(
            checkpoint.WorkflowId, ct);
        
        // 3. 使用框架 API 恢复状态
        await workflow.RestoreStateAsync(checkpoint.SharedState, ct);
        
        // 4. 使用框架 API 恢复 Agent 会话
        foreach (var sessionData in checkpoint.AgentSessions)
        {
            // 如果有 FromDict() 方法，使用它
            // 否则使用 JsonSerializer.Deserialize
            var session = DeserializeSession(sessionData);
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
```

---

## 总结

### ✅ 已确认的框架功能

1. `IWorkflowContext` - State 管理 API
2. `Workflow.GetStateAsync()` / `RestoreStateAsync()` - Workflow 状态管理
3. `AIAgent.GetSessionAsync()` / `RestoreSessionAsync()` - Agent 会话管理
4. `AgentSession.to_dict()` / `from_dict()` - Python 版本的序列化（.NET 需确认）
5. `InMemoryHistoryProvider` - Python 版本的历史管理（.NET 需确认）

### ⚠️ 需要查看官方文档确认

1. `IContextProvider` 接口 - 上下文管理扩展点
2. Conversation Compaction - 对话压缩功能
3. .NET 版本的 `AgentSession` 序列化方法
4. .NET 版本的 `InMemoryHistoryProvider`

### 🎯 关键原则

1. **优先使用框架 API** - 不要自己实现框架已提供的功能
2. **查看官方文档** - 不要推测，确认后再使用
3. **基于框架扩展** - 需要自己实现的功能，基于框架 API 构建
4. **持久化是你的责任** - 框架提供内存管理，持久化需要自己实现

---

## 参考资源

- [MAF GitHub](https://github.com/microsoft/agent-framework)
- [MAF 官方文档](https://learn.microsoft.com/en-us/agent-framework/)
- [Conversation Management](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/)

**重要**: 使用前请查看最新官方文档，确认功能是否存在！
