# Dashboard And History API Contract

## 范围

这个文档覆盖：

- dashboard stats
- history list/detail/replay
- slow query trends
- slow query alerts
- slow query list/detail

## `GET /api/dashboard/stats`

返回总任务数、运行中任务、待审核任务、完成任务、最近任务与性能趋势。

## `GET /api/dashboard/slow-query-trends`

### Query

- `databaseId`
- `days`，默认 `7`

### Response

```json
{
  "databaseId": "mysql-local",
  "days": 7,
  "points": [
    {
      "date": "2026-04-17",
      "slowQueryCount": 12,
      "avgExecutionTimeMs": 1530,
      "analysisTriggeredCount": 6
    }
  ]
}
```

## `GET /api/dashboard/slow-query-alerts`

### Query

- `databaseId`
- `status`: `open` / `resolved`

### Response

```json
{
  "items": [
    {
      "alertId": "44444444-4444-4444-4444-444444444444",
      "databaseId": "mysql-local",
      "severity": "high",
      "queryId": "55555555-5555-5555-5555-555555555555",
      "title": "同一 SQL 指纹在 1 小时内出现 20 次慢查询",
      "status": "open",
      "createdAt": "2026-04-17T08:30:00Z"
    }
  ]
}
```

## `GET /api/history`

### Query

- `workflowType`
- `status`
- `sourceType`
- `startDate`
- `endDate`
- `page`
- `pageSize`

### Response Item

```json
{
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "sourceType": "slow-query",
  "sourceRefId": "55555555-5555-5555-5555-555555555555",
  "status": "Completed",
  "startedAt": "2026-04-17T08:31:00Z",
  "completedAt": "2026-04-17T08:31:10Z",
  "durationSeconds": 10,
  "resultType": "sql-optimization-report",
  "recommendationCount": 3
}
```

## `GET /api/history/{sessionId}`

返回统一 `WorkflowResultEnvelope`，禁止再直接返回 `OptimizationReport`。

## `GET /api/history/{sessionId}/replay`

返回 workflow 事件回放序列。

## `GET /api/slow-queries`

### Response Item

```json
{
  "queryId": "55555555-5555-5555-5555-555555555555",
  "databaseId": "mysql-local",
  "databaseType": "mysql",
  "queryHash": "abc123",
  "sqlFingerprint": "select * from users where age > ?",
  "avgExecutionTimeMs": 1500,
  "executionCount": 9,
  "lastSeenAt": "2026-04-17T08:30:00Z",
  "latestAnalysisSessionId": "11111111-1111-1111-1111-111111111111"
}
```

## `GET /api/slow-queries/{queryId}`

返回 slow query 明细，并附带最近一次关联分析。

## 后端类设计

新增 DTO：

- `src/DbOptimizer.API/Contracts/Dashboard/DashboardStatsResponse.cs`
- `src/DbOptimizer.API/Contracts/Dashboard/SlowQueryTrendResponse.cs`
- `src/DbOptimizer.API/Contracts/Dashboard/SlowQueryAlertResponse.cs`
- `src/DbOptimizer.API/Contracts/History/HistoryListResponse.cs`
- `src/DbOptimizer.API/Contracts/History/HistoryDetailResponse.cs`
- `src/DbOptimizer.API/Contracts/History/HistoryReplayResponse.cs`
- `src/DbOptimizer.API/Contracts/SlowQueries/SlowQueryListResponse.cs`
- `src/DbOptimizer.API/Contracts/SlowQueries/SlowQueryDetailResponse.cs`

新增查询服务：

```csharp
public interface ISlowQueryDashboardQueryService
{
    Task<SlowQueryTrendResponse> GetTrendAsync(string databaseId, int days, CancellationToken cancellationToken = default);
    Task<SlowQueryAlertListResponse> GetAlertsAsync(string? databaseId, string? status, CancellationToken cancellationToken = default);
    Task<SlowQueryListResponse> GetSlowQueriesAsync(string? databaseId, string? queryHash, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SlowQueryDetailResponse?> GetSlowQueryAsync(Guid queryId, CancellationToken cancellationToken = default);
}
```

## 数据库扩展

为实现 slow query -> workflow 追踪：

1. `slow_queries` 新增 `latest_analysis_session_id`
2. `workflow_sessions` 新增 `source_type` 与 `source_ref_id`
