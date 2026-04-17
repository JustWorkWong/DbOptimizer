# Implementation Technical Plan

## 1. 目标

本方案要同时达成：

1. MAF 成为唯一 workflow 编排引擎。
2. SQL 与配置调优都使用统一结果协议。
3. 慢 SQL 能自动提交分析并追踪到 workflow session。
4. dashboard/history/review/SSE 使用同一份投影模型。

## 2. 代码结构调整

建议新增目录：

```text
src/DbOptimizer.API/Contracts/
src/DbOptimizer.Infrastructure/Maf/
src/DbOptimizer.Infrastructure/Maf/Runtime/
src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/
src/DbOptimizer.Infrastructure/Maf/DbConfig/
src/DbOptimizer.Infrastructure/Workflows/Application/
src/DbOptimizer.Infrastructure/Workflows/Projection/
src/DbOptimizer.Infrastructure/Workflows/Serialization/
src/DbOptimizer.Infrastructure/Workflows/State/
src/DbOptimizer.Infrastructure/Workflows/Review/
```

## 3. 核心新增类

### Runtime 层

| 类 | 方法 |
|---|---|
| `MafWorkflowRuntimeOptions` | 配置属性 |
| `IMafWorkflowRuntime` | `StartSqlAnalysisAsync` `StartDbConfigOptimizationAsync` `ResumeAsync` `CancelAsync` |
| `MafWorkflowRuntime` | `StartSqlAnalysisAsync` `StartDbConfigOptimizationAsync` `ResumeAsync` `CancelAsync` `RunInternalAsync` |
| `IMafWorkflowFactory` | `BuildSqlAnalysisWorkflow` `BuildDbConfigWorkflow` |
| `MafWorkflowFactory` | 同上 |
| `IMafRunStateStore` | `SaveAsync` `GetAsync` `DeleteAsync` |
| `IWorkflowProjectionWriter` | `ApplyAsync` |

### Application 层

| 类 | 方法 |
|---|---|
| `IWorkflowApplicationService` | `StartSqlAnalysisAsync` `StartDbConfigOptimizationAsync` `GetAsync` `ResumeAsync` `CancelAsync` |
| `WorkflowApplicationService` | 同上 |
| `WorkflowRequestValidator` | `Validate(CreateSqlAnalysisWorkflowRequest)` `Validate(CreateDbConfigOptimizationWorkflowRequest)` |

### Review 桥接层

| 类 | 方法 |
|---|---|
| `IWorkflowReviewTaskGateway` | `CreateAsync` `CompleteAsync` `GetAsync` |
| `WorkflowReviewTaskGateway` | 同上 |
| `IWorkflowReviewResponseFactory` | `CreateSqlResponse` `CreateDbConfigResponse` |

精确签名基线：

```csharp
public interface IMafWorkflowFactory
{
    Workflow BuildSqlAnalysisWorkflow();
    Workflow BuildDbConfigWorkflow();
}

public interface IMafWorkflowRuntime
{
    Task<WorkflowStartResponse> StartSqlAnalysisAsync(SqlAnalysisWorkflowCommand command, CancellationToken cancellationToken = default);
    Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(DbConfigWorkflowCommand command, CancellationToken cancellationToken = default);
    Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
```

## 4. 包依赖

`src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`

新增：

```xml
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.0.0-rc4" />
```

说明：

- 版本基于 `2026-04-17` NuGet 公开页。
- 如果团队决定锁到其他 RC/preview，必须同步更新本文件与实施文档。

## 5. API 文件拆分原则

当前 API DTO 大量内嵌在 `WorkflowApi.cs` / `ReviewApi.cs` / `DashboardAndHistoryApi.cs` 中。后续拆分原则：

1. 路由文件只保留 endpoint 映射。
2. DTO 全部移动到 `src/DbOptimizer.API/Contracts/*`。
3. 业务逻辑全部移动到 infrastructure application/query service。

## 6. 数据库变更

### `workflow_sessions`

新增字段：

- `engine_type`
- `engine_run_id`
- `engine_checkpoint_ref`
- `engine_state`
- `result_type`
- `source_type`
- `source_ref_id`

### `slow_queries`

新增字段：

- `latest_analysis_session_id`

## 7. 前端技术方案

保持现有 Vue 3 + 单应用结构，不在本轮强行引入新 UI 框架；但必须扩展：

1. `api.ts` 契约到统一 envelope
2. `App.vue` 增加 `db-config` / `slow-query` / `dashboard` 视图分区
3. 前端渲染按 `resultType` 分支

## 8. 验证策略

1. 先做契约与 DTO 编译检查。
2. 再做 MAF runtime 与单 workflow 集成验证。
3. 再做 history/review/SSE 投影验证。
4. 最后做 slow query 闭环验证。

## 9. 契约准入

任何实现任务在开始前，必须明确引用至少一份 endpoint 合同文档：

- workflow 相关任务 -> `03-design/api/WORKFLOW_API_CONTRACT.md`
- review 相关任务 -> `03-design/api/REVIEW_API_CONTRACT.md`
- dashboard/slow query 相关任务 -> `03-design/api/DASHBOARD_API_CONTRACT.md`
- SSE/replay 相关任务 -> `03-design/api/SSE_EVENT_CONTRACT.md`
