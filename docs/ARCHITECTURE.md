# DbOptimizer 架构设计文档

**创建日期**：2026-04-15  
**版本**：v1.0

---

## 1. 架构概览

### 1.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                      Blazor WebAssembly                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ Analyze  │  │ Monitor  │  │  Config  │  │Knowledge │   │
│  │  Page    │  │  Page    │  │  Page    │  │  Page    │   │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘   │
│         │              │              │              │       │
│         └──────────────┴──────────────┴──────────────┘       │
│                          │                                    │
│                    HTTP/SignalR                              │
└─────────────────────────┼────────────────────────────────────┘
                          │
┌─────────────────────────┼────────────────────────────────────┐
│                  ASP.NET Core Web API                        │
│  ┌──────────────────────┴─────────────────────────────┐     │
│  │              Controllers / Hubs                     │     │
│  └──────────────────────┬─────────────────────────────┘     │
│                         │                                     │
│  ┌──────────────────────┴─────────────────────────────┐     │
│  │           MAF Agent Orchestrator                    │     │
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐           │     │
│  │  │  SQL    │  │Execution│  │  Index  │           │     │
│  │  │ Parser  │→ │  Plan   │→ │ Advisor │           │     │
│  │  │ Agent   │  │ Agent   │  │ Agent   │           │     │
│  │  └─────────┘  └─────────┘  └─────────┘           │     │
│  │       ↓            ↓            ↓                  │     │
│  │  ┌──────────────────────────────────┐            │     │
│  │  │      Coordinator Agent           │            │     │
│  │  └──────────────────────────────────┘            │     │
│  └────────────────────┬───────────────────────────────┘     │
│                       │                                      │
│  ┌────────────────────┴───────────────────────────────┐     │
│  │              Tools (Function Calling)              │     │
│  │  • DatabaseMetadataTool                            │     │
│  │  • ExecutionPlanTool                               │     │
│  │  • IndexAnalysisTool                               │     │
│  │  • ConfigurationTool                               │     │
│  │  • ServerMetricsTool                               │     │
│  └────────────────────┬───────────────────────────────┘     │
└─────────────────────────┼────────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
┌───────▼────────┐ ┌─────▼──────┐ ┌───────▼────────┐
│   PostgreSQL   │ │   Redis    │ │  MySQL/PgSQL   │
│  (主存储+RAG)  │ │  (缓存)    │ │  (目标数据库)  │
└────────────────┘ └────────────┘ └────────────────┘
```

### 1.2 技术选型

#### 前端：Blazor WebAssembly

**选择理由**：
1. **全栈 C#**：前后端共享模型、验证逻辑，减少重复代码
2. **类型安全**：编译时检查，减少运行时错误
3. **与 MAF 无缝集成**：同一技术栈，调试方便
4. **展示 .NET 深度**：面试时的技术亮点
5. **组件化**：MudBlazor 提供丰富的 UI 组件

**权衡**：
- ❌ 首次加载较慢（WebAssembly 下载）
- ❌ 生态不如 React 丰富
- ✅ 但对于企业内部工具，这些不是问题

**替代方案**：
- React + TypeScript：生态更丰富，但需要维护两套类型定义
- Blazor Server：更轻量，但需要持续连接，扩展性差

#### 后端：ASP.NET Core + MAF

**选择理由**：
1. **MAF 原生支持**：.NET 10 + MAF 1.0 GA
2. **高性能**：异步 I/O，适合 AI 调用场景
3. **SignalR**：实时推送 Agent 执行过程
4. **依赖注入**：清晰的架构，易于测试

#### 数据库：PostgreSQL + Redis

**PostgreSQL**：
- ✅ pgvector：向量存储（RAG）
- ✅ JSON 支持：灵活存储 Agent 状态
- ✅ 成熟稳定

**Redis**：
- ✅ 缓存：数据库 schema、执行计划
- ✅ 会话：SignalR 分布式会话
- ✅ 队列：异步任务（可选）

---

## 2. 前后端交互设计

### 2.1 通信方式

#### HTTP REST API（常规操作）

**用途**：
- CRUD 操作（数据库连接、慢查询列表）
- 触发分析任务
- 查询历史记录

**示例**：
```http
POST /api/analysis/analyze
Content-Type: application/json

{
  "sql": "SELECT * FROM users WHERE id = 1",
  "databaseConnectionId": "guid"
}

Response:
{
  "sessionId": "guid",
  "status": "running"
}
```

#### SignalR（实时推送）

**用途**：
- 实时推送 Agent 执行过程
- 推送分析进度
- 推送性能监控数据

**流程**：
```
1. 前端连接 SignalR Hub
2. 前端调用 REST API 触发分析
3. 后端通过 SignalR 推送事件：
   - AgentStarted
   - ToolCalled
   - AgentDecisionMade
   - AgentCompleted
   - AnalysisCompleted
4. 前端实时更新 UI
```

**SignalR 事件定义**：
```csharp
public interface IAnalysisHub
{
    Task AgentStarted(string sessionId, string agentName);
    Task ToolCalled(string sessionId, string agentName, string toolName, string input);
    Task AgentDecisionMade(string sessionId, string agentName, AgentDecision decision);
    Task AgentCompleted(string sessionId, string agentName, string output);
    Task AnalysisCompleted(string sessionId, OptimizationReport report);
    Task AnalysisError(string sessionId, string error);
}
```

### 2.2 前端状态管理

**方案**：Fluxor（Blazor 的 Redux 实现）

**状态结构**：
```csharp
public class AppState
{
    public AnalysisState Analysis { get; set; }
    public MonitorState Monitor { get; set; }
    public ConfigState Config { get; set; }
    public KnowledgeState Knowledge { get; set; }
}

public class AnalysisState
{
    public string CurrentSessionId { get; set; }
    public List<AgentExecutionDto> AgentExecutions { get; set; }
    public OptimizationReportDto Report { get; set; }
    public bool IsAnalyzing { get; set; }
}
```

**数据流**：
```
User Action → Dispatch Action → Reducer → Update State → UI Re-render
                                    ↓
                            Side Effect (API Call)
                                    ↓
                            SignalR Event → Dispatch Action
```

### 2.3 前端页面设计

#### 2.3.1 分析页面（Analyze.razor）

**布局**：
```
┌─────────────────────────────────────────────────────────┐
│  SQL 编辑器 (Monaco Editor)                             │
│  ┌───────────────────────────────────────────────────┐  │
│  │ SELECT * FROM users WHERE created_at > '2024-01-01'│  │
│  │                                                     │  │
│  └───────────────────────────────────────────────────┘  │
│  [分析] [清空]                                          │
├─────────────────────────────────────────────────────────┤
│  Agent 执行过程 (实时更新)                              │
│  ┌─────────────────────────────────────────────────────┐│
│  │ ✓ SqlParserAgent (完成 - 2.3s)                      ││
│  │   └─ 识别到 WHERE 条件: created_at                  ││
│  │   └─ 调用工具: GetTableSchema("users")              ││
│  │                                                       ││
│  │ ⏳ ExecutionPlanAgent (进行中...)                    ││
│  │   └─ 调用工具: GetExecutionPlan(sql)                ││
│  │   └─ 分析中: 发现全表扫描...                        ││
│  │                                                       ││
│  │ ⏸ IndexAdvisorAgent (等待中)                        ││
│  └─────────────────────────────────────────────────────┘│
├─────────────────────────────────────────────────────────┤
│  优化建议 (分析完成后显示)                              │
│  ┌─────────────────────────────────────────────────────┐│
│  │ 🔴 高优先级                                          ││
│  │ 创建索引: CREATE INDEX idx_created_at ON users(...) ││
│  │ 预估提升: 80% | 风险: 低                            ││
│  │ [复制SQL] [查看详情]                                ││
│  └─────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

**交互流程**：
1. 用户输入 SQL
2. 点击"分析"按钮
3. 前端调用 API，获取 sessionId
4. 前端订阅 SignalR 事件
5. 实时显示 Agent 执行过程
6. 分析完成，显示优化建议

#### 2.3.2 监控页面（Monitor.razor）

**布局**：
```
┌─────────────────────────────────────────────────────────┐
│  性能指标 (实时更新)                                     │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │ QPS      │ │ 慢查询   │ │ 连接数   │ │ 命中率   │  │
│  │ 1,234    │ │ 23       │ │ 45/100   │ │ 95.2%    │  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘  │
├─────────────────────────────────────────────────────────┤
│  慢查询列表                                              │
│  ┌─────────────────────────────────────────────────────┐│
│  │ SQL                    │ 耗时  │ 执行时间 │ 操作    ││
│  │ SELECT * FROM orders...│ 3.2s  │ 10:23:45 │ [分析] ││
│  │ UPDATE users SET...    │ 2.1s  │ 10:22:30 │ [分析] ││
│  └─────────────────────────────────────────────────────┘│
├─────────────────────────────────────────────────────────┤
│  性能趋势图 (ECharts)                                    │
│  ┌─────────────────────────────────────────────────────┐│
│  │     QPS                                              ││
│  │ 2000┤                    ╭─╮                        ││
│  │ 1500┤          ╭─╮      │ │                        ││
│  │ 1000┤    ╭─╮  │ │  ╭─╮│ │                        ││
│  │  500┤╭─╮│ │╭─╯ │╭─╯ ││ │                        ││
│  │    0└┴─┴┴─┴┴───┴┴───┴┴─┴────────────────────────  ││
│  │      10:00  10:30  11:00  11:30  12:00            ││
│  └─────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

#### 2.3.3 配置优化页面（Config.razor）

**布局**：
```
┌─────────────────────────────────────────────────────────┐
│  服务器资源                                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │
│  │ CPU      │ │ 内存     │ │ 磁盘     │ │ 网络     │  │
│  │ ●●●●○    │ │ ●●●○○    │ │ ●●○○○    │ │ ●○○○○    │  │
│  │ 75%      │ │ 60%      │ │ 45%      │ │ 20%      │  │
│  └──────────┘ └──────────┘ └──────────┘ └──────────┘  │
├─────────────────────────────────────────────────────────┤
│  配置建议                                                │
│  ┌─────────────────────────────────────────────────────┐│
│  │ 🔴 高优先级                                          ││
│  │ innodb_buffer_pool_size                             ││
│  │ 当前值: 128M → 建议值: 4G                           ││
│  │ 原因: 数据量 10GB，buffer pool 过小导致命中率仅 60% ││
│  │ 预估提升: 30-50% | 风险: 需要重启数据库             ││
│  │ [应用] [忽略]                                        ││
│  └─────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────┘
```

---

## 3. 后端架构设计

### 3.1 分层架构

```
┌─────────────────────────────────────────────────────────┐
│                    Presentation Layer                    │
│  • Controllers (REST API)                                │
│  • Hubs (SignalR)                                        │
│  • DTOs                                                  │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│                   Application Layer                      │
│  • Services (业务逻辑)                                   │
│  • Agent Orchestrator (MAF 编排)                        │
│  • MCP Server (对外服务)                                │
│  • Skills Manager (插件管理)                            │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│                     Domain Layer                         │
│  • Agents (MAF Agents)                                   │
│  • Tools (Function Calling)                              │
│  • Domain Models                                         │
│  • Domain Services                                       │
└─────────────────────┬───────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────┐
│                 Infrastructure Layer                     │
│  • Database Adapters (MySQL/PostgreSQL)                  │
│  • MCP Client (使用现有 MCP)                            │
│  • Repositories (数据访问)                               │
│  • External Services (Azure OpenAI)                      │
└─────────────────────────────────────────────────────────┘
```

### 3.2 MAF Agent 编排

#### 3.2.1 Agent 定义

```csharp
// SqlParserAgent
public class SqlParserAgent
{
    private readonly AIAgent _agent;

    public SqlParserAgent(IChatClient chatClient)
    {
        _agent = chatClient.AsAIAgent(
            name: "SqlParser",
            instructions: @"
                你是 SQL 解析专家。分析 SQL 语法结构：
                1. 识别查询类型（SELECT/UPDATE/DELETE）
                2. 提取表名、列名、WHERE 条件
                3. 识别 JOIN 类型和顺序
                4. 识别聚合函数、子查询
                输出 JSON 格式的解析结果。
            ",
            tools: [
                AIFunctionFactory.Create(GetTableSchema),
                AIFunctionFactory.Create(GetColumnStats)
            ]
        );
    }

    [Description("获取表结构")]
    public async Task<string> GetTableSchema(
        [Description("表名")] string tableName)
    {
        // 调用 DatabaseMetadataTool
    }

    public async Task<SqlParseResult> ParseAsync(string sql)
    {
        var result = await _agent.RunAsync<SqlParseResult>(sql);
        return result.Result;
    }
}
```

#### 3.2.2 Agent 编排流程

```csharp
public class AnalysisOrchestrator
{
    private readonly SqlParserAgent _sqlParser;
    private readonly ExecutionPlanAgent _planAnalyzer;
    private readonly IndexAdvisorAgent _indexAdvisor;
    private readonly CoordinatorAgent _coordinator;
    private readonly IAnalysisHub _hub;

    public async Task<OptimizationReport> AnalyzeAsync(
        string sessionId, 
        string sql)
    {
        // 1. SQL 解析
        await _hub.AgentStarted(sessionId, "SqlParser");
        var parseResult = await _sqlParser.ParseAsync(sql);
        await _hub.AgentCompleted(sessionId, "SqlParser", 
            JsonSerializer.Serialize(parseResult));

        // 2. 执行计划分析
        await _hub.AgentStarted(sessionId, "ExecutionPlanAnalyzer");
        var planAnalysis = await _planAnalyzer.AnalyzeAsync(sql, parseResult);
        await _hub.AgentCompleted(sessionId, "ExecutionPlanAnalyzer", 
            JsonSerializer.Serialize(planAnalysis));

        // 3. 索引推荐
        await _hub.AgentStarted(sessionId, "IndexAdvisor");
        var indexRecommendations = await _indexAdvisor.RecommendAsync(
            sql, parseResult, planAnalysis);
        await _hub.AgentCompleted(sessionId, "IndexAdvisor", 
            JsonSerializer.Serialize(indexRecommendations));

        // 4. 协调整合
        await _hub.AgentStarted(sessionId, "Coordinator");
        var report = await _coordinator.GenerateReportAsync(
            parseResult, planAnalysis, indexRecommendations);
        await _hub.AnalysisCompleted(sessionId, report);

        return report;
    }
}
```

### 3.3 数据访问层

#### 3.3.1 Repository 模式

```csharp
public interface IAnalysisSessionRepository
{
    Task<AnalysisSession> CreateAsync(AnalysisSession session);
    Task<AnalysisSession> GetByIdAsync(Guid id);
    Task UpdateAsync(AnalysisSession session);
    Task<List<AnalysisSession>> GetByUserIdAsync(Guid userId, int limit);
}

public class AnalysisSessionRepository : IAnalysisSessionRepository
{
    private readonly DbContext _context;

    public async Task<AnalysisSession> CreateAsync(AnalysisSession session)
    {
        _context.AnalysisSessions.Add(session);
        await _context.SaveChangesAsync();
        return session;
    }
    
    // ... 其他实现
}
```

#### 3.3.2 数据库适配器

```csharp
public interface IDatabaseAdapter
{
    Task<string> GetExecutionPlanAsync(string sql);
    Task<List<SlowQuery>> GetSlowQueriesAsync(int limit);
    Task<Dictionary<string, string>> GetConfigurationAsync();
    Task<ServerResources> GetServerResourcesAsync();
    Task<DatabaseStats> GetDatabaseStatsAsync();
}

public class MySqlAdapter : IDatabaseAdapter
{
    private readonly IDbConnection _connection;

    public async Task<string> GetExecutionPlanAsync(string sql)
    {
        var plan = await _connection.QueryFirstAsync<string>(
            $"EXPLAIN FORMAT=JSON {sql}");
        return plan;
    }

    public async Task<Dictionary<string, string>> GetConfigurationAsync()
    {
        var config = await _connection.QueryAsync<(string, string)>(
            @"SHOW VARIABLES WHERE Variable_name IN (
                'innodb_buffer_pool_size',
                'max_connections',
                'query_cache_size'
            )");
        return config.ToDictionary(x => x.Item1, x => x.Item2);
    }
    
    // ... 其他实现
}
```

---

## 4. 最佳实践

### 4.1 错误处理

**前端**：
```csharp
try
{
    await AnalysisService.AnalyzeAsync(sql);
}
catch (ApiException ex) when (ex.StatusCode == 400)
{
    Snackbar.Add("SQL 语法错误", Severity.Error);
}
catch (ApiException ex) when (ex.StatusCode == 500)
{
    Snackbar.Add("服务器错误，请稍后重试", Severity.Error);
}
catch (Exception ex)
{
    Logger.LogError(ex, "分析失败");
    Snackbar.Add("未知错误", Severity.Error);
}
```

**后端**：
```csharp
[HttpPost("analyze")]
public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
{
    try
    {
        var result = await _orchestrator.AnalyzeAsync(request.Sql);
        return Ok(result);
    }
    catch (SqlParseException ex)
    {
        return BadRequest(new { error = "SQL 语法错误", details = ex.Message });
    }
    catch (DatabaseConnectionException ex)
    {
        return StatusCode(503, new { error = "数据库连接失败" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "分析失败");
        return StatusCode(500, new { error = "服务器内部错误" });
    }
}
```

### 4.2 性能优化

**前端**：
- 虚拟滚动（慢查询列表）
- 懒加载（Monaco Editor）
- 防抖（SQL 输入）
- 缓存（数据库连接列表）

**后端**：
- Prompt Caching（数据库 schema）
- Redis 缓存（执行计划、配置）
- 异步处理（长时间分析）
- 连接池（数据库连接）

### 4.3 安全性

**连接字符串加密**：
```csharp
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
```

**API 认证**：
```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnalysisController : ControllerBase
{
    // ...
}
```

### 4.4 可观测性

**日志**：
```csharp
_logger.LogInformation(
    "分析开始: SessionId={SessionId}, SQL={Sql}", 
    sessionId, sql);

_logger.LogWarning(
    "Agent 执行超时: AgentName={AgentName}, Duration={Duration}ms", 
    agentName, duration);
```

**指标**：
```csharp
// Prometheus metrics
_metrics.RecordAgentDuration(agentName, duration);
_metrics.IncrementAgentExecutions(agentName, status);
_metrics.RecordTokensUsed(agentName, tokens);
```

**追踪**：
```csharp
using var activity = ActivitySource.StartActivity("AnalyzeSQL");
activity?.SetTag("sql.length", sql.Length);
activity?.SetTag("session.id", sessionId);
```

---

## 5. 部署架构

### 5.1 开发环境

```
Docker Compose:
- DbOptimizer.API (localhost:5000)
- PostgreSQL (localhost:5432)
- Redis (localhost:6379)
- Blazor WebAssembly (localhost:5001)
```

### 5.2 生产环境

```
Azure/AWS:
- App Service / ECS (API)
- Static Web App / S3 + CloudFront (Blazor)
- Azure Database for PostgreSQL / RDS
- Azure Cache for Redis / ElastiCache
- Application Insights / CloudWatch (监控)
```

---

## 6. 文档拆分建议

当前文档结构：
```
docs/
├── superpowers/specs/
│   └── 2026-04-14-dboptimizer-design.md  (需求 + 数据模型，776 行)
└── ARCHITECTURE.md  (本文档，架构设计)
```

**建议拆分**：
```
docs/
├── superpowers/specs/
│   ├── 2026-04-15-requirements.md        # 需求文档（功能列表）
│   ├── 2026-04-15-data-model.md          # 数据模型（所有实体）
│   └── 2026-04-15-maf-integration.md     # MAF 集成设计
├── ARCHITECTURE.md                        # 架构设计（本文档）
├── API.md                                 # API 文档
├── DEPLOYMENT.md                          # 部署指南
└── DEVELOPMENT.md                         # 开发指南
```

