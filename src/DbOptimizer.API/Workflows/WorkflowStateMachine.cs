using DbOptimizer.API.Checkpointing;

namespace DbOptimizer.API.Workflows;

internal interface IWorkflowStateMachine
{
    bool CanTransition(WorkflowCheckpointStatus currentStatus, WorkflowCheckpointStatus nextStatus);

    void EnsureCanTransition(WorkflowCheckpointStatus currentStatus, WorkflowCheckpointStatus nextStatus);
}

internal sealed class WorkflowStateMachine : IWorkflowStateMachine
{
    private static readonly IReadOnlyDictionary<WorkflowCheckpointStatus, WorkflowCheckpointStatus[]> AllowedTransitions =
        new Dictionary<WorkflowCheckpointStatus, WorkflowCheckpointStatus[]>
        {
            [WorkflowCheckpointStatus.Running] =
            [
                WorkflowCheckpointStatus.Running,
                WorkflowCheckpointStatus.WaitingForReview,
                WorkflowCheckpointStatus.Completed,
                WorkflowCheckpointStatus.Failed,
                WorkflowCheckpointStatus.Cancelled
            ],
            [WorkflowCheckpointStatus.WaitingForReview] =
            [
                WorkflowCheckpointStatus.Running,
                WorkflowCheckpointStatus.Completed,
                WorkflowCheckpointStatus.Failed,
                WorkflowCheckpointStatus.Cancelled
            ],
            [WorkflowCheckpointStatus.Completed] = [],
            [WorkflowCheckpointStatus.Failed] = [],
            [WorkflowCheckpointStatus.Cancelled] = []
        };

    public bool CanTransition(WorkflowCheckpointStatus currentStatus, WorkflowCheckpointStatus nextStatus)
    {
        return AllowedTransitions[currentStatus].Contains(nextStatus);
    }

    public void EnsureCanTransition(WorkflowCheckpointStatus currentStatus, WorkflowCheckpointStatus nextStatus)
    {
        if (!CanTransition(currentStatus, nextStatus))
        {
            throw new InvalidOperationException($"Workflow status cannot transition from {currentStatus} to {nextStatus}.");
        }
    }
}
