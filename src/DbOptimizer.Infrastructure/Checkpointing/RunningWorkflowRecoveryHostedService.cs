using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Checkpointing;

/* =========================
 * Running Workflow 恢复预热
 * 设计目标：
 * 1) API 重启后扫描 PostgreSQL 中仍处于 Running 的会话
 * 2) 将其 Checkpoint 重新放回 Redis 热缓存，供后续恢复逻辑直接读取
 * 3) 单个会话恢复失败不阻断整体启动
 * ========================= */
public sealed class RunningWorkflowRecoveryHostedService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ICheckpointStorage checkpointStorage,
    ILogger<RunningWorkflowRecoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var runningSessionIds = await dbContext.WorkflowSessions
            .AsNoTracking()
            .Where(x => x.Status == WorkflowSessionStatus.Running)
            .Select(x => x.SessionId)
            .ToListAsync(cancellationToken);

        foreach (var sessionId in runningSessionIds)
        {
            try
            {
                var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
                if (checkpoint is null)
                {
                    logger.LogWarning("Running session {SessionId} has no recoverable checkpoint payload.", sessionId);
                    continue;
                }

                logger.LogInformation(
                    "Recovered running session {SessionId} to checkpoint cache at executor {CurrentExecutor}.",
                    checkpoint.SessionId,
                    checkpoint.CurrentExecutor);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover running session {SessionId} during startup.", sessionId);
            }
        }

        logger.LogInformation("Running workflow recovery warmup finished. Session count: {Count}.", runningSessionIds.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
