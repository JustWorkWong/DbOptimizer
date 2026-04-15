# DbOptimizer 项目规范

## 项目概述

DbOptimizer 是一个基于 Microsoft Agent Framework (MAF) 和 Blazor WebAssembly 的 AI 驱动数据库性能优化平台。

**核心价值**：
- 展示 MAF 多 Agent 协作能力
- 透明化 AI 分析过程
- 提供实用的数据库优化建议

**技术栈**：.NET 10 + MAF + Blazor WebAssembly + PostgreSQL + Redis

---

## 架构原则

### 1. 约束先行

**所有开发工作必须遵循以下文档**：
- `docs/superpowers/specs/2026-04-14-dboptimizer-design.md`：需求设计
- `docs/ARCHITECTURE.md`：架构设计
- `docs/DATA_MODEL.md`：数据模型
- `docs/IMPLEMENTATION_PLAN.md`：实现计划

**规则**：
- 修改架构前，先更新文档
- 新增功能前，先评审是否符合设计
- 不要偏离核心目标（面试展示 + 实际可用）

### 2. 第一性原理

**问题本质**：
- 数据库优化的本质是什么？→ 减少资源消耗、提升响应速度
- AI 的价值是什么？→ 自动化分析、提供专家级建议
- 多 Agent 的必要性？→ 分工明确、并行执行、易于维护

**决策标准**：
- 这个功能是否解决真实问题？
- 这个设计是否最简单？
- 这个抽象是否必要？

### 3. YAGNI（You Aren't Gonna Need It）

**不要过度设计**：
- Phase 1 只实现核心 SQL 分析
- Phase 2 再加监控和配置优化
- Phase 3 才考虑 MCP 和 Skills
- Phase 4 的功能（审批流、告警）可能永远不需要

**判断标准**：
- 这个功能现在必须要吗？
- 没有这个功能，系统能运行吗？
- 这个抽象层现在有 3 个以上的实现吗？

---

## 代码规范

### 1. 命名约定

**C# 代码**：
- 类名：PascalCase（`SqlParserAgent`）
- 方法名：PascalCase（`AnalyzeAsync`）
- 参数/变量：camelCase（`sqlQuery`）
- 私有字段：_camelCase（`_dbConnection`）
- 常量：PascalCase（`MaxRetryCount`）

**数据库**：
- 表名：snake_case 复数（`slow_queries`）
- 字段名：snake_case（`created_at`）

**文件名**：
- C# 类：与类名一致（`SqlParserAgent.cs`）
- 接口：I 开头（`IDatabaseAdapter.cs`）

### 2. 项目结构

```
src/
├── DbOptimizer.API/              # Web API 层
│   ├── Controllers/              # REST API
│   ├── Hubs/                     # SignalR
│   └── MCP/                      # MCP Server
├── DbOptimizer.Core/             # 业务逻辑层
│   ├── Agents/                   # MAF Agents
│   ├── Tools/                    # MAF Tools
│   ├── Services/                 # 业务服务
│   └── Models/                   # 领域模型
├── DbOptimizer.Infrastructure/   # 基础设施层
│   ├── Database/                 # 数据库适配器
│   ├── AI/                       # AI 服务
│   ├── MCP/                      # MCP Client
│   └── Repositories/             # 数据访问
├── DbOptimizer.Web/              # Blazor 前端
│   ├── Pages/                    # 页面
│   ├── Components/               # 组件
│   └── Services/                 # 前端服务
└── DbOptimizer.Shared/           # 共享层
    ├── DTOs/                     # 数据传输对象
    └── Validators/               # 验证器
```

### 3. 依赖注入

**原则**：
- 依赖接口，不依赖实现
- 通过构造函数注入
- 避免 Service Locator 模式

**示例**：
```csharp
public class SqlParserAgent
{
    private readonly IDatabaseAdapter _dbAdapter;
    private readonly ILogger<SqlParserAgent> _logger;

    public SqlParserAgent(
        IDatabaseAdapter dbAdapter,
        ILogger<SqlParserAgent> logger)
    {
        _dbAdapter = dbAdapter;
        _logger = logger;
    }
}
```

### 4. 异步编程

**规则**：
- 所有 I/O 操作必须异步
- 方法名以 `Async` 结尾
- 使用 `ConfigureAwait(false)`（库代码）
- 避免 `async void`（除了事件处理）

**示例**：
```csharp
public async Task<OptimizationReport> AnalyzeAsync(string sql)
{
    var executionPlan = await _dbAdapter.GetExecutionPlanAsync(sql);
    var analysis = await _agent.RunAsync(executionPlan);
    return analysis;
}
```

### 5. 错误处理

**原则**：
- 使用特定异常类型
- 记录详细日志
- 不要吞掉异常
- 在边界处理异常（Controller、Agent）

**示例**：
```csharp
public async Task<OptimizationReport> AnalyzeAsync(string sql)
{
    try
    {
        return await _agent.RunAsync(sql);
    }
    catch (DatabaseConnectionException ex)
    {
        _logger.LogError(ex, "Failed to connect to database");
        throw new AnalysisException("Database connection failed", ex);
    }
}
```

---

## MAF 开发规范

### 1. Agent 设计

**单一职责**：
- 每个 Agent 只做一件事
- SqlParserAgent 只解析 SQL
- IndexAdvisorAgent 只推荐索引

**命名规范**：
- Agent 名称：`{功能}Agent`
- Tool 名称：`{功能}Tool`

**Prompt 管理**：
- Prompt 存储在 `PromptVersion` 表
- 每次修改 Prompt 创建新版本
- Agent 执行时记录使用的 Prompt 版本

### 2. Tool 设计

**原则**：
- Tool 是纯函数，无副作用（除了读取数据）
- 输入输出都要有清晰的类型定义
- 添加 `[Description]` 属性帮助 AI 理解

**示例**：
```csharp
[Description("获取表的索引信息")]
public async Task<string> GetTableIndexesAsync(
    [Description("表名")] string tableName)
{
    var indexes = await _dbAdapter.GetIndexesAsync(tableName);
    return JsonSerializer.Serialize(indexes);
}
```

### 3. 数据持久化

**必须记录**：
- Agent 执行记录（输入、输出、耗时、Token）
- Tool 调用记录（工具名、输入、输出）
- Agent 决策记录（选项、选择、推理）
- Agent 消息（Agent 间通信）
- Agent 错误（错误类型、堆栈、上下文）

**目的**：
- 调试 Agent 行为
- 优化 Prompt
- 追踪成本
- 审计

---

## 前端开发规范

### 1. Blazor 组件

**原则**：
- 组件职责单一
- 通过参数传递数据
- 通过事件回调通知父组件

**示例**：
```razor
@* AgentViewer.razor *@
<MudCard>
    <MudCardHeader>
        <MudText Typo="Typo.h6">@AgentName</MudText>
    </MudCardHeader>
    <MudCardContent>
        @foreach (var tool in ToolCalls)
        {
            <ToolCallItem Tool="@tool" />
        }
    </MudCardContent>
</MudCard>

@code {
    [Parameter] public string AgentName { get; set; }
    [Parameter] public List<ToolCallDto> ToolCalls { get; set; }
}
```

### 2. 状态管理

**使用 Fluxor**：
- State：不可变
- Action：描述发生了什么
- Reducer：根据 Action 更新 State
- Effect：处理副作用（API 调用）

**示例**：
```csharp
// State
public record AnalysisState
{
    public string CurrentSessionId { get; init; }
    public List<AgentExecutionDto> AgentExecutions { get; init; }
    public bool IsAnalyzing { get; init; }
}

// Action
public record StartAnalysisAction(string Sql);
public record AgentStartedAction(string SessionId, string AgentName);

// Reducer
public static AnalysisState Reduce(AnalysisState state, AgentStartedAction action)
{
    return state with
    {
        AgentExecutions = state.AgentExecutions.Append(new AgentExecutionDto
        {
            AgentName = action.AgentName,
            Status = "running"
        }).ToList()
    };
}
```

---

## 测试规范

### 1. 单元测试

**覆盖率要求**：> 80%

**测试内容**：
- 业务逻辑（Services）
- 数据库适配器
- Validators

**命名规范**：
```csharp
[Fact]
public async Task AnalyzeAsync_WithValidSql_ReturnsReport()
{
    // Arrange
    var sql = "SELECT * FROM users";
    
    // Act
    var report = await _analyzer.AnalyzeAsync(sql);
    
    // Assert
    Assert.NotNull(report);
    Assert.NotEmpty(report.Recommendations);
}
```

### 2. 集成测试

**测试内容**：
- API 端点
- Agent 编排
- 数据库操作

**使用 TestContainers**：
```csharp
public class AnalysisIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }
}
```

---

## Git 规范

### 1. 分支策略

- `main`：生产代码
- `develop`：开发分支
- `feature/*`：功能分支
- `bugfix/*`：修复分支

### 2. Commit 规范

**格式**：`<type>(<scope>): <subject>`

**类型**：
- `feat`：新功能
- `fix`：修复 bug
- `docs`：文档
- `refactor`：重构
- `test`：测试
- `chore`：构建/工具

**示例**：
```
feat(agent): 实现 SqlParserAgent
fix(api): 修复慢查询采集的空指针异常
docs(architecture): 更新架构设计文档
```

---

## 性能优化

### 1. 数据库

- 使用索引
- 避免 N+1 查询
- 使用连接池
- 启用查询缓存（Redis）

### 2. API

- 使用异步 I/O
- 启用响应压缩
- 使用 HTTP/2
- 实现分页

### 3. 前端

- 启用 Blazor AOT
- 使用 Lazy Loading
- 优化资源大小
- 使用虚拟滚动

---

## 安全规范

### 1. 数据库密码

- 使用 AES 加密存储
- 不记录到日志
- 支持环境变量

### 2. API 安全

- 使用 HTTPS
- 实现 CORS
- 输入验证
- SQL 注入防护

### 3. AI 安全

- 过滤敏感信息
- 限制 Token 消耗
- 实现速率限制

---

## 文档规范

### 1. 代码注释

**何时注释**：
- 复杂的业务逻辑
- 非显而易见的设计决策
- 临时的 workaround

**何时不注释**：
- 显而易见的代码
- 重复代码逻辑的描述

### 2. XML 文档

**公共 API 必须有 XML 文档**：
```csharp
/// <summary>
/// 分析 SQL 查询并生成优化建议
/// </summary>
/// <param name="sql">要分析的 SQL 查询</param>
/// <returns>优化报告</returns>
/// <exception cref="ArgumentNullException">sql 为 null</exception>
public async Task<OptimizationReport> AnalyzeAsync(string sql)
```

---

## 变更日志

- 2026-04-15: 创建项目规范
