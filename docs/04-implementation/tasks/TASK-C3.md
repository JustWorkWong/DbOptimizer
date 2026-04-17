# TASK-C3

## Goal

把 SQL workflow 的 MAF 运行事件投影到 `workflow_sessions`、history 和 SSE。

## Dependencies

- TASK-A1
- TASK-A2
- TASK-C1
- TASK-C2
- TASK-D1

## Read First

1. [../../03-design/api/SSE_EVENT_CONTRACT.md](../../03-design/api/SSE_EVENT_CONTRACT.md)
2. [../../03-design/api/DASHBOARD_API_CONTRACT.md](../../03-design/api/DASHBOARD_API_CONTRACT.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/Workflows/Projection/IWorkflowProjectionWriter.cs`
   - `Task ApplyAsync(Guid sessionId, string workflowType, IReadOnlyList<object> mafEvents, CancellationToken cancellationToken = default);`
2. `src/DbOptimizer.Infrastructure/Workflows/Projection/WorkflowProjectionWriter.cs`
3. `src/DbOptimizer.Infrastructure/Workflows/Events/IMafWorkflowEventAdapter.cs`
4. `src/DbOptimizer.Infrastructure/Workflows/Events/MafWorkflowEventAdapter.cs`
5. `src/DbOptimizer.Infrastructure/Workflows/Events/IWorkflowProgressCalculator.cs`
6. `src/DbOptimizer.Infrastructure/Workflows/Events/WorkflowProgressCalculator.cs`
7. `src/DbOptimizer.Infrastructure/Workflows/Monitoring/ITokenUsageRecorder.cs`
   - `Task RecordAsync(Guid sessionId, string executorName, int promptTokens, int completionTokens, CancellationToken cancellationToken = default);`
8. `src/DbOptimizer.Infrastructure/Workflows/Monitoring/TokenUsageRecorder.cs`

## Files To Modify

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Api/WorkflowEventsApi.cs`
- `src/DbOptimizer.API/Program.cs`

## Steps

1. 建立 MAF event -> business event 的适配器。
2. 建立 workflow progress calculator。
3. 建立 Token usage recorder（记录每个 executor 的 token 消耗）。
4. 建立 projection writer，更新 `workflow_sessions.state/status/result_type`。
5. 更新 history/replay 查询使用新投影结果。
6. 更新 SSE snapshot 和 event 输出。
7. 注意：先实现 SQL workflow 投影，D1 完成后补配置 workflow 投影。

## Verification

1. `GET /api/history/{sessionId}` 返回统一结果
2. SSE 可看到 `review.requested` / `workflow.completed`
3. progress 按 workflow 类型计算

## Done Criteria

- SQL workflow 已有完整状态、history、SSE 投影
- 不再依赖旧 SQL 6 步固定进度算法
