# MAF Workflow 详细设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [Workflow 概述](#1-workflow-概述)
2. [SQL 分析 Workflow](#2-sql-分析-workflow)
3. [数据库配置优化 Workflow](#3-数据库配置优化-workflow)
4. [Executor 接口设计](#4-executor-接口设计)
5. [数据传递与上下文](#5-数据传递与上下文)
6. [错误处理与重试](#6-错误处理与重试)

---

## 1. Workflow 概述

### 1.1 Workflow 定义

Workflow 是由多个 Executor 组成的有向无环图（DAG），每个 Executor 负责一个独立的任务。

**核心特性**：
- **顺序执行**：Executor 按依赖关系顺序执行
- **上下文共享**：通过 `WorkflowContext` 传递数据
- **Checkpoint 支持**：每个 Executor 执行后保存状态
- **Human-in-the-loop**：支持人工审核节点

### 1.2 Workflow 类型

| Workflow | 用途 | Executors |
|----------|------|-----------|
| **SqlAnalysisWorkflow** | SQL 层调优 | SqlParser → ExecutionPlan → IndexAdvisor → Coordinator → HumanReview → Regeneration |
| **DbConfigOptimizationWorkflow** | 实例层调优 | ConfigCollector → ConfigAnalyzer → Coordinator → HumanReview → Regeneration |

---

## 2. SQL 分析 Workflow

### 2.1 Workflow 流程图

```
┌─────────────┐
│ SqlParser   │  解析 SQL，提取表、字段、JOIN 条件
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Execution   │  获取执行计划，分析性能瓶颈
│ Plan        │
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Index       │  推荐索引，预估收益
│ Advisor     │
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Coordinator │  汇总建议，生成最终报告
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Human       │  等待人工审核
│ Review      │
│ Executor    │
└──────┬──────┘
       │
       ├─ approve ──→ [完成]
       │
       └─ reject ───→ ┌─────────────┐
                      │ Regeneration│  根据反馈重新生成
                      │ Executor    │
                      └──────┬──────┘
                             │
                             └──→ [回到 Coordinator]
```

### 2.2 Executor 详细设计

#### 2.2.1 SqlParserExecutor

**职责**：解析 SQL，提取结构化信息

**输入**：
```csharp
public class SqlParserInput
{
    public string SqlText { get; set; }
    public string DatabaseType { get; set; }  // "mysql" / "postgresql"
}
```

**输出**：
```csharp
public class SqlParserOutput
{
    public List<string> Tables { get; set; }
    public List<string> Columns { get; set; }
    public List<JoinCondition> Joins { get; set; }
    public List<WhereCondition> WhereConditions { get; set; }
    public string QueryType { get; set; }  // "SELECT" / "UPDATE" / "DELETE"
}
```

**实现**：
```csharp
public class SqlParserExecutor : IExecutor
{
    private readonly ISqlParserAgent _agent;
    
    public async Task<ExecutorResult> ExecuteAsync(WorkflowContext context)
    {
        var input = context.GetInput<SqlParserInput>();
        
        // 调用 Agent 解析 SQL
        var result = await _agent.ParseSqlAsync(input.SqlText, input.DatabaseType);
        
        // 保存到上下文
        context.Set("ParsedSql", result);
        
        return ExecutorResult.Success();
    }
}
```

#### 2.2.2 ExecutionPlanExecutor

**职责**：获取执行计划，分析性能瓶颈

**输入**：
```csharp
public class ExecutionPlanInput
{
    public string SqlText { get; set; }
    public string ConnectionString { get; set; }
}
```

**输出**：
```csharp
public class ExecutionPlanOutput
{
    public string RawPlan { get; set; }  // JSON 格式的执行计划
    public List<PerformanceIssue> Issues { get; set; }
    public Dictionary<string, object> Metrics { get; set; }  // cost, rows, time
}

public class PerformanceIssue
{
    public string Type { get; set; }  // "FullTableScan" / "IndexNotUsed" / "BadJoinOrder"
    public string Description { get; set; }
    public string TableName { get; set; }
    public double ImpactScore { get; set; }  // 0-100
}
```

**实现**：
```csharp
public class ExecutionPlanExecutor : IExecutor
{
    private readonly IMcpClient _mcpClient;
    private readonly IExecutionPlanAgent _agent;
    
    public async Task<ExecutorResult> ExecuteAsync(WorkflowContext context)
    {
        var input = context.GetInput<ExecutionPlanInput>();
        
        // 通过 MCP 获取执行计划
        var rawPlan = await _mcpClient.GetExecutionPlanAsync(input.SqlText);
        
        // 调用 Agent 分析执行计划
        var analysis = await _agent.AnalyzePlanAsync(rawPlan);
        
        context.Set("ExecutionPlan", new ExecutionPlanOutput
        {
            RawPlan = rawPlan,
            Issues = analysis.Issues,
            Metrics = analysis.Metrics
        });
        
        return ExecutorResult.Success();
    }
}
```

#### 2.2.3 IndexAdvisorExecutor

**职责**：推荐索引，预估收益

**输入**：
```csharp
public class IndexAdvisorInput
{
    public SqlParserOutput ParsedSql { get; set; }
    public ExecutionPlanOutput ExecutionPlan { get; set; }
}
```

**输出**：
```csharp
public class IndexAdvisorOutput
{
    public List<IndexRecommendation> Recommendations { get; set; }
}

public class IndexRecommendation
{
    public string TableName { get; set; }
    public List<string> Columns { get; set; }
    public string IndexType { get; set; }  // "BTREE" / "HASH"
    public string CreateDdl { get; set; }
    public double EstimatedBenefit { get; set; }  // 预估性能提升百分比
    public string Reasoning { get; set; }
    public List<string> EvidenceRefs { get; set; }  // 引用执行计划中的证据
}
```

#### 2.2.4 CoordinatorExecutor

**职责**：汇总所有 Executor 的结果，生成最终报告

**输出**：
```csharp
public class OptimizationReport
{
    public string Summary { get; set; }
    public List<IndexRecommendation> IndexRecommendations { get; set; }
    public List<SqlRewriteSuggestion> SqlRewriteSuggestions { get; set; }
    public double OverallConfidence { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

#### 2.2.5 HumanReviewExecutor

**职责**：等待人工审核，处理审核结果

**行为**：
- 将 Workflow 状态设置为 `WaitingForReview`
- 保存 Checkpoint
- 等待用户提交审核结果（approve / reject / adjust）

**审核结果处理**：
```csharp
public class ReviewResult
{
    public string Action { get; set; }  // "approve" / "reject" / "adjust"
    public string Comment { get; set; }
    public Dictionary<string, object> Adjustments { get; set; }  // 用户调整的参数
}
```

#### 2.2.6 RegenerationExecutor

**职责**：根据审核反馈重新生成建议

**输入**：
```csharp
public class RegenerationInput
{
    public OptimizationReport OriginalReport { get; set; }
    public ReviewResult ReviewFeedback { get; set; }
}
```

**行为**：
- 根据用户反馈调整 Prompt
- 重新调用 CoordinatorExecutor
- 最多重试 3 次

---

## 3. 数据库配置优化 Workflow

### 3.1 Workflow 流程图

```
┌─────────────┐
│ Config      │  收集数据库配置参数
│ Collector   │
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Config      │  分析配置，推荐优化
│ Analyzer    │
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Coordinator │  生成最终报告
│ Executor    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Human       │  等待人工审核
│ Review      │
│ Executor    │
└─────────────┘
```

### 3.2 Executor 详细设计

#### 3.2.1 ConfigCollectorExecutor

**职责**：收集数据库配置参数

**输出**：
```csharp
public class DbConfigSnapshot
{
    public Dictionary<string, string> Parameters { get; set; }
    public Dictionary<string, object> SystemMetrics { get; set; }  // CPU, Memory, Disk
    public DateTime CollectedAt { get; set; }
}
```

#### 3.2.2 ConfigAnalyzerExecutor

**职责**：分析配置，推荐优化

**输出**：
```csharp
public class ConfigRecommendation
{
    public string ParameterName { get; set; }
    public string CurrentValue { get; set; }
    public string RecommendedValue { get; set; }
    public string Reasoning { get; set; }
    public double Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; }
}
```

---

## 4. Executor 接口设计

### 4.1 IExecutor 接口

```csharp
public interface IExecutor
{
    string Name { get; }
    Task<ExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default);
}

public class ExecutorResult
{
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    
    public static ExecutorResult Success() => new() { IsSuccess = true };
    public static ExecutorResult Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
```

### 4.2 WorkflowContext

```csharp
public class WorkflowContext
{
    private readonly Dictionary<string, object> _data = new();
    
    public string SessionId { get; set; }
    public string WorkflowType { get; set; }
    public CancellationToken CancellationToken { get; set; }
    
    public void Set<T>(string key, T value) => _data[key] = value;
    public T Get<T>(string key) => (T)_data[key];
    public bool TryGet<T>(string key, out T value)
    {
        if (_data.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
    
    public T GetInput<T>() => Get<T>("Input");
    public void SetInput<T>(T input) => Set("Input", input);
}
```

---

## 5. 数据传递与上下文

### 5.1 上下文数据流

```
SqlParserExecutor
  ↓ context.Set("ParsedSql", result)
ExecutionPlanExecutor
  ↓ context.Set("ExecutionPlan", result)
IndexAdvisorExecutor
  ↓ context.Set("IndexRecommendations", result)
CoordinatorExecutor
  ↓ context.Set("FinalReport", result)
HumanReviewExecutor
  ↓ context.Set("ReviewResult", result)
RegenerationExecutor
```

### 5.2 Checkpoint 序列化

```csharp
public class WorkflowCheckpoint
{
    public string SessionId { get; set; }
    public string WorkflowType { get; set; }
    public WorkflowStatus Status { get; set; }
    public string CurrentExecutor { get; set; }
    public Dictionary<string, JsonElement> Context { get; set; }  // 序列化后的上下文
    public List<string> CompletedExecutors { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

## 6. 错误处理与重试

### 6.1 错误分类

| 错误类型 | 处理策略 |
|---------|---------|
| **MCP 超时** | 重试 3 次，间隔 2s / 5s / 10s |
| **AI 调用失败** | 重试 2 次，记录错误 |
| **数据库连接失败** | 立即失败，通知用户 |
| **解析错误** | 立即失败，返回错误详情 |

### 6.2 重试策略

```csharp
public class RetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);
    public double BackoffMultiplier { get; set; } = 2.0;
    
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        var delay = InitialDelay;
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (i < MaxRetries - 1)
            {
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * BackoffMultiplier);
            }
        }
        throw new Exception("Max retries exceeded");
    }
}
```

---

## 7. 与其他文档的映射关系

- **系统架构**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **数据模型**：[DATA_MODEL.md](./DATA_MODEL.md)
- **MCP 集成**：[MCP_INTEGRATION.md](./MCP_INTEGRATION.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
