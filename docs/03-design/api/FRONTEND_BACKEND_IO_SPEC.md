# Frontend Backend IO Specification

## Purpose

统一整理前后端接口的请求、响应和前端方法映射，避免 AI 分别从多份 API 文档里拼接。

## Scope

覆盖：

1. workflow API
2. review API
3. history / dashboard / slow query API
4. 前端 `api.ts` 方法清单

## Workflow IO

### Create SQL Analysis

- Backend endpoint: `POST /api/workflows/sql-analysis`
- Frontend method: `createSqlAnalysis(payload: CreateSqlAnalysisPayload)`
- Request:
  - `sqlText: string`
  - `databaseId: string`
  - `databaseEngine?: string`
  - `source: { sourceType: string; sourceRefId?: string | null }`
  - `options: { enableIndexRecommendation: boolean; enableSqlRewrite: boolean; requireHumanReview: boolean }`
- Response:
  - `sessionId`
  - `workflowType`
  - `engineType`
  - `status`
  - `startedAt`

### Create DB Config Optimization

- Backend endpoint: `POST /api/workflows/db-config-optimization`
- Frontend method: `createDbConfigOptimization(payload: CreateDbConfigOptimizationPayload)`
- Request:
  - `databaseId: string`
  - `databaseType: string`
  - `options: { allowFallbackSnapshot: boolean; requireHumanReview: boolean }`
- Response:
  - `sessionId`
  - `workflowType`
  - `engineType`
  - `status`
  - `startedAt`

### Get Workflow Status

- Backend endpoint: `GET /api/workflows/{sessionId}`
- Frontend method: `getWorkflow(sessionId: string)`
- Response:
  - `sessionId`
  - `workflowType`
  - `engineType`
  - `status`
  - `currentNode`
  - `progressPercent`
  - `source`
  - `review`
  - `result: WorkflowResultEnvelope | null`

## Review IO

### List Reviews

- Backend endpoint: `GET /api/reviews`
- Frontend method: `getPendingReviews()`

### Get Review

- Backend endpoint: `GET /api/reviews/{taskId}`
- Frontend method: `getReview(taskId: string)`

### Submit Review

- Backend endpoint: `POST /api/reviews/{taskId}/submit`
- Frontend method: `submitReview(taskId: string, payload: SubmitReviewPayload)`
- Request:
  - `action: 'approve' | 'reject' | 'adjust'`
  - `comment?: string`
  - `adjustments?: Record<string, unknown>`

## Dashboard / Slow Query IO

### Dashboard Stats

- `GET /api/dashboard/stats`
- `getDashboardStats()`

### Slow Query Trends

- `GET /api/dashboard/slow-query-trends`
- `getSlowQueryTrends(params)`

### Slow Query Alerts

- `GET /api/dashboard/slow-query-alerts`
- `getSlowQueryAlerts(params)`

### Slow Query List

- `GET /api/slow-queries`
- `getSlowQueries(params)`

### Slow Query Detail

- `GET /api/slow-queries/{queryId}`
- `getSlowQueryDetail(queryId: string)`

## Result Rendering Rule

前端结果组件只按 `resultType` 渲染：

- `sql-optimization-report`
- `db-config-optimization-report`

## References

- [WORKFLOW_API_CONTRACT.md](./WORKFLOW_API_CONTRACT.md)
- [REVIEW_API_CONTRACT.md](./REVIEW_API_CONTRACT.md)
- [DASHBOARD_API_CONTRACT.md](./DASHBOARD_API_CONTRACT.md)
