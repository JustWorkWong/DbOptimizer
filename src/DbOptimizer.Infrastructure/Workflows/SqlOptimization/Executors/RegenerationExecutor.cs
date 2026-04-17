using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * RegenerationExecutor
 * 职责：
 * 1) 读取驳回原因与当前最终结果
 * 2) 控制回流次数，避免无限重生成
 * 3) 生成带有反馈痕迹的新汇总结果，供后续再次进入 HumanReview
 * ========================= */
internal sealed class RegenerationExecutor(
    WorkflowRuntimeOptions workflowRuntimeOptions,
    ILogger<RegenerationExecutor> logger) : IWorkflowExecutor
{
    public string Name => "RegenerationExecutor";

    public Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.TryGet<OptimizationReport>(WorkflowContextKeys.FinalResult, out var finalResult) || finalResult is null)
        {
            return Task.FromResult(WorkflowExecutorResult.Failure("RegenerationExecutor 缺少 FinalResult 上下文。"));
        }

        if (!context.TryGet<string>(WorkflowContextKeys.RejectionReason, out var rejectionReason) ||
            string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Task.FromResult(WorkflowExecutorResult.Failure("RegenerationExecutor 缺少驳回原因。"));
        }

        var currentRound = 0;
        if (context.TryGet<int>(WorkflowContextKeys.RegenerationCount, out var round))
        {
            currentRound = round;
        }

        if (currentRound >= workflowRuntimeOptions.RegenerationMaxRounds)
        {
            return Task.FromResult(WorkflowExecutorResult.Failure(
                $"已达到最大回流次数 {workflowRuntimeOptions.RegenerationMaxRounds}，停止继续重生成。"));
        }

        var nextRound = currentRound + 1;
        var regeneratedReport = BuildRegeneratedReport(finalResult, rejectionReason, nextRound);

        context.Set(WorkflowContextKeys.RegenerationCount, nextRound);
        context.Set(WorkflowContextKeys.FinalResult, regeneratedReport);
        context.Set(WorkflowContextKeys.ReviewStatus, "Rejected");

        logger.LogInformation(
            "Regeneration executor completed. SessionId={SessionId}, Round={Round}, RejectionReason={RejectionReason}",
            context.SessionId,
            nextRound,
            rejectionReason);

        return Task.FromResult(WorkflowExecutorResult.Success(regeneratedReport));
    }

    private static OptimizationReport BuildRegeneratedReport(
        OptimizationReport originalReport,
        string rejectionReason,
        int round)
    {
        var report = new OptimizationReport
        {
            Summary = $"已根据驳回意见触发第 {round} 轮重生成。驳回原因：{rejectionReason}",
            IndexRecommendations = originalReport.IndexRecommendations
                .Select(recommendation => new IndexRecommendation
                {
                    TableName = recommendation.TableName,
                    Columns = recommendation.Columns.ToList(),
                    IndexType = recommendation.IndexType,
                    CreateDdl = recommendation.CreateDdl,
                    EstimatedBenefit = recommendation.EstimatedBenefit,
                    Reasoning = $"{recommendation.Reasoning} 已纳入驳回反馈：{rejectionReason}",
                    EvidenceRefs = recommendation.EvidenceRefs.ToList(),
                    Confidence = Math.Max(0.3, Math.Round(recommendation.Confidence - 0.05, 2))
                })
                .ToList(),
            SqlRewriteSuggestions = originalReport.SqlRewriteSuggestions
                .Select(suggestion => new SqlRewriteSuggestion
                {
                    Description = suggestion.Description,
                    Reasoning = suggestion.Reasoning,
                    Confidence = suggestion.Confidence
                })
                .ToList(),
            OverallConfidence = Math.Max(0.3, Math.Round(originalReport.OverallConfidence - 0.05, 2)),
            EvidenceChain = originalReport.EvidenceChain
                .Select(item => new EvidenceItem
                {
                    SourceType = item.SourceType,
                    Reference = item.Reference,
                    Description = item.Description,
                    Confidence = item.Confidence,
                    Snippet = item.Snippet
                })
                .ToList(),
            Warnings = originalReport.Warnings.ToList(),
            Metadata = new Dictionary<string, object>(originalReport.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        report.Warnings.Add($"第 {round} 轮重生成已记录驳回原因：{rejectionReason}");
        report.EvidenceChain.Add(new EvidenceItem
        {
            SourceType = "ReviewFeedback",
            Reference = $"regeneration_round:{round}",
            Description = $"人工驳回后触发第 {round} 轮重生成。",
            Confidence = 1,
            Snippet = rejectionReason
        });
        report.Metadata["regenerationRound"] = round;
        report.Metadata["rejectionReason"] = rejectionReason;
        report.Metadata["resumedFrom"] = "HumanReviewExecutor";
        return report;
    }
}
