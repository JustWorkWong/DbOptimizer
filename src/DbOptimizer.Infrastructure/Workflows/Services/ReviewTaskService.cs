using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Workflows;

public interface IReviewTaskService
{
    Task<Guid> CreateAsync(
        Guid sessionId,
        OptimizationReport report,
        CancellationToken cancellationToken = default);
}

public interface IConfigReviewTaskService
{
    Task<Guid> CreateAsync(
        Guid sessionId,
        ConfigOptimizationReport report,
        CancellationToken cancellationToken = default);
}

/* =========================
 * 审阅任务服务
 * 当前先负责把待审内容持久化到 review_tasks，后续 Review API 直接消费这张表。
 * ========================= */
public sealed class ReviewTaskService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<ReviewTaskService> logger) : IReviewTaskService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(
        Guid sessionId,
        OptimizationReport report,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new ReviewTaskEntity
        {
            SessionId = sessionId,
            TaskType = "SqlOptimization",
            Recommendations = JsonSerializer.Serialize(report, SerializerOptions),
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ReviewTasks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Review task created. SessionId={SessionId}, ReviewTaskId={ReviewTaskId}, TaskType={TaskType}",
            sessionId,
            entity.TaskId,
            entity.TaskType);

        return entity.TaskId;
    }
}

/* =========================
 * 配置优化审阅任务服务
 * ========================= */
public sealed class ConfigReviewTaskService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<ConfigReviewTaskService> logger) : IConfigReviewTaskService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(
        Guid sessionId,
        ConfigOptimizationReport report,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new ReviewTaskEntity
        {
            SessionId = sessionId,
            TaskType = "ConfigOptimization",
            Recommendations = JsonSerializer.Serialize(report, SerializerOptions),
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ReviewTasks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Config review task created. SessionId={SessionId}, ReviewTaskId={ReviewTaskId}, TaskType={TaskType}",
            sessionId,
            entity.TaskId,
            entity.TaskType);

        return entity.TaskId;
    }
}
