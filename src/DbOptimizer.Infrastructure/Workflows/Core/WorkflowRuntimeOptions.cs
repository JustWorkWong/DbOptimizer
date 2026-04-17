using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

public sealed class WorkflowRuntimeOptions
{
    public const string SectionName = "DbOptimizer:Workflow";

    public int StepTimeoutSeconds { get; set; } = 120;

    public int MaxRetryCount { get; set; } = 3;

    public int RegenerationMaxRounds { get; set; } = 3;
}
