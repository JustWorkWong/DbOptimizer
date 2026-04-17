# SSE Event Contract

## 目标

SSE 不直接透传 MAF 原始事件，而是透传“业务可消费事件”。

## 事件通道

`GET /api/workflows/{sessionId}/events`

服务端发送：

- `snapshot`
- `workflow-event`
- `heartbeat`

## `snapshot`

首次连接后立即发送一次 workflow 快照。

## `workflow-event`

### 公共结构

```json
{
  "sequence": 12,
  "eventType": "executor.completed",
  "sessionId": "11111111-1111-1111-1111-111111111111",
  "workflowType": "SqlAnalysis",
  "timestamp": "2026-04-17T09:00:05Z",
  "payload": {}
}
```

### 允许值

- `workflow.started`
- `executor.started`
- `executor.completed`
- `executor.failed`
- `review.requested`
- `review.resolved`
- `checkpoint.saved`
- `workflow.completed`
- `workflow.failed`
- `workflow.cancelled`

### 规则

`progressPercent` 必须按 workflow 类型计算，禁止再固定为 SQL 6 步。

## 后端适配类

`src/DbOptimizer.Infrastructure/Workflows/Events/MafWorkflowEventAdapter.cs`

```csharp
public interface IMafWorkflowEventAdapter
{
    WorkflowEventRecord CreateSnapshot(WorkflowStatusResponse snapshot);
    IReadOnlyList<WorkflowEventRecord> Map(Guid sessionId, string workflowType, IReadOnlyList<object> mafEvents);
}
```

`src/DbOptimizer.Infrastructure/Workflows/Events/WorkflowProgressCalculator.cs`

```csharp
public interface IWorkflowProgressCalculator
{
    int GetProgressPercent(string workflowType, string nodeName, string status);
}
```
