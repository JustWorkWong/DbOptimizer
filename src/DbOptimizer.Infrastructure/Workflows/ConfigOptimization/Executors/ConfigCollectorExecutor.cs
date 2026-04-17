using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using ModelContextProtocol.Client;

namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * ConfigCollectorExecutor
 * 职责：
 * 1) 从 WorkflowContext 读取 DatabaseId, DatabaseType
 * 2) 调用 ConfigCollectionProvider 收集配置
 * 3) 写入 WorkflowContext: ConfigSnapshot
 * 4) 记录收集的参数数量、耗时
 * ========================= */
public sealed class ConfigCollectorExecutor(
    IConfigCollectionProvider configCollectionProvider,
    ILogger<ConfigCollectorExecutor> logger) : IWorkflowExecutor
{
    public string Name => "ConfigCollectorExecutor";

    public async Task<WorkflowExecutorResult> ExecuteAsync(
        WorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var databaseId, out var databaseEngine))
        {
            return WorkflowExecutorResult.Failure("WorkflowContext 中缺少 DatabaseId 或 DatabaseType。");
        }

        var startTime = DateTimeOffset.UtcNow;

        var snapshot = await configCollectionProvider.CollectConfigAsync(
            databaseEngine,
            databaseId,
            cancellationToken);

        var elapsed = DateTimeOffset.UtcNow - startTime;

        context.Set(WorkflowContextKeys.ConfigSnapshot, snapshot);

        logger.LogInformation(
            "配置收集完成。SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}, ParameterCount={ParameterCount}, UsedFallback={UsedFallback}, Elapsed={ElapsedMs}ms",
            context.SessionId,
            databaseId,
            databaseEngine,
            snapshot.Parameters.Count,
            snapshot.UsedFallback,
            elapsed.TotalMilliseconds);

        return WorkflowExecutorResult.Success(snapshot);
    }

    private static bool TryResolveInput(
        WorkflowContext context,
        out string databaseId,
        out DbOptimizer.Core.Models.DatabaseOptimizationEngine databaseEngine)
    {
        databaseId = string.Empty;
        databaseEngine = DbOptimizer.Core.Models.DatabaseOptimizationEngine.Unknown;

        if (!context.TryGet<string>(WorkflowContextKeys.DatabaseId, out var dbId) ||
            string.IsNullOrWhiteSpace(dbId))
        {
            return false;
        }

        if (!context.TryGet<string>(WorkflowContextKeys.DatabaseType, out var dbType) ||
            string.IsNullOrWhiteSpace(dbType))
        {
            return false;
        }

        databaseId = dbId;
        databaseEngine = dbType.ToLowerInvariant() switch
        {
            "mysql" => DbOptimizer.Core.Models.DatabaseOptimizationEngine.MySql,
            "postgresql" or "postgres" => DbOptimizer.Core.Models.DatabaseOptimizationEngine.PostgreSql,
            _ => DbOptimizer.Core.Models.DatabaseOptimizationEngine.Unknown
        };

        return databaseEngine != DbOptimizer.Core.Models.DatabaseOptimizationEngine.Unknown;
    }
}
