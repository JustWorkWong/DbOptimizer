# Microsoft Agent Framework (MAF) 完整指南

> **版本**: MAF 1.1.0 | **.NET**: 10 | **更新日期**: 2026-04-20
> **说明**: 本文档同时包含 MAF 概念示例和项目落地方向。当前 `DbOptimizer` 已完成 MAF 1.1.0 基线升级与最小原生互操作测试，但原生 checkpoint / review request-response / resume 主链路仍在重构中，不能把文中的目标态示例直接视为“仓库当前已全部实现”。

## 📋 目录

1. [核心概念](#核心概念)
2. [架构模型](#架构模型)
3. [快速开始](#快速开始)
4. [Workflow 设计模式](#workflow-设计模式)
5. [Agent 设计](#agent-设计)
6. [Executor 设计](#executor-设计)
7. [最佳实践](#最佳实践)
8. [DbOptimizer 项目规范](#dboptimizer-项目规范)

---

## 核心概念

### 什么是 MAF？

Microsoft Agent Framework 是微软官方的 **多语言 AI Agent 编排框架**（支持 .NET 和 Python），用于构建从简单聊天机器人到复杂多 Agent 工作流的 AI 应用。

### 三大核心组件

```
┌─────────────────────────────────────────────────┐
│                   Workflow                      │
│  (图结构编排，定义数据流和执行顺序)                │
└─────────────────────────────────────────────────┘
           │                    │
           ▼                    ▼
┌──────────────────┐    ┌──────────────────┐
│     Executor     │    │      Agent       │
│  (执行单元/节点)   │    │  (AI 智能体)      │
│  处理消息和逻辑    │    │  调用 LLM 推理    │
└──────────────────┘    └──────────────────┘
```

### 关键特性

- **强类型**: 编译时验证消息流，避免运行时错误
- **图结构**: 直观建模复杂流程（顺序、并发、条件分支）
- **Checkpoint**: 保存/恢复工作流状态，支持长时运行
- **流式输出**: 实时事件流，支持 SSE 推送
- **多模式**: Sequential（顺序）、Concurrent（并发）、Handoff（动态路由）

---

## 架构模型

### 数据流模型

```csharp
// 消息在 Workflow 中的流转
User Input (string)
    ↓
[StartExecutor] → SendMessage(ChatMessage)
    ↓
[Agent1] → 处理并返回 List<ChatMessage>
    ↓
[Agent2] → 处理并 YieldOutput(string)
    ↓
Final Output (string)
```

### 核心类型

| 类型 | 作用 | 示例 |
|------|------|------|
| `AIAgent` | AI 智能体，封装 LLM 调用 | `chatClient.CreateAIAgent(name, instructions)` |
| `Executor` | 执行单元，处理消息和业务逻辑 | `[SendsMessage(typeof(ChatMessage))]` |
| `WorkflowBuilder` | 构建工作流图结构 | `.AddEdge(agent1, agent2)` |
| `IWorkflowContext` | 上下文，用于发送消息/输出 | `await ctx.SendMessageAsync(msg)` |
| `WorkflowEvent` | 工作流事件（流式输出） | `WorkflowOutputEvent`, `WorkflowErrorEvent` |

---

## 快速开始

### 1. 安装依赖

```bash
dotnet add package Microsoft.Agents.AI --version 1.1.0
dotnet add package Microsoft.Extensions.AI
```

### 2. 最简单的 Agent

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

// 创建 ChatClient
var openAIClient = new OpenAIClient(apiKey);
var chatClient = openAIClient.GetChatClient("gpt-4o").AsIChatClient();

// 创建 Agent
AIAgent agent = chatClient.CreateAIAgent(
    name: "Assistant",
    instructions: "You are a helpful assistant."
);

// 运行 Agent
var response = await agent.RunAsync("Hello!");
Console.WriteLine(response.Text);
```

### 3. 最简单的 Workflow（顺序执行）

```csharp
// 创建两个 Agent
AIAgent writerAgent = chatClient.CreateAIAgent(
    name: "Writer",
    instructions: "You write draft content."
);

AIAgent reviewerAgent = chatClient.CreateAIAgent(
    name: "Reviewer",
    instructions: "You review and improve content."
);

// 构建 Workflow
var workflow = new WorkflowBuilder(writerAgent)
    .AddEdge(writerAgent, reviewerAgent)  // Writer → Reviewer
    .Build();

// 执行 Workflow（流式）
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
    workflow,
    input: "Write a blog post about AI"
);

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent output)
    {
        Console.WriteLine($"Result: {output.Data}");
    }
}
```

---

## Workflow 设计模式

### 模式 1: Sequential（顺序执行）

**适用场景**: 步骤依赖，前一步输出是后一步输入

```csharp
// 示例：销售报价流程（Sales → Price → Quote）
AIAgent salesAgent = chatClient.CreateAIAgent(
    name: "Sales",
    instructions: "Analyze customer requirements"
);

AIAgent priceAgent = chatClient.CreateAIAgent(
    name: "Pricing",
    instructions: "Calculate pricing based on requirements"
);

AIAgent quoteAgent = chatClient.CreateAIAgent(
    name: "Quote",
    instructions: "Generate final quote document"
);

var workflow = new WorkflowBuilder(salesAgent)
    .AddEdge(salesAgent, priceAgent)
    .AddEdge(priceAgent, quoteAgent)
    .Build();
```

### 模式 2: Concurrent（并发执行 Fan-Out/Fan-In）

**适用场景**: 多个独立任务并行执行，最后汇总结果

```csharp
// 自定义 Executor：广播消息到所有 Agent
[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
internal sealed partial class ConcurrentStartExecutor() 
    : Executor("ConcurrentStartExecutor")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string message, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, message), 
            cancellationToken: ct);
        await context.SendMessageAsync(
            new TurnToken(emitEvents: true), 
            cancellationToken: ct);
    }
}

// 自定义 Executor：聚合结果
[YieldsOutput(typeof(string))]
internal sealed partial class ConcurrentAggregationExecutor() 
    : Executor<List<ChatMessage>>("ConcurrentAggregationExecutor")
{
    private readonly List<ChatMessage> _messages = [];

    public override async ValueTask HandleAsync(
        List<ChatMessage> message, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        _messages.AddRange(message);
        if (_messages.Count >= 2)  // 等待两个 Agent 都完成
        {
            var result = string.Join("\n", 
                _messages.Select(m => $"{m.AuthorName}: {m.Text}"));
            await context.YieldOutputAsync(result, ct);
        }
    }
}

// 构建并发 Workflow
var startExecutor = new ConcurrentStartExecutor();
var aggregationExecutor = new ConcurrentAggregationExecutor();

AIAgent researcherAgent = chatClient.CreateAIAgent(
    name: "Researcher",
    instructions: "Research travel destinations"
);

AIAgent plannerAgent = chatClient.CreateAIAgent(
    name: "Planner",
    instructions: "Create travel itineraries"
);

var workflow = new WorkflowBuilder(startExecutor)
    .AddFanOutEdge(startExecutor, [researcherAgent, plannerAgent])
    .AddFanInBarrierEdge([researcherAgent, plannerAgent], aggregationExecutor)
    .WithOutputFrom(aggregationExecutor)
    .Build();
```

### 模式 3: Conditional Routing（条件路由）

**适用场景**: 根据消息内容动态决定下一步

```csharp
[SendsMessage(typeof(string))]
internal sealed partial class ReviewerExecutor() : Executor("Reviewer")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string draft, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        // 根据内容决定路由
        string decision = draft.Contains("approved") ? "approve" : "revise";
        await context.SendMessageAsync($"{decision}:{draft}", ct);
    }
}

[YieldsOutput(typeof(string))]
internal sealed partial class EditorExecutor() : Executor("Editor")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        string msg, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        if (msg.StartsWith("approve:"))
        {
            await context.YieldOutputAsync(msg.Split(":", 2)[1], ct);
        }
        else
        {
            await context.YieldOutputAsync("Needs revision", ct);
        }
    }
}
```

---

## Agent 设计

### Agent 的本质

Agent = **LLM + Instructions + Tools**

```csharp
// 基础 Agent
AIAgent agent = chatClient.CreateAIAgent(
    name: "SqlAnalyzer",
    instructions: """
        You are a SQL optimization expert.
        Analyze SQL queries and suggest improvements.
        Focus on index usage and query performance.
        """
);

// 带 Tools 的 Agent
AIAgent agentWithTools = chatClient.CreateAIAgent(
    name: "DatabaseAgent",
    instructions: "You can query database metadata",
    tools: [
        AIFunctionFactory.Create(GetTableIndexes),
        AIFunctionFactory.Create(GetTableStatistics)
    ]
);

// Tool 定义（纯函数）
[Description("Get indexes for a table")]
static async Task<string> GetTableIndexes(
    [Description("Table name")] string tableName)
{
    // 实现逻辑
    return "index1, index2";
}
```

### Agent 最佳实践

1. **单一职责**: 每个 Agent 只做一件事
2. **清晰的 Instructions**: 明确角色、任务、输出格式
3. **Tools 纯函数**: 无副作用，易测试
4. **命名规范**: `{功能}Agent`（如 `SqlParserAgent`）

---

## Executor 设计

### Executor 的作用

Executor 是 Workflow 中的**执行单元/节点**，用于：
- 处理业务逻辑（非 AI 推理）
- 转换消息格式
- 条件路由
- 聚合结果

### Executor 基础结构

```csharp
// 1. 声明发送的消息类型
[SendsMessage(typeof(ChatMessage))]
[SendsMessage(typeof(TurnToken))]
internal sealed partial class MyExecutor() : Executor("MyExecutor")
{
    // 2. 定义消息处理器
    [MessageHandler]
    public async ValueTask HandleAsync(
        string input,  // 输入类型
        IWorkflowContext context,  // 上下文
        CancellationToken ct = default)
    {
        // 3. 业务逻辑
        var message = new ChatMessage(ChatRole.User, input);
        
        // 4. 发送消息到下游
        await context.SendMessageAsync(message, cancellationToken: ct);
        await context.SendMessageAsync(new TurnToken(), cancellationToken: ct);
    }
}
```

### Executor 输出结果

```csharp
// 使用 YieldsOutput 声明最终输出
[YieldsOutput(typeof(string))]
internal sealed partial class FinalExecutor() : Executor("FinalExecutor")
{
    [MessageHandler]
    public async ValueTask HandleAsync(
        List<ChatMessage> messages, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        var result = string.Join("\n", messages.Select(m => m.Text));
        
        // 输出最终结果
        await context.YieldOutputAsync(result, ct);
    }
}
```

### Executor 状态管理

```csharp
// 带状态的 Executor（用于聚合）
[YieldsOutput(typeof(string))]
internal sealed partial class AggregatorExecutor() 
    : Executor<List<ChatMessage>>("Aggregator")
{
    private readonly List<ChatMessage> _collected = [];

    public override async ValueTask HandleAsync(
        List<ChatMessage> messages, 
        IWorkflowContext context, 
        CancellationToken ct = default)
    {
        _collected.AddRange(messages);
        
        // 等待所有输入到齐
        if (_collected.Count >= 3)
        {
            var summary = Summarize(_collected);
            await context.YieldOutputAsync(summary, ct);
        }
    }
    
    private string Summarize(List<ChatMessage> messages) => 
        string.Join("\n", messages.Select(m => $"- {m.Text}"));
}
```

---

## 最佳实践

### 1. Workflow 设计原则

✅ **DO**:
- 使用 `WorkflowBuilder` 构建图结构
- 明确定义输入/输出类型（强类型）
- 使用 `StreamingRun` 实现实时反馈
- 保存 Checkpoint 用于长时运行任务

❌ **DON'T**:
- 不要在 Workflow 中直接调用 LLM（用 Agent）
- 不要在 Executor 中写复杂业务逻辑（拆分）
- 不要忽略异常处理

### 2. Agent 设计原则

✅ **DO**:
- Instructions 要具体、可执行
- Tools 要有清晰的 `[Description]`
- 使用 `AIFunctionFactory.Create` 注册 Tools
- 记录 Agent 执行日志（调试、成本追踪）

❌ **DON'T**:
- 不要让一个 Agent 做多件事
- 不要在 Instructions 中写代码
- 不要忘记处理 Tool 调用失败

### 3. 错误处理

```csharp
await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
    workflow, 
    input: userInput
);

await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case WorkflowOutputEvent output:
            Console.WriteLine($"✅ Result: {output.Data}");
            break;
            
        case WorkflowErrorEvent error:
            Console.WriteLine($"❌ Error: {error.Message}");
            // 记录到数据库
            await LogErrorAsync(error);
            break;
            
        case WorkflowStepEvent step:
            Console.WriteLine($"🔄 Step: {step.StepName}");
            break;
    }
}
```

### 4. Checkpoint 持久化

```csharp
// 保存 Checkpoint
var checkpointData = await workflow.SaveCheckpointAsync();
await SaveToDatabase(checkpointData);

// 恢复 Checkpoint
var savedData = await LoadFromDatabase(workflowId);
var workflow = await Workflow.RestoreFromCheckpointAsync(savedData);
await workflow.ResumeAsync();
```

### 5. 性能优化

- **并发执行**: 使用 Fan-Out/Fan-In 模式
- **流式输出**: 使用 `StreamingRun` 避免阻塞
- **缓存 Agent**: 复用 `AIAgent` 实例
- **限流**: 控制并发 Agent 数量

---

## DbOptimizer 项目规范

### 项目中的 MAF 使用

#### 1. 目录结构

```
src/
├── DbOptimizer.Infrastructure/
│   ├── Maf/
│   │   ├── Runtime/
│   │   │   ├── IMafWorkflowRuntime.cs       # Workflow 运行时接口
│   │   │   ├── MafWorkflowRuntime.cs        # Workflow 运行时实现
│   │   │   ├── MafWorkflowFactory.cs        # Workflow 工厂
│   │   │   ├── MafSqlWorkflowStarter.cs     # SQL 优化 Workflow 启动器
│   │   │   └── MafConfigWorkflowStarter.cs  # 配置优化 Workflow 启动器
│   │   └── Agents/
│   │       ├── SqlParserAgent.cs            # SQL 解析 Agent
│   │       ├── IndexAnalyzerAgent.cs        # 索引分析 Agent
│   │       └── ConfigReviewerAgent.cs       # 配置审核 Agent
│   └── Workflows/
│       ├── SqlOptimization/                 # SQL 优化工作流
│       └── ConfigOptimization/              # 配置优化工作流
```

#### 2. Workflow 命名规范

- **Workflow**: `{业务}Workflow`（如 `SqlOptimizationWorkflow`）
- **Agent**: `{功能}Agent`（如 `SqlParserAgent`）
- **Executor**: `{功能}Executor`（如 `IndexAnalysisExecutor`）
- **Starter**: `Maf{业务}WorkflowStarter`（如 `MafSqlWorkflowStarter`）

#### 3. Prompt 版本管理

```csharp
// ❌ 错误：硬编码 Prompt
var agent = chatClient.CreateAIAgent(
    name: "SqlParser",
    instructions: "Parse SQL queries..."  // 难以追踪和版本管理
);

// ✅ 正确：从数据库加载 Prompt
var promptVersion = await _promptRepository.GetLatestAsync("SqlParser");
var agent = chatClient.CreateAIAgent(
    name: "SqlParser",
    instructions: promptVersion.Content
);

// 记录使用的 Prompt 版本
await _agentExecutionRepository.CreateAsync(new AgentExecution
{
    AgentName = "SqlParser",
    PromptVersionId = promptVersion.Id,
    // ...
});
```

#### 4. 数据持久化规范

**必须记录**:
- ✅ Agent 执行记录（`AgentExecution`）
- ✅ Tool 调用记录（`ToolCall`）
- ✅ 决策记录（`DecisionRecord`）
- ✅ Agent 消息（`AgentMessage`）
- ✅ 错误记录（`WorkflowError`）
- ✅ Workflow Checkpoint（`WorkflowCheckpoint`）

**目的**:
- 调试：追踪 Agent 行为
- 优化：分析 Prompt 效果
- 成本：追踪 Token 使用
- 审计：合规要求

#### 5. 完整示例：SQL 优化 Workflow

```csharp
public class MafSqlWorkflowStarter : IMafWorkflowStarter
{
    private readonly IChatClient _chatClient;
    private readonly IPromptVersionRepository _promptRepo;
    private readonly IAgentExecutionRepository _executionRepo;

    public async Task<Workflow> CreateWorkflowAsync(
        WorkflowRequest request, 
        CancellationToken ct)
    {
        // 1. 加载 Prompt 版本
        var parserPrompt = await _promptRepo.GetLatestAsync("SqlParser", ct);
        var analyzerPrompt = await _promptRepo.GetLatestAsync("IndexAnalyzer", ct);
        var reviewerPrompt = await _promptRepo.GetLatestAsync("SqlReviewer", ct);

        // 2. 创建 Agents
        AIAgent parserAgent = _chatClient.CreateAIAgent(
            name: "SqlParser",
            instructions: parserPrompt.Content,
            tools: [
                AIFunctionFactory.Create(ParseSqlQuery),
                AIFunctionFactory.Create(ExtractTables)
            ]
        );

        AIAgent analyzerAgent = _chatClient.CreateAIAgent(
            name: "IndexAnalyzer",
            instructions: analyzerPrompt.Content,
            tools: [
                AIFunctionFactory.Create(GetTableIndexes),
                AIFunctionFactory.Create(GetTableStatistics)
            ]
        );

        AIAgent reviewerAgent = _chatClient.CreateAIAgent(
            name: "SqlReviewer",
            instructions: reviewerPrompt.Content
        );

        // 3. 构建 Workflow（顺序执行）
        var workflow = new WorkflowBuilder(parserAgent)
            .AddEdge(parserAgent, analyzerAgent)
            .AddEdge(analyzerAgent, reviewerAgent)
            .Build();

        return workflow;
    }

    // Tool 定义
    [Description("Parse SQL query and extract structure")]
    private async Task<string> ParseSqlQuery(
        [Description("SQL query to parse")] string sql)
    {
        // 实现 SQL 解析逻辑
        return "parsed_structure";
    }

    [Description("Get indexes for a table")]
    private async Task<string> GetTableIndexes(
        [Description("Table name")] string tableName)
    {
        // 通过 MCP 调用数据库
        var result = await _mcpClient.CallToolAsync(
            "get_table_indexes",
            new { table_name = tableName }
        );
        return result;
    }
}
```

#### 6. Workflow 执行和事件处理

```csharp
public class MafWorkflowRuntime : IMafWorkflowRuntime
{
    public async Task<WorkflowResult> ExecuteAsync(
        Workflow workflow,
        WorkflowRequest request,
        CancellationToken ct)
    {
        var result = new WorkflowResult { SessionId = request.SessionId };

        try
        {
            // 执行 Workflow（流式）
            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                workflow,
                input: request.Input,
                cancellationToken: ct
            );

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                switch (evt)
                {
                    case WorkflowOutputEvent output:
                        result.Output = output.Data?.ToString();
                        result.Status = WorkflowStatus.Completed;
                        
                        // 发送 SSE 事件
                        await _sseService.SendEventAsync(request.SessionId, new
                        {
                            Type = "workflow_completed",
                            Data = output.Data
                        });
                        break;

                    case WorkflowErrorEvent error:
                        result.Status = WorkflowStatus.Failed;
                        result.Error = error.Message;
                        
                        // 持久化错误
                        await _errorRepository.CreateAsync(new WorkflowError
                        {
                            SessionId = request.SessionId,
                            Message = error.Message,
                            StackTrace = error.Exception?.StackTrace
                        });
                        break;

                    case WorkflowStepEvent step:
                        // 发送进度事件
                        await _sseService.SendEventAsync(request.SessionId, new
                        {
                            Type = "workflow_step",
                            Step = step.StepName
                        });
                        break;
                }
            }

            // 保存 Checkpoint
            if (request.EnableCheckpoint)
            {
                var checkpointData = await workflow.SaveCheckpointAsync();
                await _checkpointRepository.SaveAsync(
                    request.SessionId, 
                    checkpointData
                );
            }
        }
        catch (Exception ex)
        {
            result.Status = WorkflowStatus.Failed;
            result.Error = ex.Message;
            _logger.LogError(ex, "Workflow execution failed");
        }

        return result;
    }
}
```

#### 7. 前端集成（SSE）

```typescript
// Vue 3 组件
const connectWorkflowStream = (sessionId: string) => {
  const eventSource = new EventSource(
    `/api/workflows/${sessionId}/events`
  );

  eventSource.onmessage = (event) => {
    const data = JSON.parse(event.data);
    
    switch (data.type) {
      case 'workflow_step':
        workflowStore.updateStep(data.step);
        break;
      case 'workflow_completed':
        workflowStore.setResult(data.data);
        eventSource.close();
        break;
      case 'workflow_error':
        workflowStore.setError(data.message);
        eventSource.close();
        break;
    }
  };

  eventSource.onerror = () => {
    console.error('SSE connection failed');
    eventSource.close();
  };
};
```

---

## 常见问题

### Q1: Agent 和 Executor 的区别？

- **Agent**: 调用 LLM 进行 AI 推理，适合需要智能决策的场景
- **Executor**: 执行确定性逻辑，适合数据转换、路由、聚合

### Q2: 什么时候用 Sequential vs Concurrent？

- **Sequential**: 步骤有依赖关系（A 的输出是 B 的输入）
- **Concurrent**: 步骤独立，可以并行执行（提高性能）

### Q3: 如何调试 Workflow？

1. 使用 `WorkflowStepEvent` 追踪执行步骤
2. 记录所有 Agent 消息到数据库
3. 使用 Checkpoint 重现问题
4. 查看 `WorkflowErrorEvent` 的详细错误信息

### Q4: Prompt 如何版本管理？

```sql
CREATE TABLE prompt_versions (
    id UUID PRIMARY KEY,
    agent_name VARCHAR(100) NOT NULL,
    version INT NOT NULL,
    content TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT NOW(),
    is_active BOOLEAN DEFAULT TRUE
);
```

每次修改 Prompt 创建新版本，保留历史记录用于对比和回滚。

---

## 参考资源

- **官方文档**: https://learn.microsoft.com/en-us/agent-framework
- **GitHub**: https://github.com/microsoft/agent-framework
- **示例代码**: https://github.com/microsoft/agent-framework-samples
- **NuGet**: `Microsoft.Agents.AI` (1.1.0)

---

## 总结

MAF 的核心理念：

1. **图结构编排**: 用 `WorkflowBuilder` 构建清晰的数据流
2. **强类型安全**: 编译时验证，避免运行时错误
3. **Agent 单一职责**: 每个 Agent 只做一件事
4. **Executor 处理逻辑**: 非 AI 推理用 Executor
5. **全量持久化**: 记录所有执行细节，便于调试和优化

**避免造轮子**：
- ✅ 使用 `WorkflowBuilder` 而不是自己实现图结构
- ✅ 使用 `AIAgent` 而不是直接调用 LLM API
- ✅ 使用 `StreamingRun` 而不是自己实现事件流
- ✅ 使用 `Checkpoint` 而不是自己实现状态保存

遵循本文档并完成 native runtime refactor 后，DbOptimizer 才会更接近本文描述的 MAF 最佳实践目标态。
