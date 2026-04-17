# TASK-A2

## Goal

扩展 `workflow_sessions`、`review_tasks`、`slow_queries`，为 MAF 运行态、review correlation 和 slow query 追踪准备字段。

## Dependencies

- 无

## Read First

1. [../../03-design/data/DATA_MODEL.md](../../03-design/data/DATA_MODEL.md)
2. [../../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md](../../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md)
3. [../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md](../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md)

## Files To Modify

- `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`
- `src/DbOptimizer.Infrastructure/Persistence/*Migration*.cs`

## Database Changes

### `workflow_sessions`

- `engine_type`
- `engine_run_id`
- `engine_checkpoint_ref`
- `engine_state`
- `result_type`
- `source_type`
- `source_ref_id`

### `review_tasks`

- `request_id`
- `engine_run_id`
- `engine_checkpoint_ref`

### `slow_queries`

- `latest_analysis_session_id`

## Steps

1. 修改 `DbOptimizerDbContext` entity mapping。
2. 给 `WorkflowSessionEntity`、`ReviewTaskEntity`、`SlowQueryEntity` 增加属性。
3. 生成 migration。
4. 确认索引策略：
   - `workflow_sessions.result_type`
   - `review_tasks.request_id`
   - `slow_queries.latest_analysis_session_id`

## Verification

1. migration 可生成
2. `dotnet build` 通过
3. migration SQL 与 `DATA_MODEL.md` 一致

## Done Criteria

- 所有新字段进入 EF 模型
- migration 可应用
- review correlation 字段被声明为持久化必填字段
