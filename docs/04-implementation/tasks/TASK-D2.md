# TASK-D2

## Goal

新增前端配置调优入口、表单、状态展示和结果渲染。

## Dependencies

- TASK-A1
- TASK-D1

## Read First

1. [../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md](../../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md)
2. [../../03-design/api/FRONTEND_BACKEND_IO_SPEC.md](../../03-design/api/FRONTEND_BACKEND_IO_SPEC.md)

## Files To Modify

- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.Web/src/App.vue`

## New Components

1. `src/DbOptimizer.Web/src/components/workflow/DbConfigOptimizationForm.vue`
2. `src/DbOptimizer.Web/src/components/workflow/WorkflowResultPanel.vue`

## Frontend Methods

- `createDbConfigOptimization(payload: CreateDbConfigOptimizationPayload)`
- `submitDbConfigOptimization(): Promise<void>`
- `loadDbConfigWorkflow(sessionId: string): Promise<void>`

## Frontend State

- `dbConfigDatabaseId`
- `dbConfigDatabaseType`
- `dbConfigSessionId`
- `dbConfigWorkflowStatus`

## Steps

1. 在 `api.ts` 中补配置调优请求/响应类型和方法。
2. 在 `App.vue` 中增加 `db-config` workspace。
3. 新建配置调优表单组件。
4. 让结果面板统一消费 `WorkflowResultEnvelope`。

## Verification

1. UI 可发起配置调优
2. history/review 页面可显示配置结果
3. `resultType=db-config-optimization-report` 时渲染正确

## Done Criteria

- 前端具备配置调优完整入口
- 结果渲染不再依赖旧 `OptimizationReport`
