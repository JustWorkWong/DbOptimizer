# TASK-E2

## Goal

补齐 slow query list/detail/trend/alerts 的后端 API 和查询服务。

## Dependencies

- TASK-A1
- TASK-A2
- TASK-E1

## Read First

1. [../../03-design/api/DASHBOARD_API_CONTRACT.md](../../03-design/api/DASHBOARD_API_CONTRACT.md)
2. [../../03-design/api/API_OVERVIEW.md](../../03-design/api/API_OVERVIEW.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/SlowQuery/ISlowQueryDashboardQueryService.cs`
   - `GetTrendAsync`
   - `GetAlertsAsync`
   - `GetSlowQueriesAsync`
   - `GetSlowQueryAsync`
2. `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryDashboardQueryService.cs`
3. `src/DbOptimizer.API/Api/SlowQueryApi.cs`

## Files To Modify

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Program.cs`

## API Endpoints

- `GET /api/dashboard/slow-query-trends`
- `GET /api/dashboard/slow-query-alerts`
- `GET /api/slow-queries`
- `GET /api/slow-queries/{queryId}`

## Steps

1. 建立 slow query query service。
2. 在 dashboard API 中增加 trend/alerts endpoint。
3. 新增 slow query API 路由。
4. 返回统一 envelope。

## Verification

1. `GET /api/dashboard/slow-query-trends`
2. `GET /api/dashboard/slow-query-alerts`
3. `GET /api/slow-queries`
4. `GET /api/slow-queries/{queryId}`

## Done Criteria

- slow query 相关查询 API 完整可用
- 响应结构与契约一致
