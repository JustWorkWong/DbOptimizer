using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.Infrastructure.Workflows;

/// <summary>
/// Legacy workflow executor result type
/// Used by WorkflowExecutionAuditService and WorkflowEventPayloadFactory
/// </summary>
public sealed record WorkflowExecutorResult(
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
