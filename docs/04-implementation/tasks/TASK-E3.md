# TASK-E3

## Goal

新增 slow query 列表、详情、趋势、告警的前端视图。

## Dependencies

- TASK-D2
- TASK-E2

## Read First

1. [../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md](../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md)
2. [../../03-design/api/FRONTEND_BACKEND_IO_SPEC.md](../../03-design/api/FRONTEND_BACKEND_IO_SPEC.md)

## Files To Modify

- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.Web/src/App.vue`

## New Components

1. `src/DbOptimizer.Web/src/components/slow-query/SlowQueryListPanel.vue`
2. `src/DbOptimizer.Web/src/components/slow-query/SlowQueryDetailPanel.vue`
3. `src/DbOptimizer.Web/src/components/dashboard/SlowQueryTrendChart.vue`
4. `src/DbOptimizer.Web/src/components/dashboard/SlowQueryAlertList.vue`

## Frontend Methods

- `getSlowQueryTrends(params: { databaseId: string; days?: number })`
- `getSlowQueryAlerts(params?: { databaseId?: string; status?: string })`
- `getSlowQueries(params?: { databaseId?: string; page?: number; pageSize?: number })`
- `getSlowQueryDetail(queryId: string)`

## Frontend State

- `selectedSlowQueryId`
- `slowQueryTrend`
- `slowQueryAlerts`
- `slowQueryItems`

## Steps

1. 在 `api.ts` 增加 slow query 请求方法。
2. 在 `App.vue` 增加 `slow-query` workspace。
3. 新建 slow query 列表、详情、trend、alert 组件。
4. 从 slow query 明细提供跳转到关联 analysis session。

## Verification

1. 页面可查看趋势
2. 页面可查看告警
3. 页面可从 slow query 打开关联 analysis session

## Done Criteria

- slow query 前端视图完整可用
- dashboard 与 slow query workspace 联通
