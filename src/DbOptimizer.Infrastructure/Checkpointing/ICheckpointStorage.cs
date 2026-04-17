using System.Text.Json;

namespace DbOptimizer.Infrastructure.Checkpointing;

internal interface ICheckpointStorage
{
    Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default);

    Task<WorkflowCheckpoint?> LoadCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task DeleteCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

internal sealed record WorkflowCheckpoint
{
    public Guid SessionId { get; init; }

    public string WorkflowType { get; init; } = string.Empty;

    public WorkflowCheckpointStatus Status { get; init; } = WorkflowCheckpointStatus.Running;

    public string CurrentExecutor { get; init; } = string.Empty;

    public int CheckpointVersion { get; init; } = 1;

    public Dictionary<string, JsonElement> Context { get; init; } = new();

    public IReadOnlyList<string> CompletedExecutors { get; init; } = Array.Empty<string>();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastCheckpointAt { get; init; }
}

internal enum WorkflowCheckpointStatus
{
    Running,
    WaitingForReview,
    Completed,
    Failed,
    Cancelled
}
