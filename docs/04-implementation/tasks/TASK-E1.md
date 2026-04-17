# TASK-E1

## Goal

把 slow query 采集结果自动提交为 SQL workflow，并回写关联 session。

## Dependencies

- TASK-A2
- TASK-C1
- TASK-C2

## Read First

1. [../../03-design/api/DASHBOARD_API_CONTRACT.md](../../03-design/api/DASHBOARD_API_CONTRACT.md)
2. [../../03-design/data/DATA_MODEL.md](../../03-design/data/DATA_MODEL.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/SlowQuery/ISlowQueryWorkflowSubmissionService.cs`
   - `Task<Guid> SubmitAsync(SlowQueryEntity slowQuery, CancellationToken cancellationToken = default);`
2. `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryWorkflowSubmissionService.cs`

## Files To Modify

- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs`
- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`
- `src/DbOptimizer.API/Program.cs`

## Steps

1. 新建 slow query submission service。
2. 由 `SlowQueryCollectionService` 在保存后调用 submission service。
3. 创建 SQL workflow 时固定：
   - `sourceType=slow-query`
   - `sourceRefId=queryId`
4. 回写 `slow_queries.latest_analysis_session_id`。

## Verification

1. 新 slow query 入库后能自动创建 workflow
2. `latest_analysis_session_id` 非空
3. `workflow_sessions.source_type=slow-query`

## Done Criteria

- slow query 到 workflow 的自动闭环已建立
- 双向追踪字段可查询
