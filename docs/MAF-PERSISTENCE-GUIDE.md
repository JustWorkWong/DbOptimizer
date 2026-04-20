# MAF 数据持久化完整指南

> **版本**: MAF 1.1.0 | **更新日期**: 2026-04-20

## 📋 目录

1. [核心持久化对象](#核心持久化对象)
2. [Agent 上下文管理](#agent-上下文管理)
3. [Workflow State 管理](#workflow-state-管理)
4. [Checkpoint 最佳实践](#checkpoint-最佳实践)
5. [数据库设计](#数据库设计)
6. [完整实现示例](#完整实现示例)

---

## 核心持久化对象

### 必须持久化的数据（6 大类）

#### 1. AgentSession（Agent 会话状态）

**用途**: 保存 Agent 的对话历史和上下文

```csharp
// AgentSession 结构（基于 MAF 1.1.0）
public class AgentSessionState
{
    public string SessionId { get; set; }              // 会话 ID
    public string? ServiceSessionId { get; set; }      // 服务端会话 ID
    public Dictionary<string, object> State { get; set; } // 自定义状态
    public List<ConversationTurn> ConversationHistory { get; set; } // 对话历史
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TotalTokens { get; set; }               // 累计 Token 使用
}

// ConversationTurn 结构（完整对话轮次）
public class ConversationTurn
{
    public string Type { get; set; }  // "request" | "response"
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ChatMessage> Messages { get; set; }
    public TokenUsage? Usage { get; set; }  // Token 使用统计
}

// ChatMessage 结构
public class ChatMessage
{
    public string Role { get; set; }  // "user" | "assistant" | "tool" | "system"
    public string? AuthorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<MessageContent> Contents { get; set; }  // text | functionCall | functionResult
}

// MessageContent（多态内容）
public abstract class MessageContent
{
    public string Type { get; set; }  // "text" | "functionCall" | "functionResult"
}

public class TextContent : MessageContent
{
    public string Text { get; set; }
}

public class FunctionCallContent : MessageContent
{
    public string Name { get; set; }
    public string CallId { get; set; }
    public Dictionary<string, object> Arguments { get; set; }
}

public class FunctionResultContent : MessageContent
{
    public string CallId { get; set; }
    public string Result { get; set; }
}

// TokenUsage 统计
public class TokenUsage
{
    public int InputTokenCount { get; set; }
    public int OutputTokenCount { get; set; }
    public int TotalTokenCount { get; set; }
}
```

**为什么重要**:
- ✅ 多轮对话上下文保持
- ✅ Token 使用追踪和成本分析
- ✅ 调试和审计（完整对话记录）
- ✅ Function Call 追踪（Tool 调用链）

**序列化示例**:
```csharp
// 保存到数据库
var sessionJson = JsonSerializer.Serialize(session, new JsonSerializerOptions
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
});
await _db.AgentSessions.AddAsync(new AgentSessionEntity
{
    SessionId = session.SessionId,
    StateJson = sessionJson,
    CreatedAt = session.CreatedAt
});

// 从数据库恢复
var entity = await _db.AgentSessions.FindAsync(sessionId);
var session = JsonSerializer.Deserialize<AgentSessionState>(entity.StateJson);
```

#### 2. WorkflowState（Workflow 共享状态）

**用途**: Executor 之间共享数据，跨步骤传递上下文

```csharp
// WorkflowState 结构
public class WorkflowStateData
{
    public string WorkflowId { get; set; }
    public string SessionId { get; set; }
    public Dictionary<string, object> SharedState { get; set; }  // 共享状态字典
    public string CurrentExecutorId { get; set; }
    public WorkflowRunState Status { get; set; }  // IDLE | RUNNING | COMPLETED | FAILED
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 在 Executor 中使用共享状态
[SendsMessage(typeof(OrderDetails))]
internal sealed partial class ValidateOrderExecutor : Executor("ValidateOrder")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        OrderRequest request,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 验证订单
        var orderDetails = ValidateOrder(request);
        
        // 写入共享状态（税率）
        await context.SetStateAsync("taxRate", 0.08m, ct);
        await context.SetStateAsync("region", request.Region, ct);
        
        // 发送消息到下游
        await context.SendMessageAsync(orderDetails, ct);
    }
}

[YieldsOutput(typeof(Invoice))]
internal sealed partial class ProcessPaymentExecutor : Executor("ProcessPayment")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        OrderDetails order,
        IWorkflowContext context,
        CancellationToken ct = default)
    {
        // 读取共享状态
        var taxRate = await context.GetStateAsync<decimal>("taxRate", ct);
        var region = await context.GetStateAsync<string>("region", ct);
        
        // 使用共享状态计算
        var totalAmount = order.Amount * (1 + taxRate);
        
        // 处理支付并输出
        var invoice = ProcessPayment(order, totalAmount, region);
        await context.YieldOutputAsync(invoice, ct);
    }
}
```

**为什么重要**:
- ✅ 避免在消息中传递大量上下文
- ✅ 保持消息流清晰（只传递核心业务数据）
- ✅ 跨 Executor 共享配置和元数据
- ✅ 持久化后可恢复 Workflow 状态

#### 3. WorkflowCheckpoint（Workflow 检查点）

**用途**: 保存 Workflow 完整状态，支持暂停/恢复

```csharp
// Checkpoint 结构（基于 MAF 1.1.0）
public class WorkflowCheckpointData
{
    public string CheckpointId { get; set; }
    public string WorkflowId { get; set; }
    public string SessionId { get; set; }
    public string SchemaVersion { get; set; } = "1.0.0";
    
    // 核心状态
    public Dictionary<string, object> SharedState { get; set; }
    public List<AgentSessionState> AgentSessions { get; set; }  // 所有 Agent 的会话
    public string CurrentExecutorId { get; set; }
    public WorkflowRunState Status { get; set; }
    
    // 元数据
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; }
    public string? ResumeToken { get; set; }  // 恢复令牌
}
```

**保存 Checkpoint**:
```csharp
// 在 Workflow 执行中保存 Checkpoint
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
        agentSessions.Add(session);
    }
    
    // 3. 构建 Checkpoint
    var checkpoint = new WorkflowCheckpointData
    {
        CheckpointId = Guid.NewGuid().ToString(),
        WorkflowId = workflow.Id,
        SessionId = sessionId,
        SharedState = workflowState.SharedState,
        AgentSessions = agentSessions,
        CurrentExecutorId = workflowState.CurrentExecutorId,
        Status = workflowState.Status,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "system"
    };
    
    // 4. 序列化并保存到数据库
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

**恢复 Checkpoint**:
```csharp
// 从 Checkpoint 恢复 Workflow
public async Task<Workflow> RestoreFromCheckpointAsync(
    string checkpointId,
    CancellationToken ct)
{
    // 1. 从数据库加载 Checkpoint
    var entity = await _db.WorkflowCheckpoints
        .FirstOrDefaultAsync(c => c.CheckpointId == checkpointId, ct);
    
    if (entity == null)
        throw new InvalidOperationException($"Checkpoint {checkpointId} not found");
    
    var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpointData>(entity.DataJson);
    
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
        await agent.RestoreSessionAsync(agentSession, ct);
    }
    
    // 5. 设置当前执行位置
    await workflow.SetCurrentExecutorAsync(checkpoint.CurrentExecutorId, ct);
    
    return workflow;
}
```

> **版本**: MAF 1.1.0 | **更新日期**: 2026-04-20

## 📋 目录

1. [核心持久化对象](#核心持久化对象)
2. [Agent 上下文管理](#agent-上下文管理)
3. [Workflow State 管理](#workflow-state-管理)
4. [Checkpoint 最佳实践](#checkpoint-最佳实践)
5. [数据库设计](#数据库设计)
6. [完整实现示例](#完整实现示例)

---

## 核心持久化对象

### 必须持久化的数据（6 大类）

#### 1. AgentSession（Agent 会话状态）

**用途**: 保存 Agent 的对话历史和上下文

```csharp
// AgentSession 结构
public class AgentSessionState
{
    public string SessionId { get; set; }              // 会话 ID
    public string? ServiceSessionId { get; set; }      // 服务端会话 ID（如果使用远程 Agent）
    public Dictionary<string, object> State { get; set; } // 自定义状态
    public List<ConversationMessage> History { get; set; } // 对话历史
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ConversationMessage 结构（完整对话记录）
public class ConversationMessage
{
    public string Type { get; set; }  // "request" | "response"
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<MessageContent> Messages { get; set; }
    public TokenUsage? Usage { get; set; }  // Token 使用统计
}

// MessageContent 结构
public class MessageContent
{
    public string Role { get; set; }  // "user" | "assistant" | "tool" | "system"
    public string? AuthorName { get; set; }
    public List<Content> Contents { get; set; }  // text | functionCall | functionResult
}
```

**为什么重要**:
- 多轮对话上下文保持
- Token 使用追踪
- 调试和审计
- 成本分析

