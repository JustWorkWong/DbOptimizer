# DbOptimizer 设计文档索引

**项目名称**：DbOptimizer - AI 驱动的数据库性能优化平台  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 文档说明

本文档是 DbOptimizer 设计文档的索引页面。详细设计内容已拆分为多个专项文档，便于维护和查阅。

---

## 设计文档目录

### 核心设计

1. **[系统架构设计](./ARCHITECTURE.md)**
   - 整体架构图
   - 分层设计
   - 技术选型
   - 模块依赖关系

2. **[MAF Workflow 详细设计](./WORKFLOW_DESIGN.md)**
   - Workflow 概述
   - SQL 分析 Workflow
   - 数据库配置优化 Workflow
   - Executor 接口设计
   - 数据传递与上下文
   - 错误处理与重试

3. **[数据模型设计](./DATA_MODEL.md)**
   - 数据库选型
   - 表结构设计
   - 实体关系图
   - JSONB 字段设计
   - 索引策略

4. **[MCP 集成方案](./MCP_INTEGRATION.md)**
   - MCP 概述
   - MCP 客户端设计
   - 超时处理
   - Fallback 策略
   - 错误处理
   - 连接池管理

### 安全与部署

5. **[安全设计](./SECURITY_DESIGN.md)**
   - 安全威胁分析
   - 认证与授权
   - 数据加密
   - 审计日志
   - 安全最佳实践

6. **[部署架构](./DEPLOYMENT.md)**
   - Aspire 编排
   - Docker 部署
   - 生产环境配置
   - 运维指南

### 前端设计

7. **[前端架构设计](./FRONTEND_ARCHITECTURE.md)**
   - 全局 UI 框架
   - 状态管理（Pinia）
   - 路由设计
   - 公共组件
   - SSE 集成

8. **[页面详细设计](./PAGE_DESIGN.md)**
   - 总览页面
   - SQL 调优页面
   - 实例调优页面
   - 审核工作台页面
   - 历史任务页面
   - 运行回放页面

9. **[组件规范](./COMPONENT_SPEC.md)**
   - SSE 连接器
   - Monaco 编辑器
   - Workflow 进度条
   - 建议卡片
   - 证据查看器
   - 日志查看器

### 参考文档

10. **[API 接口规范](./API_SPEC.md)**
    - 通用规范
    - Workflow API
    - Review API
    - Dashboard API
    - History API
    - SSE 事件规范

11. **[术语表](./GLOSSARY.md)**
    - 项目术语定义
    - 技术术语解释

12. **[P0/P1 优先级设计](./P0_P1_DESIGN.md)**
    - P0 必须实现的功能
    - P1 应该实现的功能
    - 实现细节

---

## 文档关系图

```
REQUIREMENTS.md (需求文档)
    ↓
DESIGN.md (本文档 - 索引)
    ├── ARCHITECTURE.md (系统架构)
    ├── WORKFLOW_DESIGN.md (Workflow 设计)
    ├── DATA_MODEL.md (数据模型)
    ├── MCP_INTEGRATION.md (MCP 集成)
    ├── SECURITY_DESIGN.md (安全设计)
    ├── DEPLOYMENT.md (部署架构)
    ├── FRONTEND_ARCHITECTURE.md (前端架构)
    ├── PAGE_DESIGN.md (页面设计)
    ├── COMPONENT_SPEC.md (组件规范)
    ├── API_SPEC.md (API 规范)
    ├── GLOSSARY.md (术语表)
    └── P0_P1_DESIGN.md (优先级设计)
```

---

## 快速导航

**开始开发前必读**：
1. [REQUIREMENTS.md](./REQUIREMENTS.md) - 了解项目需求
2. [ARCHITECTURE.md](./ARCHITECTURE.md) - 理解系统架构
3. [WORKFLOW_DESIGN.md](./WORKFLOW_DESIGN.md) - 掌握 Workflow 设计

**后端开发**：
- [DATA_MODEL.md](./DATA_MODEL.md) - 数据库设计
- [MCP_INTEGRATION.md](./MCP_INTEGRATION.md) - MCP 集成
- [API_SPEC.md](./API_SPEC.md) - API 接口

**前端开发**：
- [FRONTEND_ARCHITECTURE.md](./FRONTEND_ARCHITECTURE.md) - 前端架构
- [PAGE_DESIGN.md](./PAGE_DESIGN.md) - 页面设计
- [COMPONENT_SPEC.md](./COMPONENT_SPEC.md) - 组件规范

**运维部署**：
- [SECURITY_DESIGN.md](./SECURITY_DESIGN.md) - 安全配置
- [DEPLOYMENT.md](./DEPLOYMENT.md) - 部署指南

---

## 文档维护

**更新原则**：
- 架构变更必须同步更新相关文档
- 新增功能需更新对应的设计文档
- API 变更需同步更新 API_SPEC.md
- 新增术语需添加到 GLOSSARY.md

**文档版本**：
- 当前版本：v1.0
- 最后更新：2026-04-15

---

## 1. 系统架构

### 1.1 整体架构图

\`\`\`
┌─────────────────────────────────────────────────────────────────────┐
│                      Vue 3 前端 (Element Plus)                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │ SQL Analysis │  │ DB Config    │  │ Review       │             │
│  │ Page         │  │ Optimization │  │ Workspace    │             │
│  └──────────────┘  └──────────────┘  └──────────────┘             │
│                            │                                         │
│                    HTTP REST API + SSE                              │
└────────────────────────────┼────────────────────────────────────────┘
                             │
┌────────────────────────────┼────────────────────────────────────────┐
│                  ASP.NET Core Web API                               │
│  ┌──────────────────────────┴─────────────────────────────────┐    │
│  │              Controllers + SSE Endpoints                     │    │
│  └──────────────────────────┬─────────────────────────────────┘    │
│                             │                                        │
│  ┌──────────────────────────┴─────────────────────────────────┐    │
│  │           MAF Workflow Orchestrator                         │    │
│  │  ┌────────────────────────────────────────────────────┐    │    │
│  │  │  SQL Analysis Workflow                             │    │    │
│  │  │  ┌──────────┐  ┌──────────┐  ┌──────────┐        │    │    │
│  │  │  │ SQL      │→ │Execution │→ │  Index   │        │    │    │
│  │  │  │ Parser   │  │  Plan    │  │ Advisor  │        │    │    │
│  │  │  │ Executor │  │ Executor │  │ Executor │        │    │    │
│  │  │  └──────────┘  └──────────┘  └──────────┘        │    │    │
│  │  │       ↓             ↓             ↓               │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Coordinator Executor             │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  │       ↓                                           │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Human Review Executor            │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  │       ↓ (if rejected)                            │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Regeneration Executor            │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  └────────────────────────────────────────────────────┘    │    │
│  └─────────────────────────┬───────────────────────────────────┘    │
│                            │                                         │
│  ┌─────────────────────────┴───────────────────────────────────┐    │
│  │              Shared Services                                 │    │
│  │  • WorkflowSessionManager                                   │    │
│  │  • CheckpointStorage                                        │    │
│  │  • SSEPublisher                                             │    │
│  │  • McpClientPool                                            │    │
│  └──────────────────────────┬───────────────────────────────────┘    │
└─────────────────────────────┼────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
┌───────▼────────┐ ┌─────────▼──────┐ ┌───────────▼────────┐
│   PostgreSQL   │ │     Redis      │ │  MySQL/PostgreSQL  │
│ (主存储)       │ │ (缓存+Session) │ │  (目标数据库)      │
└────────────────┘ └────────────────┘ └────────────────────┘
\`\`\`

### 1.2 架构分层

**表现层（Presentation Layer）**：
- Vue 3 前端
- Element Plus UI 组件
- SSE 客户端

**API 层（API Layer）**：
- ASP.NET Core Controllers
- SSE Endpoints
- 输入验证
- 异常处理

**业务逻辑层（Business Logic Layer）**：
- MAF Workflows
- MAF Executors
- 业务服务

**基础设施层（Infrastructure Layer）**：
- EF Core 数据访问
- MCP Client
- 缓存服务
- 日志服务

---

## 2. 技术选型

### 2.1 SSE vs SignalR

**选择 SSE 的原因**：
- 单向推送场景（服务端 → 前端）
- 自动重连机制
- 基于 HTTP，无需额外协议
- Vue 3 集成简单（EventSource API）

**实现方案**：
\`\`\`csharp
// ASP.NET Core SSE 端点
[HttpGet("api/workflows/{workflowId}/events")]
public async Task StreamEvents(Guid workflowId, CancellationToken ct)
{
    Response.Headers.Add("Content-Type", "text/event-stream");
    Response.Headers.Add("Cache-Control", "no-cache");
    
    await foreach (var evt in _workflowService.SubscribeAsync(workflowId, ct))
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(evt)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
\`\`\`

\`\`\`typescript
// Vue 3 前端订阅
const eventSource = new EventSource(\`/api/workflows/\${workflowId}/events\`);
eventSource.onmessage = (event) => {
  const data = JSON.parse(event.data);
  updateWorkflowState(data);
};
\`\`\`

### 2.2 MAF Workflow vs 直接调用 Agent

**选择 MAF Workflow 的原因**：
- 自动 Checkpoint 管理
- 自动错误恢复
- 内置 Human-in-the-loop 支持
- 并行执行支持
- 可观测性

---

## 3. MAF Workflow 设计

### 3.1 SQL 分析 Workflow

#### 3.1.1 Workflow 流程图

\`\`\`
[Start]
   ↓
[SqlParserExecutor]
   ↓
[ExecutionPlanExecutor]
   ↓
[IndexAdvisorExecutor]
   ↓
[CoordinatorExecutor]
   ↓
[HumanReviewExecutor]
   ↓
[Decision Point]
   ├─ Accept → [End]
   ├─ Reject → [End]
   └─ Adjust → [RegenerationExecutor] → [HumanReviewExecutor] (循环)
\`\`\`

#### 3.1.2 Executor 实现示例

\`\`\`csharp
public class SqlParserExecutor : Executor
{
    private const string StateKey = "SqlParserState";
    
    [MessageHandler]
    private async ValueTask HandleAsync(
        SqlAnalysisRequest request, 
        IWorkflowContext context,
        CancellationToken cancellation)
    {
        var sessionId = context.GetState<Guid>("session_id");
        
        // 1. 创建执行记录
        var execution = new ExecutorExecution
        {
            WorkflowSessionId = sessionId,
            ExecutorName = "SqlParserExecutor",
            Status = "running"
        };
        await _repository.SaveExecutionAsync(execution);
        
        // 2. 调用 Agent
        var agent = await _agentProvider.GetAgentAsync("SqlParserAgent");
        var result = await agent.RunAsync(prompt);
        
        // 3. 保存到 context
        context.SetState("parsed_sql", parsedSql);
        
        // 4. 发送消息给下一个 Executor
        await context.SendMessageAsync(new ExecutionPlanRequest
        {
            Sql = sql,
            ParsedSql = parsedSql
        });
    }
    
    // Checkpoint 保存
    protected override ValueTask OnCheckpointingAsync(
        IWorkflowContext context, 
        CancellationToken cancellation = default)
    {
        return context.QueueStateUpdateAsync(StateKey, _state);
    }
}
\`\`\`

### 3.2 数据库配置优化 Workflow

\`\`\`
[Start]
   ↓
[ConfigCollectorExecutor]
   ↓
[ServerResourceExecutor]
   ↓
[DatabaseStatsExecutor]
   ↓
[ConfigurationAdvisorExecutor]
   ↓
[RiskAssessorExecutor]
   ↓
[HumanReviewExecutor]
   ↓
[Decision Point]
\`\`\`

---

## 4. 数据模型设计

### 4.1 核心实体

#### 4.1.1 Workflow Session

\`\`\`sql
CREATE TABLE workflow_sessions (
    id UUID PRIMARY KEY,
    workflow_type VARCHAR(50) NOT NULL,
    status VARCHAR(20) NOT NULL,
    input_data JSONB NOT NULL,
    output_data JSONB,
    review_status VARCHAR(20),
    review_comment TEXT,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
\`\`\`

#### 4.1.2 Executor Execution

\`\`\`sql
CREATE TABLE executor_executions (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES workflow_sessions(id),
    executor_name VARCHAR(100) NOT NULL,
    status VARCHAR(20) NOT NULL,
    input_data JSONB,
    output_data JSONB,
    confidence_score DECIMAL(3,2),
    reasoning TEXT,
    evidence JSONB,
    started_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ
);
\`\`\`

### 4.2 ER 图

\`\`\`
workflow_sessions
    ├─ workflow_checkpoints (1:N)
    ├─ executor_executions (1:N)
    │   └─ agent_messages (1:N)
    ├─ index_recommendations (1:N)
    └─ configuration_recommendations (1:N)

target_databases
    ├─ slow_queries (1:N)
    ├─ index_recommendations (1:N)
    └─ configuration_recommendations (1:N)
\`\`\`

---

## 5. MCP 集成方案

### 5.1 MCP Client 架构

\`\`\`
DbOptimizer.Infrastructure/
├── MCP/
│   ├── IMcpClient.cs
│   ├── McpClientFactory.cs
│   ├── MySqlMcpClient.cs
│   └── PostgreSqlMcpClient.cs
\`\`\`

### 5.2 MCP 工具调用

\`\`\`csharp
var mcpClient = _mcpClientFactory.Create(DatabaseType.MySQL);
await mcpClient.ConnectAsync(connectionString);

var response = await mcpClient.ExecuteToolAsync("explain", new Dictionary<string, object>
{
    ["query"] = "SELECT * FROM users WHERE email = 'test@example.com'"
});

var executionPlan = JsonSerializer.Deserialize<ExecutionPlan>(response.Data);
\`\`\`

### 5.3 连接池管理

\`\`\`csharp
public class McpClientPool
{
    private readonly ConcurrentDictionary<Guid, IMcpClient> _clients = new();
    
    public async Task<IMcpClient> GetOrCreateAsync(
        Guid databaseId, 
        string connectionString, 
        DatabaseType dbType)
    {
        if (_clients.TryGetValue(databaseId, out var client))
            return client;
        
        client = _mcpClientFactory.Create(dbType);
        await client.ConnectAsync(connectionString);
        _clients[databaseId] = client;
        
        return client;
    }
}
\`\`\`

---

## 6. 前端设计

### 6.1 技术栈

- **框架**：Vue 3 (Composition API)
- **UI 库**：Element Plus
- **状态管理**：Pinia
- **路由**：Vue Router
- **HTTP 客户端**：Axios
- **代码编辑器**：Monaco Editor
- **图表**：ECharts

### 6.2 页面结构

\`\`\`
src/
├── views/
│   ├── DashboardView.vue
│   ├── SqlAnalysisView.vue
│   ├── DbConfigView.vue
│   ├── ReviewWorkspaceView.vue
│   ├── HistoryView.vue
│   └── TimelineView.vue
├── components/
│   ├── SqlEditor.vue
│   ├── WorkflowTimeline.vue
│   ├── RecommendationCard.vue
│   └── ReviewPanel.vue
├── stores/
│   ├── workflowStore.ts
│   └── reviewStore.ts
└── api/
    ├── workflowApi.ts
    └── sseClient.ts
\`\`\`

### 6.3 SSE 集成

\`\`\`typescript
// stores/workflowStore.ts
export const useWorkflowStore = defineStore('workflow', {
  state: () => ({
    currentSession: null as WorkflowSession | null,
    events: [] as WorkflowEvent[]
  }),
  
  actions: {
    subscribeToWorkflow(sessionId: string) {
      const eventSource = new EventSource(\`/api/workflows/\${sessionId}/events\`);
      
      eventSource.onmessage = (event) => {
        const data = JSON.parse(event.data);
        this.events.push(data);
      };
      
      eventSource.onerror = () => {
        eventSource.close();
      };
    }
  }
});
\`\`\`

---

## 7. API 设计

### 7.1 REST API

#### 7.1.1 创建 SQL 分析任务

\`\`\`
POST /api/workflows/sql-analysis
Content-Type: application/json

{
  "databaseId": "uuid",
  "sql": "SELECT * FROM users WHERE email = 'test@example.com'"
}

Response:
{
  "sessionId": "uuid",
  "status": "running"
}
\`\`\`

#### 7.1.2 提交审核

\`\`\`
POST /api/workflows/{sessionId}/review
Content-Type: application/json

{
  "action": "approve|reject|adjust",
  "comment": "string",
  "adjustments": {}
}

Response:
{
  "sessionId": "uuid",
  "status": "completed|running"
}
\`\`\`

### 7.2 SSE API

\`\`\`
GET /api/workflows/{sessionId}/events
Accept: text/event-stream

Response:
event: executor.started
data: {"executorName": "SqlParserExecutor", "timestamp": "..."}

event: executor.completed
data: {"executorName": "SqlParserExecutor", "confidence": 0.85, ...}
\`\`\`

---

## 8. 安全设计

### 8.1 连接字符串加密

\`\`\`csharp
public class ConnectionStringEncryptor
{
    private readonly IDataProtector _protector;
    
    public string Encrypt(string connectionString)
    {
        return _protector.Protect(connectionString);
    }
    
    public string Decrypt(string encryptedConnectionString)
    {
        return _protector.Unprotect(encryptedConnectionString);
    }
}
\`\`\`

### 8.2 输入验证

- SQL 注入防护：使用参数化查询
- 表名/列名白名单验证
- Token 限制：单次分析最多 50k tokens

---

## 9. 性能优化

### 9.1 Token 优化

- **Prompt Caching**：缓存系统提示词
- **上下文压缩**：超过 Token 预算时压缩历史消息
- **并行执行**：多个 Executor 并行执行

### 9.2 数据库优化

- **索引优化**：为常用查询添加索引
- **连接池**：复用数据库连接
- **查询缓存**：使用 Redis 缓存查询结果

---

## 10. 部署架构

### 10.1 Aspire 本地编排

\`\`\`csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("dboptimizer");

var redis = builder.AddRedis("redis");

var api = builder.AddProject<Projects.DbOptimizer_API>("api")
    .WithReference(postgres)
    .WithReference(redis);

var web = builder.AddNpmApp("web", "../DbOptimizer.Web")
    .WithReference(api)
    .WithHttpEndpoint(port: 5173);

builder.Build().Run();
\`\`\`

### 10.2 生产部署

- **容器化**：Docker + Docker Compose
- **反向代理**：Nginx
- **数据库**：托管 PostgreSQL
- **缓存**：托管 Redis

---

## 附录

### A. 术语表

| 术语 | 定义 |
|------|------|
| MAF | Microsoft Agent Framework |
| Workflow | 工作流，由多个 Executor 组成 |
| Executor | 执行器，Workflow 中的一个执行单元 |
| Checkpoint | 检查点，Workflow 的状态快照 |
| MCP | Model Context Protocol |
| SSE | Server-Sent Events |

### B. 参考资料

- [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Vue 3 Documentation](https://vuejs.org/)
- [Element Plus](https://element-plus.org/)
