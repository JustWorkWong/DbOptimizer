namespace DbOptimizer.Infrastructure.Workflows.Events;

/// <summary>
/// 工作流进度计算器实现
/// 根据不同工作流类型的执行步骤计算进度
/// </summary>
public sealed class WorkflowProgressCalculator : IWorkflowProgressCalculator
{
    private static readonly Dictionary<string, int> SqlAnalysisSteps = new()
    {
        ["SqlParserExecutor"] = 1,
        ["ExecutionPlanExecutor"] = 2,
        ["IndexAdvisorExecutor"] = 3,
        ["CoordinatorExecutor"] = 4,
        ["HumanReviewExecutor"] = 5,
        ["RegenerationExecutor"] = 6
    };

    private static readonly Dictionary<string, int> DbConfigOptimizationSteps = new()
    {
        ["ConfigCollectorExecutor"] = 1,
        ["ConfigAnalyzerExecutor"] = 2,
        ["ConfigCoordinatorExecutor"] = 3,
        ["ConfigReviewExecutor"] = 4
    };

    public int GetProgressPercent(string workflowType, string nodeName, string status)
    {
        var steps = workflowType switch
        {
            "SqlAnalysis" => SqlAnalysisSteps,
            "DbConfigOptimization" => DbConfigOptimizationSteps,
            _ => null
        };

        if (steps is null || !steps.TryGetValue(nodeName, out var currentStep))
        {
            return 0;
        }

        var totalSteps = steps.Count;
        var baseProgress = (currentStep - 1) * 100 / totalSteps;
        var stepProgress = 100 / totalSteps;

        return status switch
        {
            "Running" => baseProgress + stepProgress / 2,
            "Completed" or "WaitingReview" => baseProgress + stepProgress,
            _ => baseProgress
        };
    }
}
