using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * HumanReviewExecutor
 * 职责：
 * 1) 创建待审阅任务
 * 2) 将 ReviewId / ReviewStatus 写回上下文
 * 3) 将 Workflow 切换到 WaitingForReview，等待后续用户动作驱动恢复
 * ========================= */
public sealed class HumanReviewExecutor(
    IReviewTaskService reviewTaskService,
    ILogger<HumanReviewExecutor> logger) : IWorkflowExecutor
{
    public string Name => "HumanReviewExecutor";

    public async Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.TryGet<OptimizationReport>(WorkflowContextKeys.FinalResult, out var finalResult) || finalResult is null)
        {
            return WorkflowExecutorResult.Failure("HumanReviewExecutor 缺少 FinalResult 上下文。");
        }

        var reviewId = await reviewTaskService.CreateAsync(context.SessionId, finalResult, cancellationToken);

        context.Set(WorkflowContextKeys.ReviewId, reviewId);
        context.Set(WorkflowContextKeys.ReviewStatus, "Pending");

        logger.LogInformation(
            "Human review executor parked workflow for review. SessionId={SessionId}, ReviewId={ReviewId}",
            context.SessionId,
            reviewId);

        return WorkflowExecutorResult.WaitingForReview(new
        {
            reviewId,
            status = "Pending",
            recommendationCount = finalResult.IndexRecommendations.Count
        });
    }
}
