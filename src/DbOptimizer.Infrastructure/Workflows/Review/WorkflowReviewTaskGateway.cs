using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Workflows.Review;

public sealed class WorkflowReviewTaskGateway(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<WorkflowReviewTaskGateway> logger) : IWorkflowReviewTaskGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> CreateAsync(
        Guid sessionId,
        string taskType,
        string requestId,
        string engineRunId,
        string checkpointRef,
        WorkflowResultEnvelope payload,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new ReviewTaskEntity
        {
            TaskId = Guid.NewGuid(),
            SessionId = sessionId,
            TaskType = taskType,
            RequestId = requestId,
            EngineRunId = engineRunId,
            EngineCheckpointRef = checkpointRef,
            Recommendations = JsonSerializer.Serialize(payload, SerializerOptions),
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ReviewTasks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Review task created. TaskId={TaskId}, SessionId={SessionId}, RequestId={RequestId}, RunId={RunId}",
            entity.TaskId,
            sessionId,
            requestId,
            engineRunId);

        return entity.TaskId;
    }

    public async Task<ReviewTaskCorrelation?> GetCorrelationAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ReviewTasks
            .AsNoTracking()
            .Include(x => x.Session)
            .Where(x => x.TaskId == taskId)
            .Select(x => new
            {
                x.SessionId,
                x.Session.WorkflowType,
                x.RequestId,
                x.EngineRunId,
                x.EngineCheckpointRef,
                x.Recommendations
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<WorkflowResultEnvelope>(
            entity.Recommendations,
            SerializerOptions);

        if (payload is null)
        {
            logger.LogWarning("Failed to deserialize payload for TaskId={TaskId}", taskId);
            return null;
        }

        return new ReviewTaskCorrelation(
            entity.SessionId,
            entity.WorkflowType,
            entity.RequestId,
            entity.EngineRunId ?? string.Empty,
            entity.EngineCheckpointRef ?? string.Empty,
            payload);
    }

    public async Task UpdateStatusAsync(
        Guid taskId,
        string status,
        string? comment,
        string? adjustmentsJson,
        DateTimeOffset reviewedAt,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ReviewTasks
            .Where(x => x.TaskId == taskId)
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            logger.LogWarning("Review task not found. TaskId={TaskId}", taskId);
            return;
        }

        entity.Status = status;
        entity.ReviewerComment = comment;
        entity.Adjustments = adjustmentsJson;
        entity.ReviewedAt = reviewedAt;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Review task updated. TaskId={TaskId}, Status={Status}",
            taskId,
            status);
    }

    public async Task UpdateCorrelationAsync(
        Guid taskId,
        string requestId,
        string engineRunId,
        string checkpointRef,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await dbContext.ReviewTasks
            .Where(x => x.TaskId == taskId)
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            logger.LogWarning("Review task not found when updating correlation. TaskId={TaskId}", taskId);
            return;
        }

        entity.RequestId = requestId;
        entity.EngineRunId = engineRunId;
        entity.EngineCheckpointRef = checkpointRef;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Review task correlation updated. TaskId={TaskId}, RequestId={RequestId}, RunId={RunId}, CheckpointRef={CheckpointRef}",
            taskId,
            requestId,
            engineRunId,
            checkpointRef);
    }
}
