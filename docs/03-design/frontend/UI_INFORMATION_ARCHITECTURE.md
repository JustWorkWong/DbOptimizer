# UI Information Architecture

## 1. 页面结构

本轮 UI 以单应用多工作区组织：

| 工作区 | 目标 | 主 API |
|---|---|---|
| `dashboard` | 总览、趋势、告警 | `GET /api/dashboard/stats` `GET /api/dashboard/slow-query-trends` `GET /api/dashboard/slow-query-alerts` |
| `sql-analysis` | 手工提交 SQL workflow | `POST /api/workflows/sql-analysis` |
| `db-config` | 手工提交配置调优 workflow | `POST /api/workflows/db-config-optimization` |
| `review` | 审核队列与提交 | `GET /api/reviews` `GET /api/reviews/{taskId}` `POST /api/reviews/{taskId}/submit` |
| `history` | 会话历史明细 | `GET /api/history` `GET /api/history/{sessionId}` |
| `replay` | 事件回放 | `GET /api/history/{sessionId}/replay` |
| `slow-query` | 慢 SQL 列表与关联分析 | `GET /api/slow-queries` `GET /api/slow-queries/{queryId}` |

## 2. 组件建议

### `dashboard`

- `DashboardStatsPanel`
- `SlowQueryTrendChart`
- `SlowQueryAlertList`

### `sql-analysis`

- `SqlAnalysisForm`
- `WorkflowStatusCard`
- `WorkflowResultPanel`

### `db-config`

- `DbConfigOptimizationForm`
- `WorkflowStatusCard`
- `WorkflowResultPanel`

### `review`

- `ReviewQueuePanel`
- `ReviewDetailPanel`
- `ReviewSubmissionForm`

### `slow-query`

- `SlowQueryListPanel`
- `SlowQueryDetailPanel`
- `LinkedAnalysisSessionCard`

## 3. 状态结构

建议在 `App.vue` 或拆分后的组合式模块中维护：

```ts
type ActiveWorkspace =
  | 'dashboard'
  | 'sql-analysis'
  | 'db-config'
  | 'review'
  | 'history'
  | 'replay'
  | 'slow-query'
```

关键 state：

- `activeWorkspace`
- `selectedSessionId`
- `selectedReviewTaskId`
- `selectedSlowQueryId`
- `workflowStatus`
- `workflowResultEnvelope`
- `slowQueryTrend`
- `slowQueryAlerts`

## 4. 前端方法映射

最少需要的 API 方法：

- `createSqlAnalysis(payload: CreateSqlAnalysisPayload)`
- `createDbConfigOptimization(payload: CreateDbConfigOptimizationPayload)`
- `getWorkflow(sessionId: string)`
- `getPendingReviews()`
- `submitReview(taskId: string, payload: SubmitReviewPayload)`
- `getSlowQueryTrends(params)`
- `getSlowQueryAlerts(params)`
- `getSlowQueries(params)`
- `getSlowQueryDetail(queryId: string)`

## 5. 渲染规则

结果组件只按 `resultType` 渲染：

- `sql-optimization-report`
- `db-config-optimization-report`

禁止通过字段存在性猜测结果类型。
