# Review API Contract

## 设计目标

review API 必须与 MAF request/response 对齐：

1. review task 是外部可见审计对象。
2. review submit 的结果必须能转换成 workflow response message。
3. review payload 不能再固定为 `OptimizationReport`。

## `GET /api/reviews`

### Response Item

```json
{
  "taskId": "33333333-3333-3333-3333-333333333333",
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "DbConfigOptimization",
  "status": "Pending",
  "payload": {
    "resultType": "db-config-optimization-report",
    "displayName": "数据库配置调优报告",
    "summary": "建议调整 3 个参数。",
    "data": {},
    "metadata": {}
  },
  "createdAt": "2026-04-17T09:00:10Z"
}
```

## `GET /api/reviews/{taskId}`

### Response

```json
{
  "taskId": "33333333-3333-3333-3333-333333333333",
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "status": "Pending",
  "payload": {
    "resultType": "sql-optimization-report",
    "displayName": "SQL 调优报告",
    "summary": "发现 2 个高收益索引建议。",
    "data": {},
    "metadata": {}
  },
  "reviewerComment": null,
  "adjustments": null,
  "createdAt": "2026-04-17T09:00:10Z",
  "reviewedAt": null
}
```

## `POST /api/reviews/{taskId}/submit`

### Request

```json
{
  "action": "adjust",
  "comment": "保留索引建议，但把 rewrite 降为 warning。",
  "adjustments": {
    "suppressSqlRewrite": true
  }
}
```

### Request DTO

```csharp
public sealed class SubmitReviewRequest
{
    public string Action { get; init; } = string.Empty;
    public string? Comment { get; init; }
    public Dictionary<string, JsonElement>? Adjustments { get; init; }
}
```

## 桥接类

`src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`

```csharp
public interface IWorkflowReviewResponseFactory
{
    ReviewDecisionResponseMessage CreateSqlResponse(Guid sessionId, Guid taskId, SubmitReviewRequest request, WorkflowResultEnvelope payload);
    ReviewDecisionResponseMessage CreateDbConfigResponse(Guid sessionId, Guid taskId, SubmitReviewRequest request, WorkflowResultEnvelope payload);
}
```

`review_tasks.recommendations` 存储语义调整为 `WorkflowResultEnvelope`。

## Correlation Rule

前端 `SubmitReviewRequest` 不需要提交 `requestId/runId/checkpointRef`。

后端提交 review 时必须：

1. 先通过 `taskId` 读取 `review_tasks`
2. 从持久化字段读取：
   - `request_id`
   - `engine_run_id`
   - `engine_checkpoint_ref`
3. 再组装 `ReviewDecisionResponseMessage`

因此 `review_tasks` 必须持久化这些字段，不能只依赖内存态。
