namespace DbOptimizer.Infrastructure.Workflows.Events;

/// <summary>
/// 工作流进度计算器实现
/// 根据不同工作流类型的执行步骤计算进度
/// </summary>
public sealed class WorkflowProgressCalculator : IWorkflowProgressCalculator
{
    // MAF executor 命名映射（支持 Legacy 和 MAF 两种命名）
    private static readonly Dictionary<string, int> SqlAnalysisSteps = new()
    {
        // Legacy executors
        ["SqlParserExecutor"] = 1,
        ["ExecutionPlanExecutor"] = 2,
        ["IndexAdvisorExecutor"] = 3,
        ["CoordinatorExecutor"] = 4,
        ["HumanReviewExecutor"] = 5,
        ["RegenerationExecutor"] = 6,

        // MAF executors
        ["SqlInputValidationExecutor"] = 0,
        ["SqlParserMafExecutor"] = 1,
        ["ExecutionPlanMafExecutor"] = 2,
        ["IndexAdvisorMafExecutor"] = 3,
        ["SqlRewriteMafExecutor"] = 3,  // 并行执行，与 IndexAdvisor 同级
        ["SqlCoordinatorMafExecutor"] = 4,
        ["SqlHumanReviewGateExecutor"] = 5
    };

    private static readonly Dictionary<string, int> DbConfigOptimizationSteps = new()
    {
        // Legacy executors
        ["ConfigCollectorExecutor"] = 1,
        ["ConfigAnalyzerExecutor"] = 2,
        ["ConfigCoordinatorExecutor"] = 3,
        ["ConfigReviewExecutor"] = 4,

        // MAF executors
        ["DbConfigInputValidationExecutor"] = 0,
        ["ConfigCollectorMafExecutor"] = 1,
        ["ConfigAnalyzerMafExecutor"] = 2,
        ["ConfigCoordinatorMafExecutor"] = 3,
        ["ConfigHumanReviewGateExecutor"] = 4
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
