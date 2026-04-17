using DbOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * 配置分析执行器
 * 从 WorkflowContext 读取配置快照，调用规则引擎生成优化建议
 * ========================= */
public sealed class ConfigAnalyzerExecutor(
    IConfigRuleEngine ruleEngine,
    ILogger<ConfigAnalyzerExecutor> logger) : IWorkflowExecutor
{
    public string Name => "ConfigAnalyzer";

    public async Task<WorkflowExecutorResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[ConfigAnalyzer] 开始分析配置快照");

        if (!context.TryGet<DbConfigSnapshot>("ConfigSnapshot", out var snapshot) || snapshot is null)
        {
            logger.LogWarning("[ConfigAnalyzer] 未找到 ConfigSnapshot，跳过分析");
            return WorkflowExecutorResult.Failure("未找到配置快照数据");
        }

        logger.LogInformation(
            "[ConfigAnalyzer] 配置快照: DatabaseType={DatabaseType}, Parameters={ParameterCount}, UsedFallback={UsedFallback}",
            snapshot.DatabaseType,
            snapshot.Parameters.Count,
            snapshot.UsedFallback);

        var recommendations = await Task.Run(
            () => ruleEngine.AnalyzeConfig(snapshot),
            cancellationToken);

        logger.LogInformation(
            "[ConfigAnalyzer] 生成 {RecommendationCount} 条配置建议",
            recommendations.Count);

        if (recommendations.Count > 0)
        {
            var impactDistribution = recommendations
                .GroupBy(r => r.Impact)
                .ToDictionary(g => g.Key, g => g.Count());

            logger.LogInformation(
                "[ConfigAnalyzer] 影响级别分布: High={High}, Medium={Medium}, Low={Low}",
                impactDistribution.GetValueOrDefault("High", 0),
                impactDistribution.GetValueOrDefault("Medium", 0),
                impactDistribution.GetValueOrDefault("Low", 0));

            var requiresRestartCount = recommendations.Count(r => r.RequiresRestart);
            logger.LogInformation(
                "[ConfigAnalyzer] 需要重启的参数: {RequiresRestartCount}/{TotalCount}",
                requiresRestartCount,
                recommendations.Count);

            foreach (var recommendation in recommendations.Take(5))
            {
                logger.LogInformation(
                    "[ConfigAnalyzer] 建议: {ParameterName} | {CurrentValue} -> {RecommendedValue} | Impact={Impact}, Confidence={Confidence:F2}",
                    recommendation.ParameterName,
                    recommendation.CurrentValue,
                    recommendation.RecommendedValue,
                    recommendation.Impact,
                    recommendation.Confidence);
            }
        }
        else
        {
            logger.LogInformation("[ConfigAnalyzer] 未发现需要优化的配置参数");
        }

        context.Set("ConfigRecommendations", recommendations);

        return WorkflowExecutorResult.Success(new Dictionary<string, object>
        {
            ["RecommendationCount"] = recommendations.Count,
            ["HighImpactCount"] = recommendations.Count(r => r.Impact == "High"),
            ["MediumImpactCount"] = recommendations.Count(r => r.Impact == "Medium"),
            ["LowImpactCount"] = recommendations.Count(r => r.Impact == "Low"),
            ["RequiresRestartCount"] = recommendations.Count(r => r.RequiresRestart),
            ["AverageConfidence"] = recommendations.Count > 0
                ? recommendations.Average(r => r.Confidence)
                : 0.0
        });
    }
}
