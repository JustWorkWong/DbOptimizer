# Workflow Context And Checkpoints

## 设计目标

切到 MAF 后，系统需要 3 层状态：

1. MAF runtime state
2. 业务可查询 workflow state
3. 领域结果对象

这三层不能再混在单一 `WorkflowContext` 里。

## 核心模型

### `MafCheckpointEnvelope`

```csharp
public sealed record MafCheckpointEnvelope(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string RunId,
    string CheckpointRef,
    string Status,
    string? CurrentNode,
    JsonElement SharedState,
    IReadOnlyList<PendingRequestEnvelope> PendingRequests,
    DateTimeOffset UpdatedAt);
```

### `PendingRequestEnvelope`

```csharp
public sealed record PendingRequestEnvelope(
    string RequestId,
    string RequestType,
    JsonElement Payload,
    DateTimeOffset CreatedAt);
```

## 业务状态对象

### SQL workflow state

```csharp
public sealed class SqlAnalysisWorkflowState
{
    public Guid SessionId { get; init; }
    public string DatabaseId { get; init; } = string.Empty;
    public string DatabaseEngine { get; init; } = string.Empty;
    public string SqlText { get; init; } = string.Empty;
    public string SourceType { get; init; } = "manual";
    public Guid? SourceRefId { get; init; }
    public WorkflowResultEnvelope? DraftResult { get; set; }
    public WorkflowResultEnvelope? FinalResult { get; set; }
}
```

### Config workflow state

```csharp
public sealed class DbConfigWorkflowState
{
    public Guid SessionId { get; init; }
    public string DatabaseId { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public WorkflowResultEnvelope? DraftResult { get; set; }
    public WorkflowResultEnvelope? FinalResult { get; set; }
}
```

## 状态读写接口

`src/DbOptimizer.Infrastructure/Workflows/State/IWorkflowStateStore.cs`

```csharp
public interface IWorkflowStateStore
{
    Task SaveSqlStateAsync(Guid sessionId, SqlAnalysisWorkflowState state, CancellationToken cancellationToken = default);
    Task<SqlAnalysisWorkflowState?> GetSqlStateAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task SaveDbConfigStateAsync(Guid sessionId, DbConfigWorkflowState state, CancellationToken cancellationToken = default);
    Task<DbConfigWorkflowState?> GetDbConfigStateAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);
    Task<MafCheckpointEnvelope?> GetCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
```

## Session 投影接口

```csharp
public interface IWorkflowSessionProjectionService
{
    Task InitializeAsync(Guid sessionId, string workflowType, string sourceType, Guid? sourceRefId, CancellationToken cancellationToken = default);
    Task MarkRunningAsync(Guid sessionId, string currentNode, MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);
    Task MarkWaitingForReviewAsync(Guid sessionId, string currentNode, MafCheckpointEnvelope checkpoint, Guid reviewTaskId, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(Guid sessionId, WorkflowResultEnvelope result, MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid sessionId, string errorMessage, MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);
    Task MarkCancelledAsync(Guid sessionId, MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);
}
```

## 数据库变更建议

`workflow_sessions` 新增：

- `engine_type`
- `engine_run_id`
- `engine_checkpoint_ref`
- `engine_state`
- `source_type`
- `source_ref_id`
- `result_type`

`slow_queries` 新增：

- `latest_analysis_session_id`
