# API Overview

## 统一原则

所有 API 统一使用：

1. 外层 `ApiEnvelope<T>`
2. 统一错误码与 `details`
3. 统一分页结构
4. 统一 workflow 结果壳 `WorkflowResultEnvelope`

具体 endpoint 契约见：

- [WORKFLOW_API_CONTRACT.md](./WORKFLOW_API_CONTRACT.md)
- [REVIEW_API_CONTRACT.md](./REVIEW_API_CONTRACT.md)
- [DASHBOARD_API_CONTRACT.md](./DASHBOARD_API_CONTRACT.md)
- [SSE_EVENT_CONTRACT.md](./SSE_EVENT_CONTRACT.md)

## 外层包络

成功：

```json
{
  "success": true,
  "data": {},
  "error": null,
  "meta": {
    "requestId": "req-123",
    "timestamp": "2026-04-17T09:00:00Z"
  }
}
```

失败：

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "WORKFLOW_NOT_FOUND",
    "message": "Workflow session not found.",
    "details": {
      "sessionId": "..."
    }
  },
  "meta": {
    "requestId": "req-123",
    "timestamp": "2026-04-17T09:00:00Z"
  }
}
```

## 统一结果壳

```json
{
  "resultType": "sql-optimization-report",
  "displayName": "SQL 调优报告",
  "summary": "发现 2 个高收益索引建议。",
  "data": {},
  "metadata": {
    "databaseId": "mysql-local",
    "databaseType": "mysql"
  }
}
```

### `WorkflowResultEnvelope`

| 字段 | 类型 | 说明 |
|---|---|---|
| `resultType` | `string` | 唯一判别键 |
| `displayName` | `string` | UI 展示名 |
| `summary` | `string` | 简要摘要 |
| `data` | `object` | 具体结果对象 |
| `metadata` | `object` | 扩展元数据 |

允许值：

- `sql-optimization-report`
- `db-config-optimization-report`

## Workflow 公共枚举

### `workflowType`

- `SqlAnalysis`
- `DbConfigOptimization`

### `status`

- `Pending`
- `Running`
- `WaitingForReview`
- `Completed`
- `Failed`
- `Cancelled`

### `engineType`

- `maf`

### `sourceType`

- `manual`
- `slow-query`

## 分页结构

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "total": 87,
  "hasMore": true
}
```

## 关键错误码

| 错误码 | 场景 |
|---|---|
| `INVALID_REQUEST` | 请求参数缺失或格式错误 |
| `INVALID_WORKFLOW_TYPE` | workflowType 非法 |
| `WORKFLOW_NOT_FOUND` | session 不存在 |
| `WORKFLOW_INVALID_STATE` | 状态不允许当前操作 |
| `REVIEW_TASK_NOT_FOUND` | review task 不存在 |
| `REVIEW_ALREADY_SUBMITTED` | review 已处理 |
| `REVIEW_INVALID_STATE` | workflow 不在 waiting review |
| `CHECKPOINT_NOT_FOUND` | MAF/业务 checkpoint 不存在 |
| `SLOW_QUERY_NOT_FOUND` | slow query 不存在 |

## 后端类设计

新增 DTO 文件：

- `src/DbOptimizer.API/Contracts/Common/WorkflowResultEnvelope.cs`
- `src/DbOptimizer.API/Contracts/Common/PagedResponse.cs`
- `src/DbOptimizer.API/Contracts/Common/WorkflowEnums.cs`

新增序列化服务：

`src/DbOptimizer.Infrastructure/Workflows/Serialization/WorkflowResultSerializer.cs`

```csharp
public interface IWorkflowResultSerializer
{
    WorkflowResultEnvelope ToEnvelope(OptimizationReport report, string databaseId, string databaseType);
    WorkflowResultEnvelope ToEnvelope(ConfigOptimizationReport report);
    JsonElement ToJsonElement<T>(T value);
}
```

前端只能按 `resultType` 分发，禁止再通过字段猜结果类型。
