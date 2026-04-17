# TASK-C2

## Goal

实现 SQL review gate、review task 持久化和 response bridge。

## Dependencies

- TASK-A2
- TASK-C1

## Read First

1. [../../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md](../../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md)
2. [../../03-design/api/REVIEW_API_CONTRACT.md](../../03-design/api/REVIEW_API_CONTRACT.md)
3. [../../03-design/data/DATA_MODEL.md](../../03-design/data/DATA_MODEL.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
   - `ValueTask HandleAsync(SqlOptimizationDraftReadyMessage message, IWorkflowContext context)`
   - `ValueTask<SqlOptimizationCompletedMessage> HandleAsync(ReviewDecisionResponseMessage message, IWorkflowContext context)`
2. `src/DbOptimizer.Infrastructure/Workflows/Review/IWorkflowReviewTaskGateway.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewTaskGateway.cs`
4. `src/DbOptimizer.Infrastructure/Workflows/Review/IWorkflowReviewResponseFactory.cs`
5. `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`
6. `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/ISqlReviewAdjustmentService.cs`

## Files To Modify

- `src/DbOptimizer.API/Api/ReviewApi.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

## Persistence Rules

`review_tasks` 必须持久化：

- `request_id`
- `engine_run_id`
- `engine_checkpoint_ref`

## Option Rules

- `RequireHumanReview = true` -> 创建 review task 并挂起
- `RequireHumanReview = false` -> 不创建 review task，直接 completed

## Steps

1. 实现 review task gateway。
2. 在 review gate 生成 `requestId/runId/checkpointRef`。
3. 落库 review correlation。
4. 实现 response factory。
5. 更新 `ReviewApi.cs`，submit 时按 `taskId` 读取 correlation 再组装 response。

## Verification

1. 提交 review 后 workflow 可继续
2. reject 会落失败状态
3. `review_tasks` 已持久化 `request_id/engine_run_id/engine_checkpoint_ref`

## Done Criteria

- review gate 可挂起/恢复 SQL workflow
- 后端不依赖内存态关联 review response
