using System.Collections.Concurrent;
using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Api;

internal static class WorkflowApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkflowApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workflows");

        group.MapPost("/sql-analysis", HandleCreateSqlAnalysisAsync);
        group.MapPost("/db-config-optimization", HandleCreateDbConfigOptimizationAsync);
        group.MapGet("/{sessionId:guid}", HandleGetWorkflowAsync);
        group.MapPost("/{sessionId:guid}/cancel", HandleCancelWorkflowAsync);
        group.MapPost("/{sessionId:guid}/resume", HandleResumeWorkflowAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowApplicationService.StartSqlAnalysisAsync(request, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message, null);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleCreateDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowApplicationService.StartDbConfigOptimizationAsync(request, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message, null);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleGetWorkflowAsync(
        Guid sessionId,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await workflowApplicationService.GetAsync(sessionId, cancellationToken);
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

    private static async Task<IResult> HandleCancelWorkflowAsync(
        Guid sessionId,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowApplicationService.CancelAsync(sessionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "INVALID_OPERATION", ex.Message, null);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleResumeWorkflowAsync(
        Guid sessionId,
        IWorkflowApplicationService workflowApplicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflowApplicationService.ResumeAsync(sessionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (InvalidOperationException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, StatusCodes.Status400BadRequest, "INVALID_OPERATION", ex.Message, null);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }
}

internal interface IWorkflowExecutionScheduler
{
    Task<LegacyWorkflowStartResponse> ScheduleSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default);

    Task<LegacyWorkflowStartResponse> ScheduleDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        CancellationToken cancellationToken = default);

    Task<LegacyWorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<LegacyWorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<LegacyWorkflowResumeResponse> ResumeAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

internal interface IWorkflowQueryService
{
    Task<LegacyWorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

internal sealed class WorkflowExecutionScheduler(
    IWorkflowRunner workflowRunner,
    ICheckpointStorage checkpointStorage,
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowEventPublisher workflowEventPublisher,
    IEnumerable<IWorkflowExecutor> workflowExecutors,
    ILogger<WorkflowExecutionScheduler> logger) : IWorkflowExecutionScheduler
{
    private static readonly string[] SqlAnalysisExecutorOrder =
    [
        "SqlParserExecutor",
        "ExecutionPlanExecutor",
        "IndexAdvisorExecutor",
        "CoordinatorExecutor",
        "HumanReviewExecutor",
        "RegenerationExecutor"
    ];

    private static readonly string[] DbConfigOptimizationExecutorOrder =
    [
        "ConfigCollectorExecutor",
        "ConfigAnalyzerExecutor",
        "ConfigCoordinatorExecutor",
        "ConfigReviewExecutor"
    ];

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningSessions = new();
    private readonly IReadOnlyList<IWorkflowExecutor> _workflowExecutors = workflowExecutors.ToArray();

    public async Task<LegacyWorkflowStartResponse> ScheduleSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SqlText))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_SQL_SYNTAX", "SQL text is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DatabaseId))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "DatabaseId is required.");
        }

        var databaseEngine = ResolveDatabaseEngine(request.DatabaseEngine, request.DatabaseId);
        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "SqlAnalysis");
        context.Set(WorkflowContextKeys.SqlText, request.SqlText.Trim());
        context.Set(WorkflowContextKeys.DatabaseId, request.DatabaseId.Trim());
        context.Set(WorkflowContextKeys.DatabaseType, databaseEngine);
        context.Set(WorkflowContextKeys.DatabaseDialect, databaseEngine);
        context.Set("Options", request.Options);
        context.AdvanceCheckpointVersion();

        await checkpointStorage.SaveCheckpointAsync(context.CreateCheckpointSnapshot(), cancellationToken);
        ScheduleBackgroundExecution(
            sessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.RunAsync(context, _workflowExecutors, token));

        return new LegacyWorkflowStartResponse(sessionId, WorkflowCheckpointStatus.Running.ToString(), context.CreatedAt);
    }

    public async Task<LegacyWorkflowStartResponse> ScheduleDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DatabaseId))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "DatabaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DatabaseType))
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "DatabaseType is required.");
        }

        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "DbConfigOptimization");
        context.Set(WorkflowContextKeys.DatabaseId, request.DatabaseId.Trim());
        context.Set(WorkflowContextKeys.DatabaseType, request.DatabaseType.Trim());
        context.AdvanceCheckpointVersion();

        await checkpointStorage.SaveCheckpointAsync(context.CreateCheckpointSnapshot(), cancellationToken);
        ScheduleBackgroundExecution(
            sessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.RunAsync(context, _workflowExecutors, token));

        return new LegacyWorkflowStartResponse(sessionId, WorkflowCheckpointStatus.Running.ToString(), context.CreatedAt);
    }

    public async Task<LegacyWorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId });
        }

        if (checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "WORKFLOW_CANCELLED",
                $"Workflow cannot be cancelled from status {checkpoint.Status}.",
                new { sessionId, status = checkpoint.Status.ToString() });
        }

        var context = WorkflowContext.FromCheckpoint(checkpoint);
        context.Set("LastError", "Workflow cancelled by user.");
        context.ApplyStatus(WorkflowCheckpointStatus.Cancelled);
        var cancelEvent = new WorkflowEventMessage(
            WorkflowEventType.WorkflowCancelled,
            context.SessionId,
            context.WorkflowType,
            DateTimeOffset.UtcNow,
            new { reason = "CancelledByUser" });
        var trackedCancelEvent = WorkflowTimeline.Append(context, cancelEvent);
        context.AdvanceCheckpointVersion();
        var updatedCheckpoint = context.CreateCheckpointSnapshot();

        await PersistCancellationAsync(updatedCheckpoint, cancellationToken);
        await checkpointStorage.SaveCheckpointAsync(updatedCheckpoint, cancellationToken);
        await workflowEventPublisher.PublishAsync(cancelEvent with { Sequence = trackedCancelEvent.Sequence }, cancellationToken);

        if (_runningSessions.TryGetValue(sessionId, out var cancellationSource))
        {
            cancellationSource.Cancel();
        }

        return new LegacyWorkflowCancelResponse(sessionId, WorkflowCheckpointStatus.Cancelled.ToString());
    }

    private async Task PersistCancellationAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var workflowSession = await dbContext.WorkflowSessions
            .SingleOrDefaultAsync(item => item.SessionId == checkpoint.SessionId, cancellationToken);

        if (workflowSession is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "WORKFLOW_NOT_FOUND",
                "Workflow session not found.",
                new { sessionId = checkpoint.SessionId });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        workflowSession.WorkflowType = checkpoint.WorkflowType;
        workflowSession.Status = checkpoint.Status.ToString();
        workflowSession.State = JsonSerializer.Serialize(checkpoint, WorkflowCheckpointJson.SerializerOptions);
        workflowSession.UpdatedAt = checkpoint.UpdatedAt;
        workflowSession.CompletedAt = checkpoint.UpdatedAt;

        var pendingReviews = await dbContext.ReviewTasks
            .Where(item => item.SessionId == checkpoint.SessionId && item.Status == "Pending")
            .ToListAsync(cancellationToken);

        foreach (var reviewTask in pendingReviews)
        {
            reviewTask.Status = "Cancelled";
            reviewTask.ReviewerComment = "Workflow cancelled by user.";
            reviewTask.ReviewedAt = checkpoint.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<LegacyWorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "CHECKPOINT_NOT_FOUND",
                "Workflow checkpoint not found.",
                new { sessionId });
        }

        return await ResumeAsync(checkpoint, cancellationToken);
    }

    public async Task<LegacyWorkflowResumeResponse> ResumeAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_runningSessions.ContainsKey(checkpoint.SessionId))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "WORKFLOW_ALREADY_RUNNING",
                "Workflow is already running.",
                new { sessionId = checkpoint.SessionId });
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.Running)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "WORKFLOW_ALREADY_RUNNING",
                "Workflow is already running.",
                new { sessionId = checkpoint.SessionId });
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.Completed)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "WORKFLOW_ALREADY_COMPLETED",
                "Completed workflow cannot be resumed.",
                new { sessionId = checkpoint.SessionId });
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.WaitingForReview &&
            !checkpoint.Context.ContainsKey(WorkflowContextKeys.RejectionReason))
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "REVIEW_REQUIRED",
                "Workflow is waiting for human review and cannot be resumed directly.",
                new { sessionId = checkpoint.SessionId });
        }

        var normalizedCheckpoint = NormalizeCheckpointForResume(checkpoint);
        await checkpointStorage.SaveCheckpointAsync(normalizedCheckpoint, cancellationToken);

        ScheduleBackgroundExecution(
            checkpoint.SessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.ResumeAsync(normalizedCheckpoint, _workflowExecutors, token));

        return new LegacyWorkflowResumeResponse(
            checkpoint.SessionId,
            WorkflowCheckpointStatus.Running.ToString(),
            ResolveResumePoint(normalizedCheckpoint));
    }

    private void ScheduleBackgroundExecution(
        Guid sessionId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> executeAsync)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_runningSessions.TryAdd(sessionId, linkedSource))
        {
            linkedSource.Dispose();
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "WORKFLOW_ALREADY_RUNNING",
                "Workflow is already running.",
                new { sessionId });
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await executeAsync(linkedSource.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogInformation("Workflow execution cancelled. SessionId={SessionId}", sessionId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Workflow execution crashed. SessionId={SessionId}", sessionId);
                }
                finally
                {
                    _runningSessions.TryRemove(sessionId, out _);
                    linkedSource.Dispose();
                }
            },
            CancellationToken.None);
    }

    private string ResolveResumePoint(WorkflowCheckpoint checkpoint)
    {
        if (!string.IsNullOrWhiteSpace(checkpoint.CurrentExecutor))
        {
            return checkpoint.CurrentExecutor;
        }

        var nextExecutor = SqlAnalysisExecutorOrder.FirstOrDefault(name => !checkpoint.CompletedExecutors.Contains(name));
        return nextExecutor ?? SqlAnalysisExecutorOrder[^1];
    }

    private static WorkflowCheckpoint NormalizeCheckpointForResume(WorkflowCheckpoint checkpoint)
    {
        return checkpoint.Status switch
        {
            WorkflowCheckpointStatus.Cancelled or WorkflowCheckpointStatus.Failed => checkpoint with
            {
                Status = WorkflowCheckpointStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow,
                LastCheckpointAt = DateTimeOffset.UtcNow
            },
            _ => checkpoint
        };
    }

    private static string ResolveDatabaseEngine(string? databaseEngine, string databaseId)
    {
        var candidate = string.IsNullOrWhiteSpace(databaseEngine)
            ? databaseId
            : databaseEngine;

        if (candidate.Contains("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return "mysql";
        }

        if (candidate.Contains("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return "postgresql";
        }

        throw new ApiException(
            StatusCodes.Status400BadRequest,
            "INVALID_REQUEST",
            "Unable to resolve database engine. Provide DatabaseEngine or a DatabaseId that includes mysql/postgres.",
            new { databaseId, databaseEngine });
    }
}

internal sealed class WorkflowQueryService(
    ICheckpointStorage checkpointStorage,
    IWorkflowResultSerializer workflowResultSerializer) : IWorkflowQueryService
{
    private const int SqlAnalysisStepCount = 6;

    public async Task<LegacyWorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint is null)
        {
            return null;
        }

        checkpoint.Context.TryGetValue(WorkflowContextKeys.FinalResult, out var finalResultElement);
        checkpoint.Context.TryGetValue(WorkflowContextKeys.ReviewId, out var reviewIdElement);
        checkpoint.Context.TryGetValue(WorkflowContextKeys.ReviewStatus, out var reviewStatusElement);
        checkpoint.Context.TryGetValue("LastError", out var errorElement);
        checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseId, out var databaseIdElement);
        checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseType, out var databaseTypeElement);

        var result = finalResultElement.ValueKind == default
            ? null
            : workflowResultSerializer.ToEnvelope(
                checkpoint.WorkflowType,
                finalResultElement,
                databaseIdElement.ValueKind == default ? null : databaseIdElement.Deserialize<string>(),
                databaseTypeElement.ValueKind == default ? null : databaseTypeElement.Deserialize<string>());
        var reviewId = reviewIdElement.ValueKind == default
            ? null
            : reviewIdElement.Deserialize<Guid?>();
        var reviewStatus = reviewStatusElement.ValueKind == default
            ? null
            : reviewStatusElement.Deserialize<string>();
        var errorMessage = errorElement.ValueKind == default
            ? null
            : errorElement.Deserialize<string>();

        return new LegacyWorkflowStatusResponse(
            checkpoint.SessionId,
            checkpoint.WorkflowType,
            checkpoint.Status.ToString(),
            ResolveCurrentExecutor(checkpoint),
            CalculateProgress(checkpoint),
            checkpoint.CreatedAt,
            checkpoint.UpdatedAt,
            checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled
                ? checkpoint.UpdatedAt
                : null,
            result,
            reviewId,
            reviewStatus,
            errorMessage);
    }

    private static string? ResolveCurrentExecutor(WorkflowCheckpoint checkpoint)
    {
        if (checkpoint.Status == WorkflowCheckpointStatus.WaitingForReview)
        {
            return "HumanReviewExecutor";
        }

        return string.IsNullOrWhiteSpace(checkpoint.CurrentExecutor)
            ? null
            : checkpoint.CurrentExecutor;
    }

    private static int CalculateProgress(WorkflowCheckpoint checkpoint)
    {
        if (checkpoint.Status == WorkflowCheckpointStatus.Completed)
        {
            return 100;
        }

        var completedSteps = checkpoint.CompletedExecutors.Count;
        var progress = (int)Math.Round((double)completedSteps / SqlAnalysisStepCount * 100, MidpointRounding.AwayFromZero);
        return Math.Clamp(progress, 0, 99);
    }
}

// Legacy DTOs for backward compatibility with old scheduler
internal sealed record LegacyWorkflowStartResponse(Guid SessionId, string Status, DateTimeOffset StartedAt);
internal sealed record LegacyWorkflowCancelResponse(Guid SessionId, string Status);
internal sealed record LegacyWorkflowResumeResponse(Guid SessionId, string Status, string ResumedFrom);
internal sealed record LegacyWorkflowStatusResponse(
    Guid SessionId,
    string WorkflowType,
    string Status,
    string? CurrentExecutor,
    int Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    WorkflowResultEnvelope? Result,
    Guid? ReviewId,
    string? ReviewStatus,
    string? ErrorMessage);
