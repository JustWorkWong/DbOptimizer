# Frontend Architecture

## 1. 当前状态

前端当前仍是单文件 `App.vue` 驱动的 Vue 3 应用，已有能力集中在：

- SQL 分析提交
- review 队列与明细
- history 列表与明细
- replay

缺失：

- 配置调优入口
- slow query 视图
- dashboard 趋势与告警
- 按 `resultType` 的统一结果渲染

## 2. 本轮目标

在不强行重构 UI 技术栈的前提下，把前端信息架构补齐到可支撑整个系统：

1. `dashboard`
2. `sql-analysis`
3. `db-config`
4. `review`
5. `history`
6. `replay`
7. `slow-query`

## 3. 前端模块划分

建议新增：

```text
src/DbOptimizer.Web/src/components/
  dashboard/
  workflow/
  review/
  slow-query/
```

最少组件：

- `DashboardOverviewPanel`
- `WorkflowResultPanel`
- `SqlAnalysisForm`
- `DbConfigOptimizationForm`
- `ReviewQueuePanel`
- `HistorySessionPanel`
- `SlowQueryListPanel`
- `SlowQueryDetailPanel`

## 4. 状态约定

前端状态必须围绕 API 契约，不再直接围绕旧 `OptimizationReport` 类型。

核心状态：

- `workflowStatus`
- `workflowResultEnvelope`
- `reviewDetail`
- `slowQueryList`
- `slowQueryTrend`
- `slowQueryAlerts`

## 5. 执行规则

实现与交付以这些文档为准：

- [../03-design/api/API_OVERVIEW.md](../03-design/api/API_OVERVIEW.md)
- [../03-design/api/WORKFLOW_API_CONTRACT.md](../03-design/api/WORKFLOW_API_CONTRACT.md)
- [../03-design/api/DASHBOARD_API_CONTRACT.md](../03-design/api/DASHBOARD_API_CONTRACT.md)
- [../04-implementation/IMPLEMENTATION_TASK_CHECKLIST.md](../04-implementation/IMPLEMENTATION_TASK_CHECKLIST.md)

旧版前端架构细节已下沉到 archive，不再作为实现主依据。
