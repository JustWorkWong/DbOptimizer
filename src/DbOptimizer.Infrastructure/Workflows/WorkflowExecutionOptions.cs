namespace DbOptimizer.Infrastructure.Workflows;

/// <summary>
/// Workflow execution governance settings for the current native MAF runtime.
/// </summary>
public sealed class WorkflowExecutionOptions
{
    public const string SectionName = "WorkflowExecution";

    public bool UseNativeMafCheckpointing { get; set; } = true;

    public bool UseNativeMafReviewRequests { get; set; } = true;

    public bool UseNativeMafEventProjection { get; set; } = true;

    public bool UseUpgradedMafPackages { get; set; } = true;

    public int MaxConcurrentRuns { get; set; } = 10;

    public int MaxConcurrentSqlRuns { get; set; } = 5;

    public int MaxConcurrentConfigRuns { get; set; } = 5;

    public void ValidateForCurrentImplementation()
    {
        if (!UseNativeMafCheckpointing ||
            !UseNativeMafReviewRequests ||
            !UseNativeMafEventProjection ||
            !UseUpgradedMafPackages)
        {
            throw new InvalidOperationException(
                "The current build only supports the native MAF runtime path. All WorkflowExecution native flags must remain enabled.");
        }

        if (MaxConcurrentRuns <= 0)
        {
            throw new InvalidOperationException("WorkflowExecution:MaxConcurrentRuns must be greater than zero.");
        }

        if (MaxConcurrentSqlRuns <= 0)
        {
            throw new InvalidOperationException("WorkflowExecution:MaxConcurrentSqlRuns must be greater than zero.");
        }

        if (MaxConcurrentConfigRuns <= 0)
        {
            throw new InvalidOperationException("WorkflowExecution:MaxConcurrentConfigRuns must be greater than zero.");
        }

        if (MaxConcurrentSqlRuns > MaxConcurrentRuns)
        {
            throw new InvalidOperationException("WorkflowExecution:MaxConcurrentSqlRuns cannot exceed WorkflowExecution:MaxConcurrentRuns.");
        }

        if (MaxConcurrentConfigRuns > MaxConcurrentRuns)
        {
            throw new InvalidOperationException("WorkflowExecution:MaxConcurrentConfigRuns cannot exceed WorkflowExecution:MaxConcurrentRuns.");
        }
    }
}
