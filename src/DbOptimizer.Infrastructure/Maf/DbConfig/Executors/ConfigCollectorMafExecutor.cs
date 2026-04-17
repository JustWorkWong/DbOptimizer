using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

/* =========================
 * ConfigCollectorMafExecutor
 * 职责：通过 IConfigCollectionProvider 采集配置快照
 * ========================= */
public sealed class ConfigCollectorMafExecutor(
    IConfigCollectionProvider collectionProvider,
    ILogger<ConfigCollectorMafExecutor> logger)
    : Executor<DbConfigWorkflowCommand, ConfigSnapshotCollectedMessage>("ConfigCollectorMafExecutor")
{
    public override async ValueTask<ConfigSnapshotCollectedMessage> HandleAsync(
        DbConfigWorkflowCommand message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "开始采集配置快照。SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}",
            message.SessionId,
            message.DatabaseId,
            message.DatabaseType);

        var databaseEngine = message.DatabaseType.ToLowerInvariant() switch
        {
            "mysql" => DbOptimizer.Core.Models.DatabaseOptimizationEngine.MySql,
            "postgresql" => DbOptimizer.Core.Models.DatabaseOptimizationEngine.PostgreSql,
            _ => throw new InvalidOperationException($"不支持的数据库类型: {message.DatabaseType}")
        };

        var snapshot = await collectionProvider.CollectConfigAsync(
            databaseEngine,
            message.DatabaseId,
            cancellationToken);

        if (snapshot.UsedFallback && !message.AllowFallbackSnapshot)
        {
            logger.LogError(
                "配置采集失败且不允许降级。SessionId={SessionId}, Reason={Reason}",
                message.SessionId,
                snapshot.FallbackReason);

            throw new InvalidOperationException(
                $"配置采集失败: {snapshot.FallbackReason}");
        }

        if (snapshot.UsedFallback)
        {
            logger.LogWarning(
                "配置采集使用降级方案。SessionId={SessionId}, Reason={Reason}",
                message.SessionId,
                snapshot.FallbackReason);
        }
        else
        {
            logger.LogInformation(
                "配置快照采集成功。SessionId={SessionId}, ParameterCount={ParameterCount}",
                message.SessionId,
                snapshot.Parameters.Count);
        }

        var snapshotContract = new DbConfigSnapshotContract(
            snapshot.DatabaseType,
            snapshot.DatabaseId,
            snapshot.Parameters.Select(p => new ConfigParameterContract(
                p.Name,
                p.Value,
                p.DefaultValue,
                p.Description,
                p.IsDynamic,
                p.Type,
                p.MinValue,
                p.MaxValue)).ToList(),
            new SystemMetricsContract(
                snapshot.Metrics.CpuCores,
                snapshot.Metrics.TotalMemoryBytes,
                snapshot.Metrics.AvailableMemoryBytes,
                snapshot.Metrics.TotalDiskBytes,
                snapshot.Metrics.AvailableDiskBytes,
                snapshot.Metrics.DatabaseVersion,
                snapshot.Metrics.UptimeSeconds,
                snapshot.Metrics.ActiveConnections,
                snapshot.Metrics.MaxConnections),
            snapshot.CollectedAt,
            snapshot.UsedFallback,
            snapshot.FallbackReason);

        return new ConfigSnapshotCollectedMessage(
            message.SessionId,
            message,
            snapshotContract);
    }
}
