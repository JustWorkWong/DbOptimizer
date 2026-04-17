using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

/* =========================
 * ConfigAnalyzerMafExecutor
 * 职责：通过 IConfigRuleEngine 分析配置并生成建议
 * ========================= */
public sealed class ConfigAnalyzerMafExecutor(
    IConfigRuleEngine configRuleEngine,
    ILogger<ConfigAnalyzerMafExecutor> logger)
    : Executor<ConfigSnapshotCollectedMessage, ConfigRecommendationsGeneratedMessage>("ConfigAnalyzerMafExecutor")
{
    public override ValueTask<ConfigRecommendationsGeneratedMessage> HandleAsync(
        ConfigSnapshotCollectedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "开始分析配置。SessionId={SessionId}, DatabaseType={DatabaseType}",
            message.SessionId,
            message.Snapshot.DatabaseType);

        var snapshot = new DbConfigSnapshot
        {
            DatabaseType = message.Snapshot.DatabaseType,
            DatabaseId = message.Snapshot.DatabaseId,
            Parameters = message.Snapshot.Parameters.Select(p => new ConfigParameter
            {
                Name = p.Name,
                Value = p.Value,
                DefaultValue = p.DefaultValue,
                Description = p.Description,
                IsDynamic = p.IsDynamic,
                Type = p.Type,
                MinValue = p.MinValue,
                MaxValue = p.MaxValue
            }).ToList(),
            Metrics = new SystemMetrics
            {
                CpuCores = message.Snapshot.Metrics.CpuCores,
                TotalMemoryBytes = message.Snapshot.Metrics.TotalMemoryBytes,
                AvailableMemoryBytes = message.Snapshot.Metrics.AvailableMemoryBytes,
                TotalDiskBytes = message.Snapshot.Metrics.TotalDiskBytes,
                AvailableDiskBytes = message.Snapshot.Metrics.AvailableDiskBytes,
                DatabaseVersion = message.Snapshot.Metrics.DatabaseVersion,
                UptimeSeconds = message.Snapshot.Metrics.UptimeSeconds,
                ActiveConnections = message.Snapshot.Metrics.ActiveConnections,
                MaxConnections = message.Snapshot.Metrics.MaxConnections
            },
            CollectedAt = message.Snapshot.CollectedAt,
            UsedFallback = message.Snapshot.UsedFallback,
            FallbackReason = message.Snapshot.FallbackReason
        };

        var recommendations = configRuleEngine.AnalyzeConfig(snapshot);

        logger.LogInformation(
            "配置分析完成。SessionId={SessionId}, RecommendationCount={RecommendationCount}",
            message.SessionId,
            recommendations.Count);

        var recommendationContracts = recommendations.Select(r => new ConfigRecommendationContract(
            r.ParameterName,
            r.CurrentValue,
            r.RecommendedValue,
            r.Reasoning,
            r.Confidence,
            r.Impact,
            r.RequiresRestart,
            r.EvidenceRefs,
            r.RuleName)).ToList();

        return ValueTask.FromResult(new ConfigRecommendationsGeneratedMessage(
            message.SessionId,
            message.Command,
            message.Snapshot,
            recommendationContracts));
    }
}
