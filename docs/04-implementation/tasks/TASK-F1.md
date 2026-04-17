# TASK-F1

## Goal

建立 dashboard workspace 入口和布局，把 stats / trends / alerts 聚合到前端首页。

## Dependencies

- TASK-D2
- TASK-E2
- TASK-E3

## Read First

1. [../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md](../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md)
2. [../../03-design/api/DASHBOARD_API_CONTRACT.md](../../03-design/api/DASHBOARD_API_CONTRACT.md)

## Files To Modify

- `src/DbOptimizer.Web/src/App.vue`

## New Components

1. `src/DbOptimizer.Web/src/components/dashboard/DashboardStatsPanel.vue`
2. `src/DbOptimizer.Web/src/components/dashboard/SlowQueryTrendChart.vue`
3. `src/DbOptimizer.Web/src/components/dashboard/SlowQueryAlertList.vue`

## Frontend Methods

- `loadDashboardWorkspace(): Promise<void>`
- `selectDashboardDatabase(databaseId: string): Promise<void>`

## Frontend State

- `dashboardStats`
- `slowQueryTrend`
- `slowQueryAlerts`

## Steps

1. 增加 `dashboard` workspace。
2. 聚合 stats、trend、alerts 三类数据。
3. 支持按数据库筛选 trend/alerts。

## Verification

1. 可切换到 dashboard workspace
2. 首屏可同时看到 stats、trend、alerts

## Done Criteria

- dashboard 成为真实首页工作区
- 数据块之间状态和加载逻辑清晰分离
