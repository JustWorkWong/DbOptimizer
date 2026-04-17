# TASK-B2

## Goal

新建 workflow application service，让 API 改为通过应用服务启动/查询/取消/恢复 workflow。

## Dependencies

- TASK-A1
- TASK-A2
- TASK-B1

## Read First

1. [../../03-design/api/WORKFLOW_API_CONTRACT.md](../../03-design/api/WORKFLOW_API_CONTRACT.md)
2. [../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md](../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/Workflows/Application/IWorkflowApplicationService.cs`
   - `StartSqlAnalysisAsync`
   - `StartDbConfigOptimizationAsync`
   - `GetAsync`
   - `ResumeAsync`
   - `CancelAsync`
2. `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowApplicationService.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowRequestValidator.cs`
   - `Validate(CreateSqlAnalysisWorkflowRequest request)`
   - `Validate(CreateDbConfigOptimizationWorkflowRequest request)`

## Files To Modify

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Program.cs`

## Steps

1. 新建 validator，固定 request 校验逻辑。
2. 新建 application service，对接 MAF runtime。
3. 将 `WorkflowApi.cs` 从 scheduler/query service 切换为 application service。
4. 保持现有路由路径不变。

## API Changes

- 无路径变化
- 统一由 application service 返回 contract DTO

## Verification

1. `POST /api/workflows/sql-analysis` 仍能返回 `WorkflowStartResponse`
2. `GET /api/workflows/{sessionId}` 仍能返回 `WorkflowStatusResponse`
3. `cancel/resume` 路由仍可编译

## Done Criteria

- `WorkflowApi.cs` 不再直接承载主业务逻辑
- 请求校验集中到 validator
- 运行入口切到 application service
