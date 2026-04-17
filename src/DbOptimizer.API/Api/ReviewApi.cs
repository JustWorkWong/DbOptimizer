using System.Text.Json;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Api;

internal static class ReviewApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapReviewApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reviews");

        group.MapGet(string.Empty, HandleListReviewsAsync);
        group.MapGet("/{taskId:guid}", HandleGetReviewAsync);
        group.MapPost("/{taskId:guid}/submit", HandleSubmitReviewAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleListReviewsAsync(
        string? status,
        int? page,
        int? pageSize,
        IReviewApplicationService reviewApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await reviewApplicationService.ListAsync(
                status,
                page ?? 1,
                pageSize ?? 20,
                cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleGetReviewAsync(
        Guid taskId,
        IReviewApplicationService reviewApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await reviewApplicationService.GetAsync(taskId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleSubmitReviewAsync(
        Guid taskId,
        SubmitReviewRequest request,
        IReviewApplicationService reviewApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await reviewApplicationService.SubmitAsync(taskId, request, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }
}

internal sealed class SubmitReviewRequest
{
    public string Action { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public Dictionary<string, JsonElement>? Adjustments { get; set; }
}

internal sealed record ReviewListResponse(
    IReadOnlyList<ReviewListItemResponse> Items,
    int Page,
    int PageSize,
    int Total,
    bool HasMore);

internal sealed record ReviewListItemResponse(
    Guid TaskId,
    Guid SessionId,
    string WorkflowType,
    string Status,
    OptimizationReport Recommendations,
    DateTimeOffset CreatedAt);

internal sealed record ReviewDetailResponse(
    Guid TaskId,
    Guid SessionId,
    string WorkflowType,
    string Status,
    OptimizationReport Recommendations,
    string? ReviewerComment,
    Dictionary<string, JsonElement>? Adjustments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt);

internal sealed record ReviewSubmitResponse(Guid TaskId, string Status, DateTimeOffset ReviewedAt);

internal interface IReviewApplicationService
{
    Task<ReviewListResponse> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ReviewDetailResponse> GetAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<ReviewSubmitResponse> SubmitAsync(
        Guid taskId,
        SubmitReviewRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class ReviewApplicationService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ICheckpointStorage checkpointStorage,
    IWorkflowExecutionScheduler workflowExecutionScheduler,
    IWorkflowEventPublisher workflowEventPublisher) : IReviewApplicationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReviewListResponse> ListAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Page and pageSize must be greater than zero.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.ReviewTasks
            .AsNoTracking()
            .Include(task => task.Session)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(task => task.Status == status.Trim());
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await query
            .OrderByDescending(task => task.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = entities
            .Select(entity => new ReviewListItemResponse(
                entity.TaskId,
                entity.SessionId,
                entity.Session.WorkflowType,
                entity.Status,
                DeserializeReport(entity.Recommendations),
                entity.CreatedAt))
            .ToArray();

        return new ReviewListResponse(items, page, pageSize, total, page * pageSize < total);
    }

    public async Task<ReviewDetailResponse> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.ReviewTasks
            .AsNoTracking()
            .Include(task => task.Session)
            .SingleOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "REVIEW_TASK_NOT_FOUND",
                "Review task not found.",
                new { taskId });
        }

        return new ReviewDetailResponse(
            entity.TaskId,
            entity.SessionId,
            entity.Session.WorkflowType,
            entity.Status,
            DeserializeReport(entity.Recommendations),
            entity.ReviewerComment,
            DeserializeAdjustments(entity.Adjustments),
            entity.CreatedAt,
            entity.ReviewedAt);
    }

    public async Task<ReviewSubmitResponse> SubmitAsync(
        Guid taskId,
        SubmitReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedAction = NormalizeReviewAction(request.Action);
        if (normalizedAction == "reject" && string.IsNullOrWhiteSpace(request.Comment))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Reject action requires a comment.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reviewTask = await dbContext.ReviewTasks
            .SingleOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

        if (reviewTask is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "REVIEW_TASK_NOT_FOUND",
                "Review task not found.",
                new { taskId });
        }

        if (!string.Equals(reviewTask.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "REVIEW_ALREADY_SUBMITTED",
                "Review task has already been submitted.",
                new { taskId, status = reviewTask.Status });
        }

        var checkpoint = await checkpointStorage.LoadCheckpointAsync(reviewTask.SessionId, cancellationToken);
        if (checkpoint is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "CHECKPOINT_NOT_FOUND",
                "Workflow checkpoint not found.",
                new { sessionId = reviewTask.SessionId });
        }

        if (checkpoint.Status != WorkflowCheckpointStatus.WaitingForReview)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "REVIEW_INVALID_STATE",
                $"Workflow is not waiting for review. Current status: {checkpoint.Status}.",
                new { sessionId = reviewTask.SessionId, status = checkpoint.Status.ToString() });
        }

        var reviewedAt = DateTimeOffset.UtcNow;
        reviewTask.Status = MapReviewTaskStatus(normalizedAction);
        reviewTask.ReviewerComment = request.Comment?.Trim();
        reviewTask.ReviewedAt = reviewedAt;
        reviewTask.Adjustments = request.Adjustments is { Count: > 0 }
            ? JsonSerializer.Serialize(request.Adjustments, SerializerOptions)
            : null;

        switch (normalizedAction)
        {
            case "approve":
            {
                var updatedCheckpoint = BuildCompletedCheckpoint(
                    checkpoint,
                    reviewStatus: "Approved",
                    comment: request.Comment,
                    adjustments: request.Adjustments);
                await PersistReviewAndCheckpointAsync(dbContext, reviewTask, updatedCheckpoint, cancellationToken);
                await checkpointStorage.SaveCheckpointAsync(updatedCheckpoint, cancellationToken);
                await checkpointStorage.DeleteCheckpointAsync(checkpoint.SessionId, cancellationToken);
                await PublishCompletedWorkflowEventAsync(updatedCheckpoint, workflowEventPublisher, cancellationToken);
                break;
            }
            case "adjust":
            {
                var updatedCheckpoint = BuildCompletedCheckpoint(
                    checkpoint,
                    reviewStatus: "Adjusted",
                    comment: request.Comment,
                    adjustments: request.Adjustments);
                await PersistReviewAndCheckpointAsync(dbContext, reviewTask, updatedCheckpoint, cancellationToken);
                await checkpointStorage.SaveCheckpointAsync(updatedCheckpoint, cancellationToken);
                await checkpointStorage.DeleteCheckpointAsync(checkpoint.SessionId, cancellationToken);
                await PublishCompletedWorkflowEventAsync(updatedCheckpoint, workflowEventPublisher, cancellationToken);
                break;
            }
            case "reject":
            {
                var updatedCheckpoint = BuildRejectedCheckpoint(checkpoint, request.Comment!, request.Adjustments);
                await PersistReviewAndCheckpointAsync(dbContext, reviewTask, updatedCheckpoint, cancellationToken);
                await checkpointStorage.SaveCheckpointAsync(updatedCheckpoint, cancellationToken);
                await workflowExecutionScheduler.ResumeAsync(updatedCheckpoint, cancellationToken);
                break;
            }
        }

        return new ReviewSubmitResponse(taskId, reviewTask.Status, reviewedAt);
    }

    private static WorkflowCheckpoint BuildCompletedCheckpoint(
        WorkflowCheckpoint checkpoint,
        string reviewStatus,
        string? comment,
        Dictionary<string, JsonElement>? adjustments)
    {
        var context = WorkflowContext.FromCheckpoint(checkpoint);
        context.Set(WorkflowContextKeys.ReviewStatus, reviewStatus);

        if (!string.IsNullOrWhiteSpace(comment))
        {
            context.Set("ReviewComment", comment.Trim());
        }

        if (adjustments is { Count: > 0 })
        {
            context.Set("ReviewAdjustments", adjustments);
        }

        if (reviewStatus == "Adjusted" &&
            context.TryGet<OptimizationReport>(WorkflowContextKeys.FinalResult, out var report) &&
            report is not null)
        {
            context.Set(WorkflowContextKeys.FinalResult, ApplyAdjustments(report, comment, adjustments));
        }

        context.ApplyStatus(WorkflowCheckpointStatus.Completed);
        var workflowCompletedEvent = new WorkflowEventMessage(
            WorkflowEventType.WorkflowCompleted,
            checkpoint.SessionId,
            checkpoint.WorkflowType,
            DateTimeOffset.UtcNow,
            new
            {
                reviewStatus,
                reviewComment = comment,
                completedByReview = true
            });
        var trackedEvent = WorkflowTimeline.Append(context, workflowCompletedEvent);
        context.AdvanceCheckpointVersion();
        return context.CreateCheckpointSnapshot();
    }

    private static WorkflowCheckpoint BuildRejectedCheckpoint(
        WorkflowCheckpoint checkpoint,
        string comment,
        Dictionary<string, JsonElement>? adjustments)
    {
        var context = WorkflowContext.FromCheckpoint(checkpoint);
        context.Set(WorkflowContextKeys.ReviewStatus, "Rejected");
        context.Set(WorkflowContextKeys.RejectionReason, comment.Trim());

        if (adjustments is { Count: > 0 })
        {
            context.Set("ReviewAdjustments", adjustments);
        }

        context.AdvanceCheckpointVersion();
        return context.CreateCheckpointSnapshot();
    }

    private async Task PersistReviewAndCheckpointAsync(
        DbOptimizerDbContext dbContext,
        ReviewTaskEntity reviewTask,
        WorkflowCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var workflowSession = await dbContext.WorkflowSessions
            .SingleOrDefaultAsync(item => item.SessionId == reviewTask.SessionId, cancellationToken);

        if (workflowSession is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId = reviewTask.SessionId });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        workflowSession.WorkflowType = checkpoint.WorkflowType;
        workflowSession.Status = checkpoint.Status.ToString();
        workflowSession.State = JsonSerializer.Serialize(checkpoint, WorkflowCheckpointJson.SerializerOptions);
        workflowSession.UpdatedAt = checkpoint.UpdatedAt;
        workflowSession.CompletedAt = checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled
            ? checkpoint.UpdatedAt
            : null;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task PublishCompletedWorkflowEventAsync(
        WorkflowCheckpoint checkpoint,
        IWorkflowEventPublisher workflowEventPublisher,
        CancellationToken cancellationToken)
    {
        var completedEvent = WorkflowTimeline.GetEvents(checkpoint)
            .LastOrDefault(item => item.EventType == WorkflowEventType.WorkflowCompleted);

        if (completedEvent is null)
        {
            return;
        }

        await workflowEventPublisher.PublishAsync(
            new WorkflowEventMessage(
                completedEvent.EventType,
                completedEvent.SessionId,
                completedEvent.WorkflowType,
                completedEvent.Timestamp,
                completedEvent.Payload,
                completedEvent.Sequence),
            cancellationToken);
    }

    private static OptimizationReport ApplyAdjustments(
        OptimizationReport report,
        string? comment,
        Dictionary<string, JsonElement>? adjustments)
    {
        var cloned = JsonSerializer.Deserialize<OptimizationReport>(
                         JsonSerializer.Serialize(report, SerializerOptions),
                         SerializerOptions)
                     ?? new OptimizationReport();

        if (!string.IsNullOrWhiteSpace(comment))
        {
            cloned.Warnings.Add($"人工调整说明：{comment.Trim()}");
        }

        if (adjustments is { Count: > 0 })
        {
            cloned.Metadata["reviewAdjustments"] = JsonSerializer.Serialize(adjustments, SerializerOptions);
        }

        cloned.Metadata["reviewAction"] = "Adjusted";
        return cloned;
    }

    private static string NormalizeReviewAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Review action is required.");
        }

        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is "approve" or "reject" or "adjust")
        {
            return normalized;
        }

        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "INVALID_REQUEST",
            "Review action must be approve, reject, or adjust.",
            new { action });
    }

    private static string MapReviewTaskStatus(string action)
    {
        return action switch
        {
            "approve" => "Approved",
            "reject" => "Rejected",
            "adjust" => "Adjusted",
            _ => "Pending"
        };
    }

    private static OptimizationReport DeserializeReport(string json)
    {
        return JsonSerializer.Deserialize<OptimizationReport>(json, SerializerOptions) ?? new OptimizationReport();
    }

    private static Dictionary<string, JsonElement>? DeserializeAdjustments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
    }
}
