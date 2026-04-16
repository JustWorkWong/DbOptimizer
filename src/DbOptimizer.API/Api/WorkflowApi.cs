using System.Collections.Concurrent;
using System.Text.Json;
using DbOptimizer.API.Checkpointing;
using DbOptimizer.API.Workflows;

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
        IWorkflowExecutionScheduler scheduler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduler.ScheduleSqlAnalysisAsync(request, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static IResult HandleCreateDbConfigOptimizationAsync(HttpContext httpContext)
    {
        return ApiEnvelopeFactory.Failure(
            httpContext,
            StatusCodes.Status501NotImplemented,
            "WORKFLOW_NOT_IMPLEMENTED",
            "Database configuration optimization workflow has not been implemented yet.");
    }

    private static async Task<IResult> HandleGetWorkflowAsync(
        Guid sessionId,
        IWorkflowQueryService workflowQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await workflowQueryService.GetAsync(sessionId, cancellationToken);
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
        IWorkflowExecutionScheduler scheduler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduler.CancelAsync(sessionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }

    private static async Task<IResult> HandleResumeWorkflowAsync(
        Guid sessionId,
        IWorkflowExecutionScheduler scheduler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await scheduler.ResumeAsync(sessionId, cancellationToken);
            return ApiEnvelopeFactory.Success(httpContext, response);
        }
        catch (ApiException ex)
        {
            return ApiEnvelopeFactory.Failure(httpContext, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
    }
}

internal sealed class CreateSqlAnalysisWorkflowRequest
{
    public string SqlText { get; set; } = string.Empty;

    public string DatabaseId { get; set; } = string.Empty;

    public string? DatabaseEngine { get; set; }

    public SqlAnalysisWorkflowOptions Options { get; set; } = new();
}

internal sealed class SqlAnalysisWorkflowOptions
{
    public bool EnableIndexRecommendation { get; set; } = true;

    public bool EnableSqlRewrite { get; set; } = true;
}

internal sealed record WorkflowStartResponse(Guid SessionId, string Status, DateTimeOffset StartedAt);

internal sealed record WorkflowCancelResponse(Guid SessionId, string Status);

internal sealed record WorkflowResumeResponse(Guid SessionId, string Status, string ResumedFrom);

internal sealed record WorkflowStatusResponse(
    Guid SessionId,
    string WorkflowType,
    string Status,
    string? CurrentExecutor,
    int Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    OptimizationReport? Result,
    Guid? ReviewId,
    string? ReviewStatus,
    string? ErrorMessage);

internal interface IWorkflowExecutionScheduler
{
    Task<WorkflowStartResponse> ScheduleSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<WorkflowResumeResponse> ResumeAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

internal interface IWorkflowQueryService
{
    Task<WorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

internal sealed class WorkflowExecutionScheduler(
    IWorkflowRunner workflowRunner,
    ICheckpointStorage checkpointStorage,
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

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningSessions = new();
    private readonly IReadOnlyList<IWorkflowExecutor> _workflowExecutors = workflowExecutors.ToArray();

    public async Task<WorkflowStartResponse> ScheduleSqlAnalysisAsync(
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

        return new WorkflowStartResponse(sessionId, WorkflowCheckpointStatus.Running.ToString(), context.CreatedAt);
    }

    public async Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
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
        context.AdvanceCheckpointVersion();
        await checkpointStorage.SaveCheckpointAsync(context.CreateCheckpointSnapshot(), cancellationToken);

        if (_runningSessions.TryGetValue(sessionId, out var cancellationSource))
        {
            cancellationSource.Cancel();
        }

        return new WorkflowCancelResponse(sessionId, WorkflowCheckpointStatus.Cancelled.ToString());
    }

    public async Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
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

    public async Task<WorkflowResumeResponse> ResumeAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
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

        return new WorkflowResumeResponse(
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

internal sealed class WorkflowQueryService(ICheckpointStorage checkpointStorage) : IWorkflowQueryService
{
    private const int SqlAnalysisStepCount = 6;

    public async Task<WorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
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

        var result = finalResultElement.ValueKind == default
            ? null
            : finalResultElement.Deserialize<OptimizationReport>();
        var reviewId = reviewIdElement.ValueKind == default
            ? null
            : reviewIdElement.Deserialize<Guid?>();
        var reviewStatus = reviewStatusElement.ValueKind == default
            ? null
            : reviewStatusElement.Deserialize<string>();
        var errorMessage = errorElement.ValueKind == default
            ? null
            : errorElement.Deserialize<string>();

        return new WorkflowStatusResponse(
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
