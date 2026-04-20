# MAF Workflow 节点分析报告

> **日期**: 2026-04-20
> **目的**: 分析项目中哪些节点应该使用 Agent，以及当前的实现情况

---

## 当前 Workflow 节点构成

### SQL Analysis Workflow

```
validation → parser → plan → indexAdvisor → sqlRewrite → coordinator → reviewGate → reviewPort → reviewDecision
```

**节点列表**:
1. `SqlInputValidationExecutor` - 输入验证
2. `SqlParserMafExecutor` - SQL 解析
3. `ExecutionPlanMafExecutor` - 执行计划获取
4. `IndexAdvisorMafExecutor` - 索引推荐
5. `SqlRewriteMafExecutor` - SQL 重写建议
6. `SqlCoordinatorMafExecutor` - 结果汇总
7. `SqlHumanReviewGateExecutor` - 人工审核门控
8. `SqlHumanReviewDecisionExecutor` - 审核决策处理

### DB Config Workflow

```
validation → collector → analyzer → coordinator → reviewGate → reviewPort → reviewDecision
```

**节点列表**:
1. `DbConfigInputValidationExecutor` - 输入验证
2. `ConfigCollectorMafExecutor` - 配置收集
3. `ConfigAnalyzerMafExecutor` - 配置分析
4. `ConfigCoordinatorMafExecutor` - 结果汇总
5. `ConfigHumanReviewGateExecutor` - 人工审核门控
6. `ConfigHumanReviewDecisionExecutor` - 审核决策处理

---

## 节点类型分析

### 当前实现：全部使用 Executor

**所有节点都是 `Executor<TInput, TOutput>`**，没有使用 Agent。

### 应该使用 Agent 的节点

根据 MAF 最佳实践和项目设计文档，以下节点**应该使用 Agent**（需要 LLM 推理）：

#### SQL Analysis Workflow

1. **IndexAdvisorMafExecutor** → **应该改为 IndexAdvisorAgent**
   - **原因**: 索引推荐需要 LLM 分析执行计划、理解查询模式、推理最优索引策略
   - **当前问题**: 使用 Executor + 规则引擎，无法处理复杂场景
   - **应该做**: 
     - 使用 Agent 调用 LLM
     - Prompt: "分析执行计划，推荐索引，考虑查询模式、数据分布、索引成本"
     - Tools: `GetTableSchema`, `GetExistingIndexes`, `EstimateIndexCost`

2. **SqlRewriteMafExecutor** → **应该改为 SqlRewriteAgent**
   - **原因**: SQL 重写需要 LLM 理解查询语义、优化逻辑、生成等价 SQL
   - **当前问题**: 使用 Executor + 规则引擎，只能做简单重写
   - **应该做**:
     - 使用 Agent 调用 LLM
     - Prompt: "分析 SQL 查询，提供优化建议，生成重写后的 SQL"
     - Tools: `ValidateSqlSyntax`, `EstimateQueryCost`, `ComparePerformance`

#### DB Config Workflow

3. **ConfigAnalyzerMafExecutor** → **应该改为 ConfigAnalyzerAgent**
   - **原因**: 配置分析需要 LLM 理解配置项含义、推理最优配置、考虑业务场景
   - **当前问题**: 使用 Executor + 规则引擎，无法处理复杂配置组合
   - **应该做**:
     - 使用 Agent 调用 LLM
     - Prompt: "分析数据库配置，推荐优化方案，考虑工作负载特征"
     - Tools: `GetConfigMetadata`, `GetWorkloadProfile`, `EstimateConfigImpact`

### 应该保持 Executor 的节点

以下节点**应该保持 Executor**（确定性逻辑，不需要 LLM）：

1. **SqlInputValidationExecutor** - 输入验证（确定性）
2. **SqlParserMafExecutor** - SQL 解析（确定性，使用 SQL Parser）
3. **ExecutionPlanMafExecutor** - 执行计划获取（确定性，调用数据库 EXPLAIN）
4. **SqlCoordinatorMafExecutor** - 结果汇总（确定性，数据聚合）
5. **SqlHumanReviewGateExecutor** - 人工审核门控（确定性，流程控制）
6. **SqlHumanReviewDecisionExecutor** - 审核决策处理（确定性，流程控制）
7. **DbConfigInputValidationExecutor** - 输入验证（确定性）
8. **ConfigCollectorMafExecutor** - 配置收集（确定性，调用 MCP）
9. **ConfigCoordinatorMafExecutor** - 结果汇总（确定性，数据聚合）
10. **ConfigHumanReviewGateExecutor** - 人工审核门控（确定性，流程控制）
11. **ConfigHumanReviewDecisionExecutor** - 审核决策处理（确定性，流程控制）

---

## 问题分析

### 当前问题

1. **IndexAdvisorMafExecutor 使用 Executor**
   - 只能做规则式索引推荐
   - 无法理解复杂查询模式
   - 无法考虑业务上下文
   - 推荐质量有限

2. **SqlRewriteMafExecutor 使用 Executor**
   - 只能做简单的 SQL 重写
   - 无法理解查询语义
   - 无法生成复杂的优化 SQL
   - 优化效果有限

3. **ConfigAnalyzerMafExecutor 使用 Executor**
   - 只能做规则式配置分析
   - 无法理解配置项之间的关联
   - 无法考虑工作负载特征
   - 推荐质量有限

### 设计意图 vs 实际实现

**设计意图**（根据 AGENTS.md）:
- 项目设计上**要使用 Agent**
- Agent 负责需要 LLM 推理的节点
- Executor 负责确定性逻辑的节点

**实际实现**:
- **所有节点都使用 Executor**
- **没有任何 Agent**
- 失去了 LLM 推理能力

---

## 改进方案

### 方案 1: 将关键节点改为 Agent（推荐）

#### 1. IndexAdvisorAgent

```csharp
// 创建 Agent
var indexAdvisorAgent = chatClient.CreateAIAgent(
    name: "IndexAdvisor",
    instructions: @"
        你是一个数据库索引优化专家。
        
        任务：
        1. 分析执行计划，识别性能瓶颈
        2. 理解查询模式和数据访问特征
        3. 推荐最优索引策略
        4. 考虑索引成本和收益
        
        输出格式：
        - 索引名称
        - 索引列
        - 索引类型（B-Tree, Hash, GIN, etc.）
        - 预期性能提升
        - 置信度（0.0-1.0）
        - 推理依据
    ",
    tools: new[]
    {
        AIFunctionFactory.Create(GetTableSchemaAsync),
        AIFunctionFactory.Create(GetExistingIndexesAsync),
        AIFunctionFactory.Create(EstimateIndexCostAsync)
    }
);

// 在 Workflow 中使用
var indexAdvisorBinding = new AgentExecutorBinding(
    "IndexAdvisor",
    indexAdvisorAgent,
    typeof(ExecutionPlanCompletedMessage),
    typeof(IndexRecommendationCompletedMessage)
);

builder.AddEdge(plan, indexAdvisorBinding);
```

#### 2. SqlRewriteAgent

```csharp
// 创建 Agent
var sqlRewriteAgent = chatClient.CreateAIAgent(
    name: "SqlRewrite",
    instructions: @"
        你是一个 SQL 优化专家。
        
        任务：
        1. 分析 SQL 查询，理解查询语义
        2. 识别性能问题（如 N+1、笛卡尔积、子查询）
        3. 生成优化后的等价 SQL
        4. 验证语义等价性
        
        输出格式：
        - 原始 SQL
        - 优化后 SQL
        - 优化类型（如 JOIN 优化、子查询消除）
        - 预期性能提升
        - 置信度（0.0-1.0）
        - 推理依据
    ",
    tools: new[]
    {
        AIFunctionFactory.Create(ValidateSqlSyntaxAsync),
        AIFunctionFactory.Create(EstimateQueryCostAsync),
        AIFunctionFactory.Create(ComparePerformanceAsync)
    }
);

// 在 Workflow 中使用
var sqlRewriteBinding = new AgentExecutorBinding(
    "SqlRewrite",
    sqlRewriteAgent,
    typeof(IndexRecommendationCompletedMessage),
    typeof(SqlRewriteCompletedMessage)
);

builder.AddEdge(indexAdvisor, sqlRewriteBinding);
```

#### 3. ConfigAnalyzerAgent

```csharp
// 创建 Agent
var configAnalyzerAgent = chatClient.CreateAIAgent(
    name: "ConfigAnalyzer",
    instructions: @"
        你是一个数据库配置优化专家。
        
        任务：
        1. 分析数据库配置快照
        2. 理解工作负载特征（OLTP/OLAP/混合）
        3. 推荐最优配置参数
        4. 考虑配置项之间的关联
        
        输出格式：
        - 配置项名称
        - 当前值
        - 推荐值
        - 优化原因
        - 预期效果
        - 置信度（0.0-1.0）
        - 推理依据
    ",
    tools: new[]
    {
        AIFunctionFactory.Create(GetConfigMetadataAsync),
        AIFunctionFactory.Create(GetWorkloadProfileAsync),
        AIFunctionFactory.Create(EstimateConfigImpactAsync)
    }
);

// 在 Workflow 中使用
var configAnalyzerBinding = new AgentExecutorBinding(
    "ConfigAnalyzer",
    configAnalyzerAgent,
    typeof(ConfigSnapshotCollectedMessage),
    typeof(ConfigRecommendationsGeneratedMessage)
);

builder.AddEdge(collector, configAnalyzerBinding);
```

### 方案 2: 混合模式（Agent + Executor）

保留现有 Executor 作为 fallback，Agent 作为增强：

```csharp
// 1. 先用 Agent 生成推荐
var agentRecommendations = await indexAdvisorAgent.RunAsync(input);

// 2. 用 Executor 验证和补充
var executorRecommendations = await indexAdvisorExecutor.ExecuteAsync(input);

// 3. 合并结果
var finalRecommendations = MergeRecommendations(
    agentRecommendations, 
    executorRecommendations
);
```

---

## 数据持久化需求

### Agent 相关持久化

如果使用 Agent，需要持久化：

1. **AgentSession** - Agent 会话状态
   ```csharp
   // 使用框架的序列化方法
   var sessionDict = session.ToDict();
   var sessionJson = JsonSerializer.Serialize(sessionDict);
   await _db.AgentSessions.AddAsync(new AgentSessionEntity
   {
       SessionId = session.SessionId,
       AgentName = "IndexAdvisor",
       StateJson = sessionJson,
       TotalTokens = session.TotalTokens
   });
   ```

2. **Agent 执行记录** - 每次 Agent 调用
   ```csharp
   await _db.AgentExecutions.AddAsync(new AgentExecutionRecord
   {
       ExecutionId = Guid.NewGuid(),
       SessionId = sessionId,
       AgentName = "IndexAdvisor",
       PromptVersionId = promptVersionId,
       Input = input,
       Output = output,
       InputTokens = usage.InputTokens,
       OutputTokens = usage.OutputTokens,
       TotalTokens = usage.TotalTokens,
       StartedAt = startTime,
       CompletedAt = DateTime.UtcNow,
       Status = "success"
   });
   ```

3. **决策记录** - Agent 的推理过程
   ```csharp
   await _db.DecisionRecords.AddAsync(new DecisionRecord
   {
       DecisionId = Guid.NewGuid(),
       SessionId = sessionId,
       AgentName = "IndexAdvisor",
       DecisionType = "index_recommendation",
       Question = "应该创建哪些索引？",
       Answer = "推荐创建 idx_users_email",
       Reasoning = "执行计划显示全表扫描，email 列经常用于查询",
       Confidence = 0.85,
       Evidences = evidences
   });
   ```

### Checkpoint 需要包含 Agent 会话

```csharp
// 保存 Checkpoint 时包含所有 Agent 会话
var checkpoint = new WorkflowCheckpointData
{
    CheckpointId = Guid.NewGuid().ToString(),
    WorkflowId = workflow.Id,
    SessionId = sessionId,
    SharedState = workflowState.SharedState,
    AgentSessions = new List<AgentSessionState>
    {
        await indexAdvisorAgent.GetSessionAsync(sessionId),
        await sqlRewriteAgent.GetSessionAsync(sessionId),
        await configAnalyzerAgent.GetSessionAsync(sessionId)
    },
    CurrentExecutorId = workflowState.CurrentExecutorId,
    CreatedAt = DateTime.UtcNow
};
```

---

## 实施建议

### 优先级

1. **P0 - IndexAdvisorAgent** - 最关键，索引推荐是核心功能
2. **P1 - SqlRewriteAgent** - 重要，SQL 优化是核心功能
3. **P2 - ConfigAnalyzerAgent** - 次要，配置优化可以先用规则引擎

### 实施步骤

1. **创建 Agent 定义**
   - 编写 Prompt（Instructions）
   - 定义 Tools（Function Calling）
   - 配置 Context Provider（如果需要）

2. **修改 MafWorkflowFactory**
   - 将 Executor 改为 Agent
   - 使用 `AgentExecutorBinding` 或类似机制

3. **实现数据持久化**
   - AgentSession 持久化
   - Agent 执行记录
   - 决策记录

4. **更新 Checkpoint 逻辑**
   - 保存所有 Agent 会话
   - 恢复时重建 Agent 会话

5. **测试和验证**
   - 功能测试：Agent 是否正确推理
   - 性能测试：Token 使用、响应时间
   - 成本测试：LLM 调用成本

---

## 总结

### 当前状态

- ❌ **所有节点都使用 Executor**
- ❌ **没有任何 Agent**
- ❌ **失去了 LLM 推理能力**

### 应该的状态

- ✅ **IndexAdvisor 使用 Agent**（需要 LLM 推理）
- ✅ **SqlRewrite 使用 Agent**（需要 LLM 推理）
- ✅ **ConfigAnalyzer 使用 Agent**（需要 LLM 推理）
- ✅ **其他节点使用 Executor**（确定性逻辑）

### 关键差距

项目设计上**要使用 Agent**，但实际实现**没有使用 Agent**，这是一个重大的架构偏差。

### 下一步行动

1. 确认是否要实施 Agent（与团队讨论）
2. 如果实施，按优先级逐步改造
3. 更新 MAF-BEST-PRACTICES.md，增加 Agent 使用指南
4. 更新 MAF-CODE-REVIEW.md，标记需要改为 Agent 的节点
