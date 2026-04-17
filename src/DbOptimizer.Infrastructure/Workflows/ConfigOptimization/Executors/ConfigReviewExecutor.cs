using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * ConfigReviewExecutor
 * 职责：
 * 1) 创建配置优化审阅任务
 * 2) 将 ReviewId / ReviewStatus 写回上下文
 * 3) 将 Workflow 切换到 WaitingForReview，等待后续用户动作驱动恢复
 * ========================= */
internal sealed class ConfigReviewExecutor(
    IConfigReviewTaskService configReviewTaskService,
    ILogger<ConfigReviewExecutor> logger) : IWorkflowExecutor
{
    public string Name => "ConfigReviewExecutor";

    public async Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.TryGet<ConfigOptimizationReport>(WorkflowContextKeys.FinalResult, out var finalResult) || finalResult is null)
        {
            return WorkflowExecutorResult.Failure("ConfigReviewExecutor 缺少 FinalResult 上下文。");
        }

        var reviewId = await configReviewTaskService.CreateAsync(context.SessionId, finalResult, cancellationToken);

        context.Set(WorkflowContextKeys.ReviewId, reviewId);
        context.Set(WorkflowContextKeys.ReviewStatus, "Pending");

        logger.LogInformation(
            "Config review executor parked workflow for review. SessionId={SessionId}, ReviewId={ReviewId}",
            context.SessionId,
            reviewId);

        return WorkflowExecutorResult.WaitingForReview(new
        {
            reviewId,
            status = "Pending",
            recommendationCount = finalResult.Recommendations.Count
        });
    }
}
