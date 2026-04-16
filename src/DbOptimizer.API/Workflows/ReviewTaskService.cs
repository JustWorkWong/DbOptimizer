using System.Text.Json;
using DbOptimizer.API.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Workflows;

internal interface IReviewTaskService
{
    Task<Guid> CreateAsync(
        Guid sessionId,
        OptimizationReport report,
        CancellationToken cancellationToken = default);
}

/* =========================
 * 审阅任务服务
 * 当前先负责把待审内容持久化到 review_tasks，后续 Review API 直接消费这张表。
 * ========================= */
internal sealed class ReviewTaskService(
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
            Recommendations = JsonSerializer.Serialize(report, SerializerOptions),
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ReviewTasks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Review task created. SessionId={SessionId}, ReviewTaskId={ReviewTaskId}",
            sessionId,
            entity.TaskId);

        return entity.TaskId;
    }
}
