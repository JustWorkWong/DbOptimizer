using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * ConfigCoordinatorExecutor
 * 职责：
 * 1) 汇总 ConfigSnapshot / ConfigRecommendations
 * 2) 输出最终可审阅的 ConfigOptimizationReport
 * 3) 维护简洁的 summary、confidence 与统计信息
 * ========================= */
public sealed class ConfigCoordinatorExecutor(ILogger<ConfigCoordinatorExecutor> logger) : IWorkflowExecutor
{
    public string Name => "ConfigCoordinatorExecutor";

    public Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var snapshot, out var recommendations))
        {
            return Task.FromResult(WorkflowExecutorResult.Failure("ConfigCoordinatorExecutor 缺少汇总所需的上下文数据。"));
        }

        var report = BuildReport(snapshot, recommendations);
        context.Set(WorkflowContextKeys.FinalResult, report);

        logger.LogInformation(
            "Config coordinator executor completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}, OverallConfidence={OverallConfidence}",
            context.SessionId,
            report.Recommendations.Count,
            report.OverallConfidence);

        return Task.FromResult(WorkflowExecutorResult.Success(report));
    }

    private static bool TryResolveInput(
        WorkflowContext context,
        out DbConfigSnapshot snapshot,
        out List<ConfigRecommendation> recommendations)
    {
        snapshot = null!;
        recommendations = new List<ConfigRecommendation>();

        return context.TryGet<DbConfigSnapshot>(WorkflowContextKeys.ConfigSnapshot, out var snap) &&
               snap is not null &&
               context.TryGet<List<ConfigRecommendation>>(WorkflowContextKeys.ConfigRecommendations, out var recs) &&
               recs is not null &&
               Assign(snap, recs, out snapshot, out recommendations);
    }

    private static bool Assign(
        DbConfigSnapshot snap,
        List<ConfigRecommendation> recs,
        out DbConfigSnapshot snapshot,
        out List<ConfigRecommendation> recommendations)
    {
        snapshot = snap;
        recommendations = recs;
        return true;
    }

    private static ConfigOptimizationReport BuildReport(
        DbConfigSnapshot snapshot,
        IReadOnlyList<ConfigRecommendation> recommendations)
    {
        var highImpact = recommendations.Count(r => r.Impact.Equals("High", StringComparison.OrdinalIgnoreCase));
        var mediumImpact = recommendations.Count(r => r.Impact.Equals("Medium", StringComparison.OrdinalIgnoreCase));
        var lowImpact = recommendations.Count(r => r.Impact.Equals("Low", StringComparison.OrdinalIgnoreCase));
        var requiresRestart = recommendations.Count(r => r.RequiresRestart);

        var report = new ConfigOptimizationReport
        {
            Summary = BuildSummary(recommendations, highImpact, mediumImpact, lowImpact, requiresRestart),
            Recommendations = recommendations.ToList(),
            OverallConfidence = CalculateOverallConfidence(recommendations),
            GeneratedAt = DateTimeOffset.UtcNow,
            DatabaseType = snapshot.DatabaseType,
            DatabaseId = snapshot.DatabaseId,
            HighImpactCount = highImpact,
            MediumImpactCount = mediumImpact,
            LowImpactCount = lowImpact,
            RequiresRestartCount = requiresRestart,
            Metadata = new Dictionary<string, object>
            {
                ["CollectedAt"] = snapshot.CollectedAt,
                ["UsedFallback"] = snapshot.UsedFallback,
                ["TotalParameters"] = snapshot.Parameters.Count
            }
        };

        return report;
    }

    private static string BuildSummary(
        IReadOnlyList<ConfigRecommendation> recommendations,
        int highImpact,
        int mediumImpact,
        int lowImpact,
        int requiresRestart)
    {
        if (recommendations.Count == 0)
        {
            return "未发现需要优化的配置参数";
        }

        var highList = recommendations
            .Where(r => r.Impact.Equals("High", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.ParameterName)
            .Take(3);

        var highListStr = string.Join(", ", highList);
        if (recommendations.Count(r => r.Impact.Equals("High", StringComparison.OrdinalIgnoreCase)) > 3)
        {
            highListStr += "...";
        }

        var confidence = CalculateOverallConfidence(recommendations);

        return $"发现 {recommendations.Count} 个配置优化建议：\n" +
               $"- {highImpact} 个高影响建议（{highListStr}）\n" +
               $"- {mediumImpact} 个中等影响建议\n" +
               $"- {lowImpact} 个低影响建议\n" +
               $"其中 {requiresRestart} 个参数需要重启数据库生效。\n" +
               $"整体置信度：{confidence:P0}";
    }

    private static double CalculateOverallConfidence(IReadOnlyList<ConfigRecommendation> recommendations)
    {
        if (recommendations.Count == 0)
        {
            return 0.0;
        }

        double totalWeightedConfidence = 0.0;
        double totalWeight = 0.0;

        foreach (var recommendation in recommendations)
        {
            var weight = recommendation.Impact.ToUpperInvariant() switch
            {
                "HIGH" => 3.0,
                "MEDIUM" => 2.0,
                "LOW" => 1.0,
                _ => 1.0
            };

            totalWeightedConfidence += recommendation.Confidence * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? totalWeightedConfidence / totalWeight : 0.0;
    }
}
