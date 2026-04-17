# Implementation Task Checklist

## 使用方式

一次只执行一个 `TASK-*`。每个任务都必须回答：

1. 新增什么类。
2. 类里有什么方法。
3. 每个方法入参/出参是什么。
4. 修改哪些文件。
5. 如何验证。

---

## EPIC A. 契约与数据库基础

### TASK-A1 统一结果壳与 API DTO

状态：
- `已完成（2026-04-17）`

实际实现：
- 新增 `src/DbOptimizer.Core/Models/WorkflowResultEnvelope.cs`
- 新增 `src/DbOptimizer.Infrastructure/Workflows/Serialization/WorkflowResultSerializer.cs`
- 已更新 `WorkflowApi / ReviewApi / DashboardAndHistoryApi / api.ts / App.vue`
- 已完成前后端构建校验与 review 收口

目标：

- 把 `OptimizationReport` / `ConfigOptimizationReport` 统一投影为 `WorkflowResultEnvelope`
- 把路由文件中的 DTO 拆出到 `Contracts/`

新增类：

1. `src/DbOptimizer.API/Contracts/Common/WorkflowResultEnvelope.cs`
   - `public sealed record WorkflowResultEnvelope(string ResultType, string DisplayName, string Summary, JsonElement Data, IReadOnlyDictionary<string, JsonElement>? Metadata);`
2. `src/DbOptimizer.Infrastructure/Workflows/Serialization/WorkflowResultSerializer.cs`
   - `WorkflowResultEnvelope ToEnvelope(OptimizationReport report, string databaseId, string databaseType)`
   - `WorkflowResultEnvelope ToEnvelope(ConfigOptimizationReport report)`
   - `JsonElement ToJsonElement<T>(T value)`

修改文件：

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Api/ReviewApi.cs`
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.Web/src/api.ts`

实现要点：

- 所有 `result` / `recommendations` 字段统一换成 `WorkflowResultEnvelope`
- `resultType` 必须作为唯一判别字段

验证：

1. `dotnet build` 通过
2. `api.ts` 类型无 `OptimizationReport` 直出 API 的用法

### TASK-A2 扩展 `workflow_sessions` 与 `slow_queries` 字段

目标：

- 为 MAF 运行态和 slow query 追踪准备字段

修改文件：

- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`
- 新 migration 文件

字段：

- `workflow_sessions.engine_type`
- `workflow_sessions.engine_run_id`
- `workflow_sessions.engine_checkpoint_ref`
- `workflow_sessions.engine_state`
- `workflow_sessions.result_type`
- `workflow_sessions.source_type`
- `workflow_sessions.source_ref_id`
- `slow_queries.latest_analysis_session_id`

新增类：

1. `src/DbOptimizer.Infrastructure/Persistence/Migrations/*`

验证：

1. migration 生成成功
2. `DbContext` 模型与数据库字段一致

---

## EPIC B. MAF Runtime 基础设施

### TASK-B1 引入 MAF 包与运行时注册

目标：

- 在 infrastructure 中引入 `Microsoft.Agents.AI.Workflows`
- 建立 runtime/factory/state store 接口

新增类：

1. `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntimeOptions.cs`
   - 配置属性：`CheckpointFlushEnabled` `MaxConcurrentRuns`
2. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafWorkflowRuntime.cs`
   - `Task<WorkflowStartResponse> StartSqlAnalysisAsync(SqlAnalysisWorkflowCommand command, CancellationToken cancellationToken = default)`
   - `Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(DbConfigWorkflowCommand command, CancellationToken cancellationToken = default)`
   - `Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)`
   - `Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)`
3. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafWorkflowFactory.cs`
   - `Workflow BuildSqlAnalysisWorkflow()`
   - `Workflow BuildDbConfigWorkflow()`
4. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs`
   - `Task SaveAsync(MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default)`
   - `Task<MafCheckpointEnvelope?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)`
   - `Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)`

修改文件：

- `src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`
- `src/DbOptimizer.API/Program.cs`

验证：

1. 包可还原
2. `Program.cs` 能注册 runtime、factory、state store

### TASK-B2 新建 workflow application service

目标：

- 让 API 通过新 application service 访问 MAF runtime

新增类：

1. `src/DbOptimizer.Infrastructure/Workflows/Application/IWorkflowApplicationService.cs`
   - `StartSqlAnalysisAsync`
   - `StartDbConfigOptimizationAsync`
   - `GetAsync`
   - `ResumeAsync`
   - `CancelAsync`
2. `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowApplicationService.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowRequestValidator.cs`
   - `Validate(CreateSqlAnalysisWorkflowRequest request)`
   - `Validate(CreateDbConfigOptimizationWorkflowRequest request)`

修改文件：

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Program.cs`

验证：

1. `POST /api/workflows/sql-analysis` 仍可创建 session
2. `GET /api/workflows/{sessionId}` 走新查询模型

---

## EPIC C. SQL Workflow 迁移

### TASK-C1 新建 SQL workflow 消息与 executor

目标：

- 用 MAF executor 重建 SQL workflow

新增文件：

- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/SqlAnalysisWorkflowMessages.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlInputValidationExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlParserMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/ExecutionPlanMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/IndexAdvisorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlRewriteMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlCoordinatorMafExecutor.cs`

关键方法：

- `HandleAsync(SqlAnalysisWorkflowCommand, IWorkflowContext, CancellationToken)`
- `HandleAsync(SqlParsingCompletedMessage, IWorkflowContext, CancellationToken)`
- `HandleAsync(ExecutionPlanCompletedMessage, IWorkflowContext, CancellationToken)`
- `HandleAsync(IndexRecommendationCompletedMessage, IWorkflowContext, CancellationToken)`

依赖：

- 复用现有 `ISqlParser`、`IExecutionPlanProvider`、`IExecutionPlanAnalyzer`、`IIndexRecommendationGenerator`
- 新增 `ISqlRewriteAdvisor`

验证：

1. SQL workflow 可在 MAF 内执行到 draft 结果
2. `WorkflowResultEnvelope.resultType == sql-optimization-report`

### TASK-C2 新建 SQL review gate 与 response bridge

目标：

- 让 SQL workflow 在 review gate 挂起，并能通过 submit 继续

新增类：

1. `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
   - `ValueTask HandleAsync(SqlOptimizationDraftReadyMessage message, IWorkflowContext context)`
   - `ValueTask<SqlOptimizationCompletedMessage> HandleAsync(ReviewDecisionResponseMessage message, IWorkflowContext context)`
2. `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewTaskGateway.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`
4. `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/ISqlReviewAdjustmentService.cs`

修改文件：

- `src/DbOptimizer.API/Api/ReviewApi.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

验证：

1. 提交 review 后 workflow 可继续
2. reject 会落失败状态
3. review task 已持久化 `request_id/engine_run_id/engine_checkpoint_ref`

### TASK-C3 SQL workflow 状态、history、SSE 投影

目标：

- MAF 事件可进入 `workflow_sessions`、history、SSE

新增类：

1. `src/DbOptimizer.Infrastructure/Workflows/Projection/WorkflowProjectionWriter.cs`
   - `ApplyAsync(Guid sessionId, string workflowType, IReadOnlyList<object> mafEvents, CancellationToken cancellationToken = default)`
2. `src/DbOptimizer.Infrastructure/Workflows/Events/MafWorkflowEventAdapter.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Events/WorkflowProgressCalculator.cs`

修改文件：

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Api/WorkflowEventsApi.cs`

验证：

1. `GET /api/history/{sessionId}` 可返回 SQL 统一结果
2. SSE 可看到 `review.requested` / `workflow.completed`

---

## EPIC D. 配置调优闭环

### TASK-D1 新建配置 workflow 消息与 executor

目标：

- 用 MAF executor 重建配置调优 workflow

新增文件：

- `src/DbOptimizer.Infrastructure/Maf/DbConfig/DbConfigWorkflowMessages.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/DbConfigInputValidationExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigCollectorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigAnalyzerMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigCoordinatorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`

关键方法：

- `HandleAsync(DbConfigWorkflowCommand, IWorkflowContext, CancellationToken)`
- `HandleAsync(ConfigSnapshotCollectedMessage, IWorkflowContext, CancellationToken)`
- `HandleAsync(ConfigRecommendationsGeneratedMessage, IWorkflowContext, CancellationToken)`
- `HandleAsync(ReviewDecisionResponseMessage, IWorkflowContext)`

验证：

1. 配置 workflow 可跑到 draft 结果
2. `resultType == db-config-optimization-report`

### TASK-D2 前端配置调优页面与 API

目标：

- 提供配置调优入口与结果渲染

修改文件：

- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.Web/src/App.vue`

新增前端类型：

- `CreateDbConfigOptimizationPayload`
- `DbConfigOptimizationResult`

建议新增组件：

- `src/DbOptimizer.Web/src/components/workflow/DbConfigOptimizationForm.vue`
- `src/DbOptimizer.Web/src/components/workflow/WorkflowResultPanel.vue`

前端状态：

- `dbConfigDatabaseId`
- `dbConfigDatabaseType`
- `dbConfigSessionId`
- `dbConfigWorkflowStatus`

前端方法：

- `createDbConfigOptimization(payload: CreateDbConfigOptimizationPayload)`
- `submitDbConfigOptimization(): Promise<void>`
- `loadDbConfigWorkflow(sessionId: string): Promise<void>`

实现要点：

- 增加 `createDbConfigOptimization`
- 导航增加 `db-config`
- 按 `resultType` 渲染配置结果
- `DbConfigOptimizationForm` 负责收集 `databaseId/databaseType`
- `WorkflowResultPanel` 输入改为 `WorkflowResultEnvelope`

验证：

1. UI 可发起配置调优
2. review/history 页面可显示配置结果

---

## EPIC E. 慢 SQL 自动分析闭环

### TASK-E1 提交服务与关联字段

目标：

- 慢 SQL 采集完成后自动创建 SQL workflow，并回写关联 session

新增类：

1. `src/DbOptimizer.Infrastructure/SlowQuery/ISlowQueryWorkflowSubmissionService.cs`
   - `Task<Guid> SubmitAsync(SlowQueryEntity slowQuery, CancellationToken cancellationToken = default)`
2. `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryWorkflowSubmissionService.cs`

修改文件：

- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs`
- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

实现要点：

- 由 `SlowQueryCollectionService` 在保存后调用 submission service
- 创建 SQL workflow 时 `sourceType=slow-query`
- 成功创建后回写 `latest_analysis_session_id`

验证：

1. 新 slow query 入库后能自动创建 workflow
2. `latest_analysis_session_id` 非空

### TASK-E2 slow query 查询与 dashboard API

目标：

- 补齐 slow query list/detail/trend/alerts API

新增类：

1. `src/DbOptimizer.Infrastructure/SlowQuery/ISlowQueryDashboardQueryService.cs`
2. `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryDashboardQueryService.cs`
   - `GetTrendAsync`
   - `GetAlertsAsync`
   - `GetSlowQueriesAsync`
   - `GetSlowQueryAsync`

修改文件：

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- 新增 `src/DbOptimizer.API/Api/SlowQueryApi.cs`
- `src/DbOptimizer.API/Program.cs`

验证：

1. `GET /api/dashboard/slow-query-trends`
2. `GET /api/dashboard/slow-query-alerts`
3. `GET /api/slow-queries`

### TASK-E3 前端趋势、告警、slow query 视图

目标：

- 前端能看慢 SQL 趋势、告警、最近分析

修改文件：

- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.Web/src/App.vue`

建议新增组件：

- `src/DbOptimizer.Web/src/components/slow-query/SlowQueryListPanel.vue`
- `src/DbOptimizer.Web/src/components/slow-query/SlowQueryDetailPanel.vue`
- `src/DbOptimizer.Web/src/components/dashboard/SlowQueryTrendChart.vue`
- `src/DbOptimizer.Web/src/components/dashboard/SlowQueryAlertList.vue`

前端方法：

- `getSlowQueryTrends(params: { databaseId: string; days?: number })`
- `getSlowQueryAlerts(params?: { databaseId?: string; status?: string })`
- `getSlowQueries(params?: { databaseId?: string; page?: number; pageSize?: number })`
- `getSlowQueryDetail(queryId: string)`

实现要点：

- 增加 trend/alerts/slow queries 拉取方法
- 增加 slow query 详情面板
- `SlowQueryListPanel` 点击后更新 `selectedSlowQueryId`
- `SlowQueryDetailPanel` 负责显示 `latestAnalysisSessionId` 并提供跳转
- `SlowQueryTrendChart` 输入为 `SlowQueryTrendResponse`
- `SlowQueryAlertList` 输入为 `SlowQueryAlertListResponse`

验证：

1. 页面可查看趋势
2. 页面可从 slow query 打开关联 analysis session

---

## EPIC F. Dashboard 趋势与告警

### TASK-F1 Dashboard workspace 入口与布局

目标：

- 在前端导航中新增 `dashboard` workspace
- 将 stats、trends、alerts 三块信息落成独立区块

修改文件：

- `src/DbOptimizer.Web/src/App.vue`

建议新增组件：

- `src/DbOptimizer.Web/src/components/dashboard/DashboardStatsPanel.vue`
- `src/DbOptimizer.Web/src/components/dashboard/SlowQueryTrendChart.vue`
- `src/DbOptimizer.Web/src/components/dashboard/SlowQueryAlertList.vue`

新增 state：

- `dashboardStats`
- `slowQueryTrend`
- `slowQueryAlerts`

方法：

- `loadDashboardWorkspace(): Promise<void>`
- `selectDashboardDatabase(databaseId: string): Promise<void>`

验证：

1. 可切换到 dashboard workspace
2. 首屏可同时看到 stats、trend、alerts

---

## EPIC G. PromptVersion 管理

### TASK-G1 PromptVersion API 与页面

目标：

- 打通 `prompt_versions` 的基本管理能力

新增类：

1. `src/DbOptimizer.Infrastructure/Prompts/IPromptVersionService.cs`
   - `ListAsync`
   - `CreateAsync`
   - `ActivateAsync`
   - `RollbackAsync`
2. `src/DbOptimizer.API/Api/PromptVersionApi.cs`

验证：

1. 可列出 prompt versions
2. 可切换 active version
