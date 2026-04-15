# DbOptimizer 数据模型设计文档

**创建日期**：2026-04-15  
**版本**：v1.0

---

## 1. 数据模型概览

### 1.1 核心实体关系

```
User (用户)
  ├─ UserPreferences (用户偏好)
  ├─ DatabaseConnection (数据库连接) *
  │    └─ SlowQuery (慢查询) *
  │         └─ AnalysisSession (分析会话)
  │              ├─ AgentExecution (Agent 执行) *
  │              │    ├─ ToolCall (工具调用) *
  │              │    ├─ AgentDecision (决策记录) *
  │              │    ├─ AgentError (错误记录) *
  │              │    └─ PromptVersion (Prompt 版本)
  │              ├─ AgentMessage (Agent 消息) *
  │              ├─ AgentCheckpoint (状态快照) *
  │              └─ OptimizationReport (优化报告)
  │                   ├─ Issue (问题) *
  │                   └─ Recommendation (建议) *
  │                        └─ UserFeedback (用户反馈)
  └─ ConfigurationAnalysis (配置分析) *
       ├─ ServerResources (服务器资源)
       ├─ DatabaseStats (数据库统计)
       ├─ ConfigRecommendation (配置建议) *
       └─ ResourceBottleneck (资源瓶颈) *

KnowledgeEntry (知识库)
AgentMetrics (Agent 性能指标)
```

---

## 2. 详细数据模型

### 2.1 用户相关

#### User (用户)

```csharp
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
    
    // 导航属性
    public UserPreferences Preferences { get; set; }
    public List<DatabaseConnection> DatabaseConnections { get; set; }
    public List<AnalysisSession> AnalysisSessions { get; set; }
}
```

#### UserPreferences (用户偏好)

```csharp
public class UserPreferences
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PreferredDatabase { get; set; }      // mysql/postgresql
    public string Theme { get; set; }                  // light/dark
    public bool EnableAutoAnalysis { get; set; }
    public int SlowQueryThreshold { get; set; }        // 毫秒
    public bool EnableNotifications { get; set; }
    public Dictionary<string, object> CustomSettings { get; set; }
    
    // 导航属性
    public User User { get; set; }
}
```

### 2.2 数据库连接

#### DatabaseConnection (数据库连接)

```csharp
public class DatabaseConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; }
    public string DatabaseType { get; set; }           // mysql/postgresql
    public string Host { get; set; }
    public int Port { get; set; }
    public string Database { get; set; }
    public string Username { get; set; }
    public string PasswordEncrypted { get; set; }      // 加密存储
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public string ConnectionMode { get; set; }         // direct/agent
    
    // 导航属性
    public User User { get; set; }
    public List<SlowQuery> SlowQueries { get; set; }
    public List<ConfigurationAnalysis> ConfigurationAnalyses { get; set; }
}
```

### 2.3 慢查询

#### SlowQuery (慢查询记录)

```csharp
public class SlowQuery
{
    public Guid Id { get; set; }
    public Guid DatabaseConnectionId { get; set; }
    public string Sql { get; set; }
    public string NormalizedSql { get; set; }          // 参数化后的 SQL
    public string SqlHash { get; set; }                // SQL 指纹（用于去重）
    public double ExecutionTime { get; set; }          // 毫秒
    public DateTime ExecutedAt { get; set; }
    public string ExecutionPlan { get; set; }          // JSON
    public long RowsExamined { get; set; }
    public long RowsSent { get; set; }
    public bool IsAnalyzed { get; set; }
    public Guid? AnalysisSessionId { get; set; }
    
    // 导航属性
    public DatabaseConnection DatabaseConnection { get; set; }
    public AnalysisSession AnalysisSession { get; set; }
}
```

### 2.4 分析会话

#### AnalysisSession (分析会话)

```csharp
public class AnalysisSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? DatabaseConnectionId { get; set; }
    public Guid? SlowQueryId { get; set; }
    public string InputSql { get; set; }
    public string AnalysisType { get; set; }           // sql_analysis/config_analysis
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; }                 // running/completed/failed/cancelled
    public double? DurationMs { get; set; }
    public int TotalTokensUsed { get; set; }
    public decimal? EstimatedCost { get; set; }        // USD
    
    // 导航属性
    public User User { get; set; }
    public DatabaseConnection DatabaseConnection { get; set; }
    public SlowQuery SlowQuery { get; set; }
    public List<AgentExecution> AgentExecutions { get; set; }
    public List<AgentMessage> AgentMessages { get; set; }
    public List<AgentCheckpoint> AgentCheckpoints { get; set; }
    public OptimizationReport Report { get; set; }
}
```

### 2.5 Agent 执行

#### AgentExecution (Agent 执行记录)

```csharp
public class AgentExecution
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string AgentName { get; set; }              // SqlParser/ExecutionPlan/IndexAdvisor/ConfigurationAdvisor
    public string AgentType { get; set; }              // analyzer/advisor/coordinator
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? DurationMs { get; set; }
    public string Status { get; set; }                 // running/completed/failed
    public string Input { get; set; }                  // JSON
    public string Output { get; set; }                 // JSON
    public string ReasoningProcess { get; set; }       // AI 推理过程（Markdown）
    public int TokensUsed { get; set; }
    public Guid PromptVersionId { get; set; }
    public string PromptSnapshot { get; set; }         // Prompt 快照
    public int RetryCount { get; set; }
    
    // 导航属性
    public AnalysisSession AnalysisSession { get; set; }
    public PromptVersion PromptVersion { get; set; }
    public List<ToolCall> ToolCalls { get; set; }
    public List<AgentDecision> Decisions { get; set; }
    public List<AgentError> Errors { get; set; }
}
```

#### ToolCall (工具调用记录)

```csharp
public class ToolCall
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string ToolName { get; set; }               // GetExecutionPlan/GetTableIndexes/GetTableStats
    public string Input { get; set; }                  // JSON
    public string Output { get; set; }                 // JSON
    public DateTime CalledAt { get; set; }
    public double DurationMs { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; }
    
    // 导航属性
    public AgentExecution AgentExecution { get; set; }
}
```

#### AgentMessage (Agent 间通信)

```csharp
public class AgentMessage
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string FromAgent { get; set; }
    public string ToAgent { get; set; }
    public string MessageType { get; set; }            // request/response/notification
    public string Content { get; set; }                // JSON
    public DateTime SentAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public bool IsProcessed { get; set; }
    
    // 导航属性
    public AnalysisSession AnalysisSession { get; set; }
}
```

#### AgentDecision (Agent 决策记录)

```csharp
public class AgentDecision
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string DecisionPoint { get; set; }          // "选择工具"/"判断是否需要更多信息"/"选择优化策略"
    public List<string> Options { get; set; }          // 可选项
    public string SelectedOption { get; set; }
    public string Reasoning { get; set; }              // 为什么选这个
    public double Confidence { get; set; }             // 置信度 0-1
    public DateTime DecidedAt { get; set; }
    public Dictionary<string, object> Context { get; set; }  // 决策时的上下文
    
    // 导航属性
    public AgentExecution AgentExecution { get; set; }
}
```

#### AgentError (Agent 错误记录)

```csharp
public class AgentError
{
    public Guid Id { get; set; }
    public Guid AgentExecutionId { get; set; }
    public string ErrorType { get; set; }              // timeout/api_error/validation_error/tool_error
    public string ErrorMessage { get; set; }
    public string StackTrace { get; set; }
    public string Context { get; set; }                // 发生错误时的上下文（JSON）
    public DateTime OccurredAt { get; set; }
    public bool IsRetryable { get; set; }
    public int RetryCount { get; set; }
    public bool IsResolved { get; set; }
    public string Resolution { get; set; }
    
    // 导航属性
    public AgentExecution AgentExecution { get; set; }
}
```

#### AgentCheckpoint (Agent 状态快照)

```csharp
public class AgentCheckpoint
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string AgentName { get; set; }
    public string State { get; set; }                  // JSON 序列化的状态
    public string CheckpointType { get; set; }         // manual/auto/error
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; }
    
    // 导航属性
    public AnalysisSession AnalysisSession { get; set; }
}
```

### 2.6 Prompt 管理

#### PromptVersion (Prompt 版本)

```csharp
public class PromptVersion
{
    public Guid Id { get; set; }
    public string AgentName { get; set; }
    public int Version { get; set; }
    public string PromptTemplate { get; set; }
    public string Description { get; set; }            // 版本说明
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, object> Metadata { get; set; }  // 模型参数、temperature 等
    
    // 导航属性
    public List<AgentExecution> AgentExecutions { get; set; }
}
```

### 2.7 优化报告

#### OptimizationReport (优化报告)

```csharp
public class OptimizationReport
{
    public Guid Id { get; set; }
    public Guid AnalysisSessionId { get; set; }
    public string Summary { get; set; }                // Markdown
    public string OverallSeverity { get; set; }        // critical/high/medium/low
    public double? EstimatedImprovementPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // 导航属性
    public AnalysisSession AnalysisSession { get; set; }
    public List<Issue> Issues { get; set; }
    public List<Recommendation> Recommendations { get; set; }
}
```

#### Issue (问题)

```csharp
public class Issue
{
    public Guid Id { get; set; }
    public Guid OptimizationReportId { get; set; }
    public string Type { get; set; }                   // full_table_scan/index_missing/join_order/n_plus_one
    public string Severity { get; set; }               // critical/high/medium/low
    public string Title { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }               // SQL 中的位置
    public Dictionary<string, object> Details { get; set; }
    
    // 导航属性
    public OptimizationReport OptimizationReport { get; set; }
}
```

#### Recommendation (优化建议)

```csharp
public class Recommendation
{
    public Guid Id { get; set; }
    public Guid OptimizationReportId { get; set; }
    public string Type { get; set; }                   // create_index/rewrite_sql/adjust_config/add_cache
    public string Priority { get; set; }               // high/medium/low
    public string Title { get; set; }
    public string Description { get; set; }            // Markdown
    public string Code { get; set; }                   // DDL/SQL
    public string ExpectedImpact { get; set; }         // "提升 30-50% 查询性能"
    public string Risk { get; set; }                   // "需要重启数据库"
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string AppliedBy { get; set; }
    
    // 导航属性
    public OptimizationReport OptimizationReport { get; set; }
    public List<UserFeedback> Feedbacks { get; set; }
}
```

### 2.8 配置分析

#### ConfigurationAnalysis (配置分析记录)

```csharp
public class ConfigurationAnalysis
{
    public Guid Id { get; set; }
    public Guid DatabaseConnectionId { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public Dictionary<string, string> CurrentConfig { get; set; }
    public string ServerResourcesJson { get; set; }    // JSON
    public string DatabaseStatsJson { get; set; }      // JSON
    public string OverallHealth { get; set; }          // excellent/good/fair/poor
    
    // 导航属性
    public DatabaseConnection DatabaseConnection { get; set; }
    public List<ConfigRecommendation> Recommendations { get; set; }
    public List<ResourceBottleneck> Bottlenecks { get; set; }
}
```

#### ConfigRecommendation (配置建议)

```csharp
public class ConfigRecommendation
{
    public Guid Id { get; set; }
    public Guid ConfigurationAnalysisId { get; set; }
    public string Parameter { get; set; }
    public string CurrentValue { get; set; }
    public string RecommendedValue { get; set; }
    public string Reason { get; set; }
    public string Priority { get; set; }               // high/medium/low
    public string Impact { get; set; }
    public string Risk { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    
    // 导航属性
    public ConfigurationAnalysis ConfigurationAnalysis { get; set; }
}
```

#### ResourceBottleneck (资源瓶颈)

```csharp
public class ResourceBottleneck
{
    public Guid Id { get; set; }
    public Guid ConfigurationAnalysisId { get; set; }
    public string Resource { get; set; }               // cpu/memory/disk/network
    public string Severity { get; set; }               // critical/high/medium/low
    public string Description { get; set; }
    public string Recommendation { get; set; }
    public Dictionary<string, object> Metrics { get; set; }
    
    // 导航属性
    public ConfigurationAnalysis ConfigurationAnalysis { get; set; }
}
```

### 2.9 知识库

#### KnowledgeEntry (知识库条目)

```csharp
public class KnowledgeEntry
{
    public Guid Id { get; set; }
    public string Type { get; set; }                   // optimization_case/best_practice/documentation
    public string Title { get; set; }
    public string Content { get; set; }                // Markdown
    public string[] Tags { get; set; }
    public string DatabaseType { get; set; }           // mysql/postgresql/all
    public float[] Embedding { get; set; }             // pgvector
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UsageCount { get; set; }
    public double AverageRating { get; set; }
    public string Source { get; set; }                 // user/system/import
}
```

### 2.10 用户反馈

#### UserFeedback (用户反馈)

```csharp
public class UserFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? RecommendationId { get; set; }
    public string FeedbackType { get; set; }           // helpful/not_helpful/applied/rejected
    public int? Rating { get; set; }                   // 1-5
    public string Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // 导航属性
    public User User { get; set; }
    public Recommendation Recommendation { get; set; }
}
```

### 2.11 Agent 性能指标

#### AgentMetrics (Agent 性能指标)

```csharp
public class AgentMetrics
{
    public Guid Id { get; set; }
    public string AgentName { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string PeriodType { get; set; }             // hourly/daily/weekly/monthly
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public double AverageDurationMs { get; set; }
    public double P50DurationMs { get; set; }
    public double P95DurationMs { get; set; }
    public double P99DurationMs { get; set; }
    public double AverageTokensUsed { get; set; }
    public double SuccessRate { get; set; }
    public decimal TotalCost { get; set; }             // USD
}
```

---

## 3. 索引设计

### 3.1 核心索引

```sql
-- SlowQuery
CREATE INDEX idx_slow_query_connection ON slow_queries(database_connection_id, executed_at DESC);
CREATE INDEX idx_slow_query_hash ON slow_queries(sql_hash, database_connection_id);
CREATE INDEX idx_slow_query_analyzed ON slow_queries(is_analyzed, executed_at DESC);

-- AnalysisSession
CREATE INDEX idx_analysis_session_user ON analysis_sessions(user_id, started_at DESC);
CREATE INDEX idx_analysis_session_status ON analysis_sessions(status, started_at DESC);

-- AgentExecution
CREATE INDEX idx_agent_execution_session ON agent_executions(analysis_session_id, started_at);
CREATE INDEX idx_agent_execution_agent ON agent_executions(agent_name, started_at DESC);

-- ToolCall
CREATE INDEX idx_tool_call_agent ON tool_calls(agent_execution_id, called_at);

-- KnowledgeEntry (pgvector)
CREATE INDEX idx_knowledge_embedding ON knowledge_entries USING ivfflat (embedding vector_cosine_ops);
CREATE INDEX idx_knowledge_tags ON knowledge_entries USING GIN (tags);

-- AgentMetrics
CREATE INDEX idx_agent_metrics_agent ON agent_metrics(agent_name, period_start DESC);
```

---

## 4. 数据保留策略

| 数据类型 | 保留期限 | 清理策略 |
|---------|---------|---------|
| 慢查询记录 | 30 天 | 自动归档或删除 |
| 分析会话 | 90 天 | 用户可手动删除 |
| Agent 执行记录 | 30 天 | 与会话同步清理 |
| Agent 消息/决策/错误 | 30 天 | 与执行记录同步清理 |
| Prompt 版本 | 永久 | 不活跃版本可归档 |
| Agent 性能指标（天） | 90 天 | 聚合到月后删除 |
| Agent 性能指标（月） | 永久 | - |
| 知识库 | 永久 | 定期清理低质量条目 |
| 用户反馈 | 永久 | - |
| 配置分析 | 90 天 | 保留最新 10 条 |

---

## 5. 数据迁移策略

### 5.1 版本管理

使用 EF Core Migrations：
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 5.2 数据备份

**每日备份**：
- PostgreSQL: `pg_dump`
- 保留 7 天

**每周备份**：
- 完整备份
- 保留 4 周

**每月备份**：
- 完整备份
- 永久保留

---

## 6. 安全设计

### 6.1 敏感数据加密

**数据库连接密码**：
- 使用 AES-256 加密
- 密钥存储在 Azure Key Vault / AWS Secrets Manager

**用户密码**：
- 使用 bcrypt 哈希
- Salt rounds: 12

### 6.2 数据访问控制

**行级安全**：
- 用户只能访问自己的数据
- 通过 EF Core 全局查询过滤器实现

```csharp
modelBuilder.Entity<AnalysisSession>()
    .HasQueryFilter(s => s.UserId == _currentUserId);
```

### 6.3 审计日志

**记录操作**：
- 数据库连接的创建/修改/删除
- 配置建议的应用
- 敏感数据的访问

```csharp
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; }
    public string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Changes { get; set; }  // JSON
    public DateTime CreatedAt { get; set; }
    public string IpAddress { get; set; }
}
```

---

## 7. 性能优化

### 7.1 查询优化

**分页查询**：
```csharp
var slowQueries = await _context.SlowQueries
    .Where(q => q.DatabaseConnectionId == connectionId)
    .OrderByDescending(q => q.ExecutedAt)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

**预加载关联数据**：
```csharp
var session = await _context.AnalysisSessions
    .Include(s => s.AgentExecutions)
        .ThenInclude(e => e.ToolCalls)
    .Include(s => s.Report)
        .ThenInclude(r => r.Recommendations)
    .FirstOrDefaultAsync(s => s.Id == sessionId);
```

### 7.2 缓存策略

**Redis 缓存**：
- 数据库 schema：1 小时
- 执行计划：30 分钟
- 知识库检索结果：10 分钟

```csharp
var cacheKey = $"schema:{connectionId}";
var schema = await _cache.GetOrCreateAsync(cacheKey, async entry =>
{
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
    return await _adapter.GetSchemaAsync();
});
```

### 7.3 批量操作

**批量插入**：
```csharp
await _context.SlowQueries.AddRangeAsync(slowQueries);
await _context.SaveChangesAsync();
```

**批量更新**：
```csharp
await _context.SlowQueries
    .Where(q => q.DatabaseConnectionId == connectionId)
    .ExecuteUpdateAsync(s => s.SetProperty(q => q.IsAnalyzed, true));
```

