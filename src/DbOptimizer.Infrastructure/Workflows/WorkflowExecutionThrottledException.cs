namespace DbOptimizer.Infrastructure.Workflows;

public sealed class WorkflowExecutionThrottledException : Exception
{
    public WorkflowExecutionThrottledException(
        string workflowType,
        int totalLimit,
        int workflowTypeLimit,
        int totalActiveRuns,
        int workflowTypeActiveRuns)
        : base(
            $"Workflow execution concurrency limit reached for '{workflowType}'. " +
            $"Active total runs: {totalActiveRuns}/{totalLimit}, active workflow runs: {workflowTypeActiveRuns}/{workflowTypeLimit}.")
    {
        WorkflowType = workflowType;
        TotalLimit = totalLimit;
        WorkflowTypeLimit = workflowTypeLimit;
        TotalActiveRuns = totalActiveRuns;
        WorkflowTypeActiveRuns = workflowTypeActiveRuns;
    }

    public string WorkflowType { get; }

    public int TotalLimit { get; }

    public int WorkflowTypeLimit { get; }

    public int TotalActiveRuns { get; }

    public int WorkflowTypeActiveRuns { get; }
}
