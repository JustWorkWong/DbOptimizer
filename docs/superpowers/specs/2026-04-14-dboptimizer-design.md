# DbOptimizer 需求设计文档

**项目名称**：DbOptimizer - AI 驱动的数据库性能优化平台  
**创建日期**：2026-04-14  
**版本**：v1.0  
**作者**：tengfengsu

---

## 1. 项目概述

### 1.1 项目定位

DbOptimizer 是一个基于 Microsoft Agent Framework (MAF) 和 Blazor 的数据库性能优化平台，通过多 Agent 协作提供智能化的数据库优化建议。

**核心价值**：
- **开发环境辅助**：开发者本地调试时快速分析慢查询
- **生产环境监控**：持续监控数据库性能，自动发现优化机会
- **AI 驱动**：多 Agent 协作，提供深度分析和优化建议
- **可扩展**：支持 MCP、Skills 插件，与现有工具生态集成

### 1.2 技术栈

**后端**：
- .NET 10
- ASP.NET Core Web API
- Microsoft Agent Framework (MAF)
- SignalR（实时通信）
- Entity Framework Core
- Dapper（高性能查询）
- Quartz.NET（定时任务）

**前端**：
- Blazor WebAssembly
- MudBlazor（UI 组件库）
- Monaco Editor（SQL 编辑器）
- ECharts（图表）

**数据库**：
- PostgreSQL（主存储 + pgvector）
- Redis（缓存 + 会话）

**AI**：
- Azure OpenAI / Anthropic Claude
- Microsoft Agent Framework
- Vector Store（RAG）

**支持的目标数据库**：
- MySQL 5.7+
- PostgreSQL 13+

### 1.3 部署模式

**混合部署**（用户可选）：
1. **中心化模式**：单一服务，通过连接字符串远程监控多个数据库
2. **Agent 模式**：在数据库服务器部署轻量级 Agent，采集数据推送到中心服务

---

## 2. 核心功能

### 2.1 慢查询分析

**功能描述**：
- 手动输入 SQL 进行分析
- 自动采集数据库慢查询日志
- 多 Agent 协作分析（SQL 解析 + 执行计划 + 索引推荐）
- 实时显示 Agent 工作过程（透明化 AI）

**技术实现**：
- `SqlParserAgent`：分析 SQL 语法结构
- `ExecutionPlanAgent`：解读执行计划
- `IndexAdvisorAgent`：推荐索引策略
- `CoordinatorAgent`：整合结果，生成报告

**输出**：
- 问题诊断（全表扫描、索引失效、JOIN 顺序）
- 优化建议（索引创建、SQL 重写）
- 预估性能提升

### 2.2 配置优化建议

**功能描述**：
- 采集数据库配置参数
- 采集服务器资源（CPU、内存、磁盘、网络）
- 采集数据库状态（数据量、QPS、Buffer Pool 命中率）
- AI 综合分析，给出配置优化建议

**技术实现**：
- `ConfigurationAdvisorAgent`：分析配置参数
- `ResourceAnalyzerAgent`：分析资源瓶颈
- `ConfigurationTool`：读取数据库配置
- `ServerMetricsTool`：采集服务器指标
- `DatabaseStatsTool`：采集数据库统计信息

**输出**：
- 配置参数调整建议（参数名、当前值、建议值、原因）
- 资源瓶颈分析（CPU/内存/磁盘/网络）
- 优先级排序（高/中/低）
- 风险提示（调整可能的副作用）

### 2.3 性能监控

**功能描述**：
- 实时监控数据库性能指标
- 慢查询自动采集（定时任务）
- 性能趋势图表
- 告警通知（可选）

**监控指标**：
- QPS / TPS
- 慢查询数量和占比
- 连接数
- Buffer Pool 命中率
- 磁盘 IO
- 锁等待

**技术实现**：
- `SlowQueryCollector`：定时采集慢查询
- `MetricsCollector`：采集性能指标
- SignalR：实时推送数据到前端
- Quartz.NET：定时任务调度

### 2.4 知识库（RAG）

**功能描述**：
- 存储历史优化案例
- 向量化检索相似问题
- AI 参考历史案例给出建议

**数据来源**：
- 用户的优化历史
- 数据库最佳实践文档
- MySQL/PostgreSQL 官方文档

**技术实现**：
- pgvector：向量存储
- Embedding：文本向量化
- 语义搜索：检索相似案例

### 2.5 效果验证（可选）

**功能描述**：
- 在测试环境执行优化前后的 SQL
- 对比执行时间、IO、CPU
- 生成性能对比报告

**技术实现**：
- `PerformanceValidator`：A/B 测试
- 执行计划对比
- 性能指标对比

### 2.6 MCP 集成

#### 2.6.1 MCP Server（对外提供服务）

**暴露的工具**：
- `analyze_slow_query`：分析慢查询
- `get_slow_queries`：获取慢查询列表
- `recommend_indexes`：推荐索引
- `analyze_configuration`：分析配置
- `get_resource_usage`：获取资源使用情况

**使用场景**：
- Claude Code 通过 MCP 调用
- 其他 AI 工具集成
- 自动化脚本调用

#### 2.6.2 MCP Client（使用现有 MCP）

**集成现有 MCP Server**：
- `@modelcontextprotocol/server-postgres`
- `@modelcontextprotocol/server-mysql`

**好处**：
- 复用现有生态
- 标准化接口
- 减少重复开发

### 2.7 Skills 插件系统

#### 2.7.1 内置 Skills

- `/analyze-slow-query`：分析单个查询
- `/recommend-indexes`：推荐索引
- `/explain-plan`：解释执行计划
- `/analyze-config`：分析配置
- `/compare-queries`：对比查询性能

#### 2.7.2 外部 Skills 支持

**插件接口**：
```csharp
public interface ISkillPlugin
{
    string Name { get; }
    string Description { get; }
    Task<SkillResult> ExecuteAsync(SkillContext context);
}
```

**加载机制**：
- 从 `./skills` 目录加载
- 从 `~/.dboptimizer/skills` 加载用户自定义 Skills
- 动态注册到系统

---

## 3. 数据持久化设计

### 3.1 核心数据模型

#### 3.1.1 用户数据

```csharp
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserPreferences Preferences { get; set; }
}

public class UserPreferences
{
    public Guid UserId { get; set; }
    public string PreferredDatabase { get; set; }  // mysql/postgresql
    public string Theme { get; set; }
    public bool EnableAutoAnalysis { get; set; }
    public int SlowQueryThreshold { get; set; }    // 毫秒
    public Dictionary<string, object> CustomSettings { get; set; }
}
```

#### 3.1.2 数据库连接

```csharp
public class DatabaseConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; }
    public string DatabaseType { get; set; }       // mysql/postgresql
    public string ConnectionString { get; set; }   // 加密存储
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastConnectedAt { get; set; }
}
```

#### 3.1.3 慢查询记录

```csharp
public class SlowQuery
{
    public Guid Id { get; set; }
    public Guid DatabaseConnectionId { get; set; }
    public string Sql { get; set; }
    public string NormalizedSql { get; set; }      // 参数化后的 SQL
    public double ExecutionTime { get; set; }      // 毫秒
    public DateTime ExecutedAt { get; set; }
    public string ExecutionPlan { get; set; }
    public bool IsAnalyzed { get; set; }
    public Guid? AnalysisSessionId { get; set; }
}
```

#### 3.1.4 分析会话

```csharp
public class AnalysisSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? SlowQueryId { get; set; }
    public string InputSql { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; }             // running/completed/failed
    public List<AgentExecution> AgentExecutions { get; set; }
    public OptimizationReport Report { get; set; }
}
```

#### 3.1.5 Agent 执行记录

```csharp
public class AgentExecution
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string AgentName { get; set; }          // SqlParser/ExecutionPlan/IndexAdvisor
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; }
    public string Input { get; set; }
    public string Output { get; set; }
    public List<ToolCall> ToolCalls { get; set; }
    public string ReasoningProcess { get; set; }   // AI 推理过程
    public int TokensUsed { get; set; }
}
```

#### 3.1.6 工具调用记录

```csharp
public class ToolCall
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string ToolName { get; set; }
    public string Input { get; set; }
    public string Output { get; set; }
    public DateTime CalledAt { get; set; }
    public double DurationMs { get; set; }
}
```

#### 3.1.7 优化报告

```csharp
public class OptimizationReport
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public List<Issue> Issues { get; set; }
    public List<Recommendation> Recommendations { get; set; }
    public string Summary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Issue
{
    public string Type { get; set; }               // full_table_scan/index_missing/join_order
    public string Severity { get; set; }           // high/medium/low
    public string Description { get; set; }
    public string Location { get; set; }           // SQL 中的位置
}

public class Recommendation
{
    public string Type { get; set; }               // create_index/rewrite_sql/adjust_config
    public string Priority { get; set; }           // high/medium/low
    public string Title { get; set; }
    public string Description { get; set; }
    public string Code { get; set; }               // DDL/SQL
    public string ExpectedImpact { get; set; }
    public string Risk { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
}
```

#### 3.1.8 配置分析记录

```csharp
public class ConfigurationAnalysis
{
    public Guid Id { get; set; }
    public Guid DatabaseConnectionId { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public Dictionary<string, string> CurrentConfig { get; set; }
    public ServerResources ServerResources { get; set; }
    public DatabaseStats DatabaseStats { get; set; }
    public List<ConfigRecommendation> Recommendations { get; set; }
    public List<ResourceBottleneck> Bottlenecks { get; set; }
}

public class ServerResources
{
    public int CpuCores { get; set; }
    public double CpuUsage { get; set; }
    public long TotalMemoryMB { get; set; }
    public long AvailableMemoryMB { get; set; }
    public string DiskType { get; set; }
    public int DiskIops { get; set; }
    public long DiskFreeGB { get; set; }
}

public class DatabaseStats
{
    public long TotalDataSizeGB { get; set; }
    public int TableCount { get; set; }
    public long IndexSizeGB { get; set; }
    public double QueriesPerSecond { get; set; }
    public double TransactionsPerSecond { get; set; }
    public double BufferPoolHitRate { get; set; }
    public double SlowQueryRatio { get; set; }
}

public class ConfigRecommendation
{
    public string Parameter { get; set; }
    public string CurrentValue { get; set; }
    public string RecommendedValue { get; set; }
    public string Reason { get; set; }
    public string Priority { get; set; }
    public string Impact { get; set; }
    public string Risk { get; set; }
}
```

#### 3.1.9 知识库（RAG）

```csharp
public class KnowledgeEntry
{
    public Guid Id { get; set; }
    public string Type { get; set; }               // optimization_case/best_practice/documentation
    public string Title { get; set; }
    public string Content { get; set; }
    public string[] Tags { get; set; }
    public float[] Embedding { get; set; }         // 向量
    public DateTime CreatedAt { get; set; }
    public int UsageCount { get; set; }
}
```

#### 3.1.10 MAF Checkpoint

```csharp
public class AgentCheckpoint
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string AgentName { get; set; }
    public string State { get; set; }              // JSON 序列化的状态
    public DateTime CreatedAt { get; set; }
}
```

#### 3.1.11 用户反馈

```csharp
public class UserFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? RecommendationId { get; set; }
    public string FeedbackType { get; set; }       // helpful/not_helpful/applied/rejected
    public string Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### 3.2 数据库 Schema

**PostgreSQL 表结构**：
- `users`
- `user_preferences`
- `database_connections`
- `slow_queries`
- `analysis_sessions`
- `agent_executions`
- `tool_calls`
- `optimization_reports`
- `issues`
- `recommendations`
- `configuration_analyses`
- `config_recommendations`
- `resource_bottlenecks`
- `knowledge_entries`（使用 pgvector）
- `agent_checkpoints`
- `user_feedback`

### 3.3 数据保留策略

**慢查询记录**：
- 保留 30 天
- 超过 30 天自动归档或删除

**分析会话**：
- 保留 90 天
- 用户可手动删除

**Agent 执行记录**：
- 保留 30 天
- 用于调试和审计

**知识库**：
- 永久保留
- 定期清理低质量条目

**用户反馈**：
- 永久保留
- 用于改进 AI 模型

---

## 4. 非功能需求

### 4.1 安全性

**只读模式**：
- AI 只生成优化建议，不自动执行
- 用户手动审核后执行

**数据安全**：
- 数据库连接字符串加密存储
- API 使用 JWT 认证
- HTTPS 强制

**权限控制**：
- 用户只能访问自己的数据
- 管理员可查看所有数据

### 4.2 性能

**响应时间**：
- 单查询分析：< 10 秒
- 配置分析：< 15 秒
- 页面加载：< 2 秒

**并发**：
- 支持 100+ 并发用户
- Agent 执行队列化

**缓存**：
- Redis 缓存数据库元数据
- Prompt Caching 减少 AI 成本

### 4.3 可观测性

**日志**：
- 结构化日志（Serilog）
- 日志级别：Debug/Info/Warning/Error

**监控**：
- OpenTelemetry 追踪
- Agent 执行时间
- Token 消耗统计

**告警**：
- AI 调用失败
- 数据库连接失败
- 系统资源不足

### 4.4 可扩展性

**水平扩展**：
- 无状态 API（通过 Redis 共享会话）
- 数据库连接池

**插件化**：
- Skills 插件系统
- 数据库适配器接口
- MCP 集成

---

## 5. 实现优先级

### Phase 1: 核心 AI 能力（2周）

**目标**：MVP 可演示

**功能**：
1. ✅ 基础架构（.NET 10 + Blazor + MAF）
2. ✅ 3 个核心 Agent（SqlParser + ExecutionPlan + IndexAdvisor）
3. ✅ 手动输入 SQL 分析
4. ✅ 透明化展示 AI 过程（实时显示 Agent 工作）
5. ✅ MySQL/PostgreSQL 适配器
6. ✅ 数据持久化（用户、分析会话、Agent 执行记录）

**交付物**：
- 可运行的 Web 应用
- 单查询分析功能
- Agent 协作可视化

### Phase 2: 监控 + 配置优化（2周）

**目标**：实用功能完善

**功能**：
1. ✅ 连接数据库采集慢查询
2. ✅ 配置优化 Agent（ConfigurationAdvisor + ResourceAnalyzer）
3. ✅ MCP Client 集成（使用现有 postgres/mysql MCP）
4. ✅ RAG 知识库（pgvector）
5. ✅ 性能趋势图表
6. ✅ 定时任务（Quartz.NET）

**交付物**：
- 监控面板
- 配置分析功能
- 知识库检索

### Phase 3: MCP Server + Skills（1周）

**目标**：生态集成

**功能**：
1. ✅ MCP Server 实现
2. ✅ 内置 Skills
3. ✅ Skills 插件系统
4. ✅ 文档完善（API 文档、使用指南）

**交付物**：
- MCP Server 可用
- Skills 可在 Claude Code 中调用
- 完整文档

### Phase 4: 生产级特性（可选）

**目标**：企业级能力

**功能**：
1. ⏸️ Agent 模式部署
2. ⏸️ 审批流（类似 AIDemo）
3. ⏸️ 告警通知
4. ⏸️ 多租户支持
5. ⏸️ 权限管理

---

## 6. 技术亮点（面试展示）

### 6.1 MAF 多 Agent 协作

**设计模式**：
- 专家分工（SqlParser / ExecutionPlan / IndexAdvisor / ConfigurationAdvisor）
- 协调者模式（CoordinatorAgent）
- 工具调用（Function Calling）

**可讲点**：
- 为什么用多 Agent 而不是单 Agent
- Agent 间如何通信
- 如何处理 Agent 失败

### 6.2 透明化 AI 过程

**实现**：
- SignalR 实时推送 Agent 状态
- 前端可视化 Agent 工作流
- 显示工具调用和推理过程

**可讲点**：
- 为什么要透明化（可解释性、可调试性）
- 如何实现实时推送
- 用户体验设计

### 6.3 MCP 双向集成

**作为 Server**：
- 对外提供标准化服务
- 其他工具可集成

**作为 Client**：
- 复用现有 MCP 生态
- 减少重复开发

**可讲点**：
- MCP 协议的价值
- 互操作性设计
- 标准化的重要性

### 6.4 Skills 插件化

**设计**：
- 插件接口
- 动态加载
- 用户自定义 Skills

**可讲点**：
- 可扩展架构
- 插件系统设计
- 如何平衡灵活性和安全性

### 6.5 数据持久化

**设计**：
- 完整的数据模型
- Agent 执行记录
- MAF Checkpoint
- 用户反馈

**可讲点**：
- 为什么要保存 Agent 执行过程
- Checkpoint 的作用
- 如何利用用户反馈改进 AI

### 6.6 配置优化

**多维度分析**：
- 数据库配置
- 服务器资源
- 数据库状态

**可讲点**：
- 系统级优化思维
- AI 如何综合多维度数据
- 量化收益和风险评估

---

## 7. 风险和挑战

### 7.1 技术风险

**MAF 稳定性**：
- MAF 1.0 刚 GA，可能有 bug
- 缓解：充分测试，准备降级方案

**AI 成本**：
- 频繁调用 AI 可能成本高
- 缓解：Prompt Caching、批量处理

**性能**：
- Agent 协作可能较慢
- 缓解：并行执行、异步处理

### 7.2 产品风险

**用户接受度**：
- 用户可能不信任 AI 建议
- 缓解：透明化过程、提供证据

**竞争**：
- 市场上已有类似工具
- 缓解：差异化（MAF、多 Agent、透明化）

### 7.3 时间风险

**开发周期**：
- 5 周可能不够
- 缓解：严格按 Phase 执行，优先 MVP

---

## 8. 成功标准

### 8.1 Phase 1 成功标准

- ✅ 可以输入 SQL 并得到优化建议
- ✅ 可以看到 Agent 协作过程
- ✅ 建议准确率 > 70%（人工评估）

### 8.2 Phase 2 成功标准

- ✅ 可以连接数据库并采集慢查询
- ✅ 配置分析给出合理建议
- ✅ RAG 检索相关案例

### 8.3 Phase 3 成功标准

- ✅ MCP Server 可被 Claude Code 调用
- ✅ Skills 可正常工作
- ✅ 文档完整

### 8.4 最终成功标准

- ✅ 可以在面试中完整演示
- ✅ 可以讲清楚技术设计和权衡
- ✅ 代码质量高，可维护
- ✅ 真正可用（不只是 demo）

---

## 9. 附录

### 9.1 参考项目

- [Supabase Index Advisor](https://github.com/supabase/index_advisor)
- [Laravel Slower](https://github.com/halilcosdu/laravel-slower)
- [Yahoo MySQL Performance Analyzer](https://github.com/yahoo/mysql_perf_analyzer)

### 9.2 技术文档

- [Microsoft Agent Framework](https://learn.microsoft.com/zh-cn/agent-framework/overview/?pivots=programming-language-csharp)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [pgvector](https://github.com/pgvector/pgvector)

---

**文档状态**：待审核  
**下一步**：用户审核 → 编写实现计划

#### 3.1.12 Agent 间通信

```csharp
public class AgentMessage
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string FromAgent { get; set; }
    public string ToAgent { get; set; }
    public string MessageType { get; set; }        // request/response/notification
    public string Content { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
}
```

#### 3.1.13 Agent 决策记录

```csharp
public class AgentDecision
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string DecisionPoint { get; set; }      // "选择工具"/"判断是否需要更多信息"
    public List<string> Options { get; set; }      // 可选项
    public string SelectedOption { get; set; }
    public string Reasoning { get; set; }          // 为什么选这个
    public double Confidence { get; set; }         // 置信度 0-1
    public DateTime DecidedAt { get; set; }
}
```

#### 3.1.14 Agent 错误详情

```csharp
public class AgentError
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string ErrorType { get; set; }          // timeout/api_error/validation_error/tool_error
    public string ErrorMessage { get; set; }
    public string StackTrace { get; set; }
    public string Context { get; set; }            // 发生错误时的上下文（JSON）
    public DateTime OccurredAt { get; set; }
    public bool IsRetryable { get; set; }
    public int RetryCount { get; set; }
}
```

#### 3.1.15 Prompt 版本管理

```csharp
public class PromptVersion
{
    public Guid Id { get; set; }
    public string AgentName { get; set; }
    public int Version { get; set; }
    public string PromptTemplate { get; set; }
    public string Description { get; set; }        // 版本说明
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; }
}

// AgentExecution 补充字段
public class AgentExecution
{
    // ... 现有字段
    public Guid PromptVersionId { get; set; }      // 使用的 Prompt 版本
    public string PromptSnapshot { get; set; }     // Prompt 快照（防止版本被删除）
}
```

#### 3.1.16 Agent 性能指标

```csharp
public class AgentMetrics
{
    public Guid Id { get; set; }
    public string AgentName { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public double AverageDurationMs { get; set; }
    public double AverageTokensUsed { get; set; }
    public double SuccessRate { get; set; }
    public double P95DurationMs { get; set; }      // 95 分位耗时
    public double P99DurationMs { get; set; }      // 99 分位耗时
}
```

### 3.2 数据库 Schema（更新）

**PostgreSQL 表结构**：
- `users`
- `user_preferences`
- `database_connections`
- `slow_queries`
- `analysis_sessions`
- `agent_executions`
- `tool_calls`
- `agent_messages`（新增）
- `agent_decisions`（新增）
- `agent_errors`（新增）
- `prompt_versions`（新增）
- `agent_metrics`（新增）
- `optimization_reports`
- `issues`
- `recommendations`
- `configuration_analyses`
- `config_recommendations`
- `resource_bottlenecks`
- `knowledge_entries`（使用 pgvector）
- `agent_checkpoints`
- `user_feedback`

### 3.3 数据保留策略（更新）

**慢查询记录**：
- 保留 30 天
- 超过 30 天自动归档或删除

**分析会话**：
- 保留 90 天
- 用户可手动删除

**Agent 执行记录**：
- 保留 30 天
- 用于调试和审计

**Agent 消息/决策/错误**：
- 保留 30 天
- 与 Agent 执行记录同步清理

**Prompt 版本**：
- 永久保留
- 不活跃版本可归档

**Agent 性能指标**：
- 按天聚合：保留 90 天
- 按月聚合：永久保留

**知识库**：
- 永久保留
- 定期清理低质量条目

**用户反馈**：
- 永久保留
- 用于改进 AI 模型

