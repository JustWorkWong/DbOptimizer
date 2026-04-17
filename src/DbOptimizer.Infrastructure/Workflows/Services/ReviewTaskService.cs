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
    IWorkflowResultSerializer workflowResultSerializer,
    ILogger<ReviewTaskService> logger) : IReviewTaskService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(
        Guid sessionId,
        OptimizationReport report,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var (databaseId, databaseType) = await LoadDatabaseMetadataAsync(dbContext, sessionId, cancellationToken);

        var entity = new ReviewTaskEntity
        {
            TaskId = Guid.NewGuid(),
            SessionId = sessionId,
            TaskType = "SqlOptimization",
            Recommendations = JsonSerializer.Serialize(
                workflowResultSerializer.ToEnvelope(report, databaseId, databaseType),
                SerializerOptions),
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

    private static async Task<(string DatabaseId, string DatabaseType)> LoadDatabaseMetadataAsync(
        DbOptimizerDbContext dbContext,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.WorkflowSessions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .Select(item => item.State)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(state))
        {
            return (string.Empty, string.Empty);
        }

        using var document = JsonDocument.Parse(state);
        if (!document.RootElement.TryGetProperty("context", out var contextElement) ||
            contextElement.ValueKind != JsonValueKind.Object)
        {
            return (string.Empty, string.Empty);
        }

        return (
            ReadContextString(contextElement, WorkflowContextKeys.DatabaseId),
            ReadContextString(contextElement, WorkflowContextKeys.DatabaseType));
    }

    private static string ReadContextString(JsonElement contextElement, string propertyName)
    {
        if (contextElement.TryGetProperty(propertyName, out var propertyElement) &&
            propertyElement.ValueKind == JsonValueKind.String)
        {
            return propertyElement.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

/* =========================
 * 配置优化审阅任务服务
 * ========================= */
public sealed class ConfigReviewTaskService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowResultSerializer workflowResultSerializer,
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
            TaskId = Guid.NewGuid(),
            SessionId = sessionId,
            TaskType = "ConfigOptimization",
            Recommendations = JsonSerializer.Serialize(
                workflowResultSerializer.ToEnvelope(report),
                SerializerOptions),
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
