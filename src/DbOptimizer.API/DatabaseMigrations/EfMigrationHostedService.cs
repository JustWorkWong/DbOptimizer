using DbOptimizer.API.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.DatabaseMigrations;

/* =========================
 * EF Core 迁移托管服务
 * 设计目标：
 * 1) API 启动阶段执行 EF Core 迁移
 * 2) 迁移完成后再开放健康就绪
 * 3) 失败时阻断启动并记录错误摘要
 * ========================= */
internal sealed class EfMigrationHostedService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    MigrationReadinessState readinessState,
    ILogger<EfMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);

            readinessState.MarkReady();
            logger.LogInformation("EF Core migrations completed successfully.");
        }
        catch (Exception ex)
        {
            readinessState.MarkFailed(ex.Message);
            logger.LogError(ex, "EF Core migration failed during startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
