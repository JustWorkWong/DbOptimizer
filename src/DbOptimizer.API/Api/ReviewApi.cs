using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Review;
using DbOptimizer.Infrastructure.Maf.Runtime;
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
    WorkflowResultEnvelope Recommendations,
    DateTimeOffset CreatedAt);

internal sealed record ReviewDetailResponse(
    Guid TaskId,
    Guid SessionId,
    string WorkflowType,
    string Status,
    WorkflowResultEnvelope Recommendations,
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
    IWorkflowResultSerializer workflowResultSerializer,
    IWorkflowReviewTaskGateway reviewTaskGateway,
    IWorkflowReviewResponseFactory reviewResponseFactory,
    IMafWorkflowRuntime mafWorkflowRuntime) : IReviewApplicationService
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
                DeserializeReport(entity.Session.WorkflowType, entity.Recommendations),
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
            DeserializeReport(entity.Session.WorkflowType, entity.Recommendations),
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
            .Include(x => x.Session)
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

        // 只支持 MAF workflow
        if (!string.Equals(reviewTask.Session.EngineType, "maf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                "LEGACY_WORKFLOW_NOT_SUPPORTED",
                "Legacy workflow review is no longer supported. Please use MAF workflow.",
                new { sessionId = reviewTask.SessionId, engineType = reviewTask.Session.EngineType });
        }

        // MAF workflow 路径：通过 gateway 读取 correlation 并恢复
        return await HandleMafWorkflowReviewAsync(taskId, reviewTask, request, normalizedAction, cancellationToken);
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

    private WorkflowResultEnvelope DeserializeReport(string workflowType, string json)
    {
        return workflowResultSerializer.ToEnvelope(workflowType, json);
    }

    private static Dictionary<string, JsonElement>? DeserializeAdjustments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
    }

    private async Task<ReviewSubmitResponse> HandleMafWorkflowReviewAsync(
        Guid taskId,
        ReviewTaskEntity reviewTask,
        SubmitReviewRequest request,
        string normalizedAction,
        CancellationToken cancellationToken)
    {
        var correlation = await reviewTaskGateway.GetCorrelationAsync(taskId, cancellationToken);

        if (correlation is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "CORRELATION_NOT_FOUND",
                "Review task correlation data not found.",
                new { taskId });
        }

        var reviewedAt = DateTimeOffset.UtcNow;
        var adjustments = request.Adjustments ?? new Dictionary<string, JsonElement>();

        // 更新 review_tasks 状态
        await reviewTaskGateway.UpdateStatusAsync(
            taskId,
            MapReviewTaskStatus(normalizedAction),
            request.Comment?.Trim(),
            adjustments.Count > 0 ? JsonSerializer.Serialize(adjustments, SerializerOptions) : null,
            reviewedAt,
            cancellationToken);

        // 构建 ReviewDecisionResponseMessage
        var responseMessage = reviewResponseFactory.CreateSqlResponse(
            correlation.SessionId,
            taskId,
            correlation.RequestId,
            correlation.EngineRunId,
            correlation.CheckpointRef,
            normalizedAction,
            request.Comment,
            adjustments);

        // 通过 MAF runtime 恢复 workflow（暂时使用 sessionId，后续需要扩展 ResumeAsync 支持 response message）
        await mafWorkflowRuntime.ResumeAsync(
            correlation.SessionId,
            cancellationToken);

        return new ReviewSubmitResponse(taskId, MapReviewTaskStatus(normalizedAction), reviewedAt);
    }
}
