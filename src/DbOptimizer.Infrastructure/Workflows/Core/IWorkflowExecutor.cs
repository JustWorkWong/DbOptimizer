using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.Infrastructure.Workflows;

internal interface IWorkflowExecutor
{
    string Name { get; }

    Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default);
}

internal sealed record WorkflowExecutorResult(
    bool IsSuccess,
    WorkflowCheckpointStatus NextStatus,
    object? Output,
    string? ErrorMessage)
{
    public static WorkflowExecutorResult Success(object? output = null)
    {
        return new WorkflowExecutorResult(true, WorkflowCheckpointStatus.Running, output, null);
    }

    public static WorkflowExecutorResult WaitingForReview(object? output = null)
    {
        return new WorkflowExecutorResult(true, WorkflowCheckpointStatus.WaitingForReview, output, null);
    }

    public static WorkflowExecutorResult Completed(object? output = null)
    {
        return new WorkflowExecutorResult(true, WorkflowCheckpointStatus.Completed, output, null);
    }

    public static WorkflowExecutorResult Failure(string errorMessage, object? output = null)
    {
        return new WorkflowExecutorResult(false, WorkflowCheckpointStatus.Failed, output, errorMessage);
    }
}

internal sealed record WorkflowRunResult(
    Guid SessionId,
    WorkflowCheckpointStatus Status,
    string CurrentExecutor,
    IReadOnlyCollection<string> CompletedExecutors,
    int CheckpointVersion,
    string? ErrorMessage);
