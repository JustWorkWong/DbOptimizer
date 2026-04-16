using System.Text.Json;
using DbOptimizer.API.Checkpointing;
using DbOptimizer.API.Persistence;
using DbOptimizer.API.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Api;

internal static class DashboardAndHistoryApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDashboardAndHistoryApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/stats", HandleGetDashboardStatsAsync);

        var historyGroup = endpoints.MapGroup("/api/history");
        historyGroup.MapGet(string.Empty, HandleGetHistoryListAsync);
        historyGroup.MapGet("/{sessionId:guid}", HandleGetHistoryDetailAsync);
        historyGroup.MapGet("/{sessionId:guid}/replay", HandleGetHistoryReplayAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetDashboardStatsAsync(
        IHistoryQueryService historyQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await historyQueryService.GetDashboardStatsAsync(cancellationToken);
        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleGetHistoryListAsync(
        string? workflowType,
        string? status,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        int? page,
        int? pageSize,
        IHistoryQueryService historyQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await historyQueryService.GetHistoryListAsync(
                workflowType,
                status,
                startDate,
                endDate,
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

    private static async Task<IResult> HandleGetHistoryDetailAsync(
        Guid sessionId,
        IHistoryQueryService historyQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await historyQueryService.GetHistoryDetailAsync(sessionId, cancellationToken);
        if (response is null)
        {
            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId });
        }

        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleGetHistoryReplayAsync(
        Guid sessionId,
        IHistoryQueryService historyQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await historyQueryService.GetHistoryReplayAsync(sessionId, cancellationToken);
        if (response is null)
        {
            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId });
        }

        return ApiEnvelopeFactory.Success(httpContext, response);
    }
}

internal sealed record DashboardStatsResponse(
    int TotalTasks,
    int RunningTasks,
    int PendingReview,
    int CompletedTasks,
    IReadOnlyList<DashboardRecentTaskItem> RecentTasks,
    PerformanceTrendResponse PerformanceTrend);

internal sealed record DashboardRecentTaskItem(
    Guid SessionId,
    string WorkflowType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record PerformanceTrendResponse(
    IReadOnlyList<string> Dates,
    IReadOnlyList<int> TaskCounts,
    IReadOnlyList<double> SuccessRates,
    IReadOnlyList<double> AvgDurations);

internal sealed record HistoryListResponse(
    IReadOnlyList<HistoryListItemResponse> Items,
    int Page,
    int PageSize,
    int Total,
    bool HasMore);

internal sealed record HistoryListItemResponse(
    Guid SessionId,
    string WorkflowType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int Duration,
    int RecommendationCount);

internal sealed record HistoryDetailResponse(
    Guid SessionId,
    string WorkflowType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int Duration,
    IReadOnlyList<HistoryExecutorResponse> Executors,
    OptimizationReport? Result,
    TokenUsageResponse? TokenUsage);

internal sealed record HistoryExecutorResponse(
    string ExecutorName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int Duration);

internal sealed record TokenUsageResponse(int Prompt, int Completion, int Total, decimal Cost);

internal sealed record HistoryReplayResponse(Guid SessionId, IReadOnlyList<HistoryReplayEventResponse> Events);

internal sealed record HistoryReplayEventResponse(long Sequence, DateTimeOffset Timestamp, string EventType, JsonElement Payload);

internal interface IHistoryQueryService
{
    Task<DashboardStatsResponse> GetDashboardStatsAsync(CancellationToken cancellationToken = default);

    Task<HistoryListResponse> GetHistoryListAsync(
        string? workflowType,
        string? status,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<HistoryDetailResponse?> GetHistoryDetailAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<HistoryReplayResponse?> GetHistoryReplayAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

internal sealed class HistoryQueryService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowEventQueryService workflowEventQueryService) : IHistoryQueryService
{
    public async Task<DashboardStatsResponse> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await dbContext.WorkflowSessions
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var recentTasks = sessions
            .Take(10)
            .Select(item => new DashboardRecentTaskItem(
                item.SessionId,
                item.WorkflowType,
                item.Status,
                item.CreatedAt,
                item.CompletedAt))
            .ToArray();

        var trend = sessions
            .GroupBy(item => item.CreatedAt.UtcDateTime.Date)
            .OrderBy(group => group.Key)
            .TakeLast(7)
            .ToArray();

        return new DashboardStatsResponse(
            sessions.Count,
            sessions.Count(item => string.Equals(item.Status, WorkflowCheckpointStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase)),
            sessions.Count(item => string.Equals(item.Status, WorkflowCheckpointStatus.WaitingForReview.ToString(), StringComparison.OrdinalIgnoreCase)),
            sessions.Count(item => string.Equals(item.Status, WorkflowCheckpointStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase)),
            recentTasks,
            new PerformanceTrendResponse(
                trend.Select(group => group.Key.ToString("yyyy-MM-dd")).ToArray(),
                trend.Select(group => group.Count()).ToArray(),
                trend.Select(group =>
                        group.Count(item => string.Equals(item.Status, WorkflowCheckpointStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase)) /
                        (double)group.Count())
                    .ToArray(),
                trend.Select(group => group
                        .Where(item => item.CompletedAt.HasValue)
                        .Select(item => (item.CompletedAt!.Value - item.CreatedAt).TotalSeconds)
                        .DefaultIfEmpty(0)
                        .Average())
                    .ToArray()));
    }

    public async Task<HistoryListResponse> GetHistoryListAsync(
        string? workflowType,
        string? status,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0 || pageSize <= 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Page and pageSize must be greater than zero.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.WorkflowSessions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(workflowType))
        {
            query = query.Where(item => item.WorkflowType == workflowType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status.Trim());
        }

        if (startDate.HasValue)
        {
            query = query.Where(item => item.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(item => item.CreatedAt <= endDate.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var sessions = await query
            .OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = sessions
            .Select(item =>
            {
                var checkpoint = WorkflowCheckpointJson.Deserialize(item.State);
                var report = TryGetFinalResult(checkpoint);
                var recommendationCount = report?.IndexRecommendations.Count ?? 0;
                var duration = item.CompletedAt.HasValue
                    ? (int)Math.Round((item.CompletedAt.Value - item.CreatedAt).TotalSeconds, MidpointRounding.AwayFromZero)
                    : 0;

                return new HistoryListItemResponse(
                    item.SessionId,
                    item.WorkflowType,
                    item.Status,
                    item.CreatedAt,
                    item.CompletedAt,
                    duration,
                    recommendationCount);
            })
            .ToArray();

        return new HistoryListResponse(items, page, pageSize, total, page * pageSize < total);
    }

    public async Task<HistoryDetailResponse?> GetHistoryDetailAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var checkpoint = WorkflowCheckpointJson.Deserialize(session.State);
        var result = TryGetFinalResult(checkpoint);
        var events = MergeEvents(checkpoint, workflowEventQueryService.GetEvents(sessionId, 0, 2048));
        var executors = BuildExecutors(events);
        var duration = session.CompletedAt.HasValue
            ? (int)Math.Round((session.CompletedAt.Value - session.CreatedAt).TotalSeconds, MidpointRounding.AwayFromZero)
            : 0;

        return new HistoryDetailResponse(
            session.SessionId,
            session.WorkflowType,
            session.Status,
            session.CreatedAt,
            session.CompletedAt,
            duration,
            executors,
            result,
            null);
    }

    public async Task<HistoryReplayResponse?> GetHistoryReplayAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await dbContext.WorkflowSessions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .Select(item => item.State)
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return null;
        }

        var checkpoint = WorkflowCheckpointJson.Deserialize(state);
        var events = MergeEvents(checkpoint, workflowEventQueryService.GetEvents(sessionId, 0, 2048))
            .Select(item => new HistoryReplayEventResponse(
                item.Sequence,
                item.Timestamp,
                item.EventType.ToString(),
                item.Payload))
            .ToArray();

        return new HistoryReplayResponse(sessionId, events);
    }

    private static OptimizationReport? TryGetFinalResult(WorkflowCheckpoint? checkpoint)
    {
        if (checkpoint is null ||
            !checkpoint.Context.TryGetValue(WorkflowContextKeys.FinalResult, out var resultElement))
        {
            return null;
        }

        return resultElement.Deserialize<OptimizationReport>(WorkflowCheckpointJson.SerializerOptions);
    }

    private static IReadOnlyList<HistoryExecutorResponse> BuildExecutors(IReadOnlyList<WorkflowEventRecord> events)
    {
        var executions = new List<MutableExecutorTimeline>();

        foreach (var item in events)
        {
            if (!TryGetExecutorName(item, out var executorName))
            {
                continue;
            }

            switch (item.EventType)
            {
                case WorkflowEventType.ExecutorStarted:
                {
                    var timeline = new MutableExecutorTimeline(executorName)
                    {
                        StartedAt = item.Timestamp,
                        Status = "Running"
                    };
                    executions.Add(timeline);
                    break;
                }
                case WorkflowEventType.ExecutorCompleted:
                {
                    var timeline = FindOpenExecution(executions, executorName);
                    if (timeline is null)
                    {
                        break;
                    }

                    timeline.CompletedAt = item.Timestamp;
                    timeline.Status = "Completed";
                    break;
                }
                case WorkflowEventType.ExecutorFailed:
                {
                    var timeline = FindOpenExecution(executions, executorName);
                    if (timeline is null)
                    {
                        break;
                    }

                    timeline.CompletedAt = item.Timestamp;
                    timeline.Status = "Failed";
                    break;
                }
            }
        }

        return executions
            .OrderBy(item => item.StartedAt ?? DateTimeOffset.MinValue)
            .Select(item => new HistoryExecutorResponse(
                item.ExecutorName,
                item.Status,
                item.StartedAt ?? DateTimeOffset.MinValue,
                item.CompletedAt,
                item.StartedAt.HasValue && item.CompletedAt.HasValue
                    ? (int)Math.Round((item.CompletedAt.Value - item.StartedAt.Value).TotalSeconds, MidpointRounding.AwayFromZero)
                    : 0))
            .ToArray();
    }

    private static MutableExecutorTimeline? FindOpenExecution(
        IReadOnlyList<MutableExecutorTimeline> executions,
        string executorName)
    {
        return executions.LastOrDefault(item =>
            string.Equals(item.ExecutorName, executorName, StringComparison.Ordinal) &&
            item.CompletedAt is null);
    }

    private static IReadOnlyList<WorkflowEventRecord> MergeEvents(
        WorkflowCheckpoint? checkpoint,
        IReadOnlyList<WorkflowEventRecord> liveEvents)
    {
        var persistedEvents = WorkflowTimeline.GetEvents(checkpoint);
        return persistedEvents
            .Concat(liveEvents)
            .GroupBy(item => item.Sequence)
            .Select(group => group.OrderByDescending(item => item.Timestamp).First())
            .OrderBy(item => item.Sequence)
            .ToArray();
    }

    private static bool TryGetExecutorName(WorkflowEventRecord record, out string executorName)
    {
        executorName = string.Empty;

        if (!record.Payload.TryGetProperty("executorName", out var executorNameElement))
        {
            return false;
        }

        executorName = executorNameElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(executorName);
    }

    private sealed class MutableExecutorTimeline(string executorName)
    {
        public string ExecutorName { get; } = executorName;

        public string Status { get; set; } = "Pending";

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }
    }
}
