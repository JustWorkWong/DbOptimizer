# Workflow API Contract

## `POST /api/workflows/sql-analysis`

### Request

```json
{
  "sqlText": "SELECT * FROM users WHERE age > 18 ORDER BY created_at DESC",
  "databaseId": "mysql-local",
  "databaseEngine": "mysql",
  "source": {
    "sourceType": "manual",
    "sourceRefId": null
  },
  "options": {
    "enableIndexRecommendation": true,
    "enableSqlRewrite": true,
    "requireHumanReview": true
  }
}
```

### Request DTO

```csharp
public sealed class CreateSqlAnalysisWorkflowRequest
{
    public string SqlText { get; init; } = string.Empty;
    public string DatabaseId { get; init; } = string.Empty;
    public string? DatabaseEngine { get; init; }
    public WorkflowSourceDto Source { get; init; } = new();
    public SqlAnalysisWorkflowOptionsDto Options { get; init; } = new();
}
```

### Option Semantics

- `enableIndexRecommendation = false`
  - 不执行真实索引推荐逻辑
  - `IndexAdvisorMafExecutor` 仍作为门控节点存在，但必须输出空的 `IndexRecommendations`
  - 后续消息继续流向 `SqlRewriteMafExecutor`
- `enableSqlRewrite = false`
  - 不执行真实 SQL rewrite 逻辑
  - `SqlRewriteMafExecutor` 必须输出空的 `SqlRewriteSuggestions`
- `requireHumanReview = false`
  - 不创建 `review_tasks`
  - workflow 在 coordinator 之后直接完成

### Response

```json
{
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "engineType": "maf",
  "status": "Running",
  "startedAt": "2026-04-17T09:00:00Z"
}
```

## `POST /api/workflows/db-config-optimization`

### Request

```json
{
  "databaseId": "mysql-local",
  "databaseType": "mysql",
  "options": {
    "allowFallbackSnapshot": true,
    "requireHumanReview": true
  }
}
```

### Request DTO

```csharp
public sealed class CreateDbConfigOptimizationWorkflowRequest
{
    public string DatabaseId { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public DbConfigWorkflowOptionsDto Options { get; init; } = new();
}
```

### Option Semantics

- `allowFallbackSnapshot = true`
  - 允许配置采集失败后使用 fallback snapshot
- `requireHumanReview = false`
  - 不创建 `review_tasks`
  - workflow 在 coordinator 之后直接完成

## `GET /api/workflows/{sessionId}`

### Response

```json
{
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "engineType": "maf",
  "status": "WaitingForReview",
  "currentNode": "SqlHumanReviewGateExecutor",
  "progressPercent": 66,
  "startedAt": "2026-04-17T09:00:00Z",
  "updatedAt": "2026-04-17T09:00:10Z",
  "completedAt": null,
  "source": {
    "sourceType": "slow-query",
    "sourceRefId": "22222222-2222-2222-2222-222222222222"
  },
  "review": {
    "taskId": "33333333-3333-3333-3333-333333333333",
    "status": "Pending"
  },
  "result": null,
  "error": null
}
```

### Response DTO

```csharp
public sealed record WorkflowStatusResponse(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string Status,
    string? CurrentNode,
    int ProgressPercent,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    WorkflowSourceDto Source,
    WorkflowReviewSummaryDto? Review,
    WorkflowResultEnvelope? Result,
    WorkflowErrorDto? Error);
```

## `POST /api/workflows/{sessionId}/cancel`

返回：

```json
{
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "engineType": "maf",
  "status": "Cancelled"
}
```

## Review Resume Semantics

- Public workflow API no longer exposes `POST /api/workflows/{sessionId}/resume`.
- When a workflow is in `WaitingForReview`, continuation happens through review submission, which resumes the native MAF run with review responses.
- `POST /api/workflows/{sessionId}/cancel` remains the public interruption endpoint.

## 后端类拆分

API DTO 文件：

- `src/DbOptimizer.API/Contracts/Workflows/CreateSqlAnalysisWorkflowRequest.cs`
- `src/DbOptimizer.API/Contracts/Workflows/CreateDbConfigOptimizationWorkflowRequest.cs`
- `src/DbOptimizer.API/Contracts/Workflows/WorkflowStatusResponse.cs`
- `src/DbOptimizer.API/Contracts/Workflows/WorkflowStartResponse.cs`
- `src/DbOptimizer.API/Contracts/Workflows/WorkflowCancelResponse.cs`

Application Service：

```csharp
public interface IWorkflowApplicationService
{
    Task<WorkflowStartResponse> StartSqlAnalysisAsync(CreateSqlAnalysisWorkflowRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(CreateDbConfigOptimizationWorkflowRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
```
