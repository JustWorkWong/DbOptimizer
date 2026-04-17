using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

/* =========================
 * ConfigCoordinatorMafExecutor
 * 职责：汇总配置优化结果，生成 WorkflowResultEnvelope
 * ========================= */
public sealed class ConfigCoordinatorMafExecutor(
    ILogger<ConfigCoordinatorMafExecutor> logger)
    : Executor<ConfigRecommendationsGeneratedMessage, DbConfigOptimizationDraftReadyMessage>("ConfigCoordinatorMafExecutor")
{
    public override ValueTask<DbConfigOptimizationDraftReadyMessage> HandleAsync(
        ConfigRecommendationsGeneratedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "开始汇总配置优化结果。SessionId={SessionId}, RecommendationCount={RecommendationCount}",
            message.SessionId,
            message.Recommendations.Count);

        var highImpactCount = message.Recommendations.Count(r => r.Impact.Equals("High", StringComparison.OrdinalIgnoreCase));
        var mediumImpactCount = message.Recommendations.Count(r => r.Impact.Equals("Medium", StringComparison.OrdinalIgnoreCase));
        var lowImpactCount = message.Recommendations.Count(r => r.Impact.Equals("Low", StringComparison.OrdinalIgnoreCase));
        var requiresRestartCount = message.Recommendations.Count(r => r.RequiresRestart);

        var overallConfidence = message.Recommendations.Count > 0
            ? message.Recommendations.Average(r => r.Confidence)
            : 0.0;

        var summary = GenerateSummary(
            message.Snapshot.DatabaseType,
            message.Recommendations.Count,
            highImpactCount,
            mediumImpactCount,
            lowImpactCount,
            requiresRestartCount);

        var reportData = new
        {
            databaseType = message.Snapshot.DatabaseType,
            databaseId = message.Snapshot.DatabaseId,
            summary,
            recommendations = message.Recommendations.Select(r => new
            {
                parameterName = r.ParameterName,
                currentValue = r.CurrentValue,
                recommendedValue = r.RecommendedValue,
                reasoning = r.Reasoning,
                confidence = r.Confidence,
                impact = r.Impact,
                requiresRestart = r.RequiresRestart,
                evidenceRefs = r.EvidenceRefs,
                ruleName = r.RuleName
            }).ToList(),
            overallConfidence,
            generatedAt = DateTimeOffset.UtcNow,
            highImpactCount,
            mediumImpactCount,
            lowImpactCount,
            requiresRestartCount,
            snapshot = new
            {
                collectedAt = message.Snapshot.CollectedAt,
                usedFallback = message.Snapshot.UsedFallback,
                fallbackReason = message.Snapshot.FallbackReason,
                parameterCount = message.Snapshot.Parameters.Count,
                metrics = new
                {
                    cpuCores = message.Snapshot.Metrics.CpuCores,
                    totalMemoryBytes = message.Snapshot.Metrics.TotalMemoryBytes,
                    availableMemoryBytes = message.Snapshot.Metrics.AvailableMemoryBytes,
                    databaseVersion = message.Snapshot.Metrics.DatabaseVersion,
                    uptimeSeconds = message.Snapshot.Metrics.UptimeSeconds,
                    activeConnections = message.Snapshot.Metrics.ActiveConnections,
                    maxConnections = message.Snapshot.Metrics.MaxConnections
                }
            }
        };

        var metadata = new
        {
            sessionId = message.SessionId,
            workflowType = "DbConfigOptimization",
            generatedAt = DateTimeOffset.UtcNow,
            databaseType = message.Snapshot.DatabaseType,
            databaseId = message.Snapshot.DatabaseId
        };

        var envelope = new WorkflowResultEnvelope
        {
            ResultType = "db-config-optimization-report",
            DisplayName = $"{message.Snapshot.DatabaseType} 配置优化报告",
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(reportData),
            Metadata = JsonSerializer.SerializeToElement(metadata)
        };

        logger.LogInformation(
            "配置优化结果汇总完成。SessionId={SessionId}, OverallConfidence={OverallConfidence:F2}",
            message.SessionId,
            overallConfidence);

        return ValueTask.FromResult(new DbConfigOptimizationDraftReadyMessage(
            message.SessionId,
            envelope));
    }

    private static string GenerateSummary(
        string databaseType,
        int totalCount,
        int highImpactCount,
        int mediumImpactCount,
        int lowImpactCount,
        int requiresRestartCount)
    {
        if (totalCount == 0)
        {
            return $"{databaseType} 配置分析完成，未发现需要优化的配置项。";
        }

        var parts = new List<string>
        {
            $"{databaseType} 配置分析完成，共发现 {totalCount} 项优化建议"
        };

        if (highImpactCount > 0)
        {
            parts.Add($"{highImpactCount} 项高影响");
        }

        if (mediumImpactCount > 0)
        {
            parts.Add($"{mediumImpactCount} 项中等影响");
        }

        if (lowImpactCount > 0)
        {
            parts.Add($"{lowImpactCount} 项低影响");
        }

        var summary = string.Join("，", parts) + "。";

        if (requiresRestartCount > 0)
        {
            summary += $" 其中 {requiresRestartCount} 项需要重启数据库生效。";
        }

        return summary;
    }
}
