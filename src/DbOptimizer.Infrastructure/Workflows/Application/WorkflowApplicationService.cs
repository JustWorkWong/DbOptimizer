using System.Collections.Concurrent;
using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Workflows.Application;

/// <summary>
/// Workflow 应用服务实现
/// </summary>
public sealed class WorkflowApplicationService(
    IWorkflowRunner workflowRunner,
    ICheckpointStorage checkpointStorage,
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowEventPublisher workflowEventPublisher,
    IWorkflowResultSerializer workflowResultSerializer,
    IEnumerable<IWorkflowExecutor> workflowExecutors,
    ILogger<WorkflowApplicationService> logger) : IWorkflowApplicationService
{
    private const int SqlAnalysisStepCount = 6;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningSessions = new();
    private readonly IReadOnlyList<IWorkflowExecutor> _workflowExecutors = workflowExecutors.ToArray();

    public async Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = WorkflowRequestValidator.ValidateCreateSqlAnalysisRequest(request);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Validation failed: {string.Join("; ", validationResult.Errors)}");
        }

        var databaseEngine = ResolveDatabaseEngine(request.DatabaseEngine, request.DatabaseId);
        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "SqlAnalysis");
        context.Set(WorkflowContextKeys.SqlText, request.SqlText.Trim());
        context.Set(WorkflowContextKeys.DatabaseId, request.DatabaseId.Trim());
        context.Set(WorkflowContextKeys.DatabaseType, databaseEngine);
        context.Set(WorkflowContextKeys.DatabaseDialect, databaseEngine);
        context.Set("Options", request.Options);
        context.Set("SourceType", request.SourceType);
        if (request.SourceRefId.HasValue)
        {
            context.Set("SourceRefId", request.SourceRefId.Value);
        }
        context.AdvanceCheckpointVersion();

        await checkpointStorage.SaveCheckpointAsync(context.CreateCheckpointSnapshot(), cancellationToken);

        // 写入 workflow_sessions 表，包含 source_type 和 source_ref_id
        await CreateWorkflowSessionEntityAsync(
            sessionId,
            "SqlAnalysis",
            request.SourceType,
            request.SourceRefId,
            cancellationToken);

        ScheduleBackgroundExecution(
            sessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.RunAsync(context, _workflowExecutors, token));

        return new WorkflowStartResponse(
            sessionId,
            "SqlAnalysis",
            "legacy",
            "Running",
            context.CreatedAt);
    }

    public async Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = WorkflowRequestValidator.ValidateCreateDbConfigOptimizationRequest(request);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Validation failed: {string.Join("; ", validationResult.Errors)}");
        }

        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "DbConfigOptimization");
        context.Set(WorkflowContextKeys.DatabaseId, request.DatabaseId.Trim());
        context.Set(WorkflowContextKeys.DatabaseType, request.DatabaseType.Trim());
        context.Set("Options", request.Options);
        context.AdvanceCheckpointVersion();

        await checkpointStorage.SaveCheckpointAsync(context.CreateCheckpointSnapshot(), cancellationToken);
        ScheduleBackgroundExecution(
            sessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.RunAsync(context, _workflowExecutors, token));

        return new WorkflowStartResponse(
            sessionId,
            "DbConfigOptimization",
            "legacy",
            "Running",
            context.CreatedAt);
    }

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

        WorkflowReviewSummaryDto? review = reviewId.HasValue
            ? new WorkflowReviewSummaryDto(reviewId.Value, reviewStatus ?? "Unknown")
            : null;

        WorkflowErrorDto? error = !string.IsNullOrWhiteSpace(errorMessage)
            ? new WorkflowErrorDto("WORKFLOW_ERROR", errorMessage, null)
            : null;

        return new WorkflowStatusResponse(
            checkpoint.SessionId,
            checkpoint.WorkflowType,
            "legacy",
            checkpoint.Status.ToString(),
            ResolveCurrentExecutor(checkpoint),
            CalculateProgress(checkpoint),
            checkpoint.CreatedAt,
            checkpoint.UpdatedAt,
            checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled
                ? checkpoint.UpdatedAt
                : null,
            new WorkflowSourceDto("manual", null),
            review,
            result,
            error);
    }

    public async Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint is null)
        {
            throw new InvalidOperationException($"Workflow checkpoint not found. SessionId={sessionId}");
        }

        if (_runningSessions.ContainsKey(checkpoint.SessionId))
        {
            throw new InvalidOperationException($"Workflow is already running. SessionId={sessionId}");
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.Running)
        {
            throw new InvalidOperationException($"Workflow is already running. SessionId={sessionId}");
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.Completed)
        {
            throw new InvalidOperationException($"Completed workflow cannot be resumed. SessionId={sessionId}");
        }

        if (checkpoint.Status == WorkflowCheckpointStatus.WaitingForReview &&
            !checkpoint.Context.ContainsKey(WorkflowContextKeys.RejectionReason))
        {
            throw new InvalidOperationException($"Workflow is waiting for human review and cannot be resumed directly. SessionId={sessionId}");
        }

        var normalizedCheckpoint = NormalizeCheckpointForResume(checkpoint);
        await checkpointStorage.SaveCheckpointAsync(normalizedCheckpoint, cancellationToken);

        ScheduleBackgroundExecution(
            checkpoint.SessionId,
            cancellationToken: CancellationToken.None,
            executeAsync: token => workflowRunner.ResumeAsync(normalizedCheckpoint, _workflowExecutors, token));

        return new WorkflowResumeResponse(
            checkpoint.SessionId,
            checkpoint.WorkflowType,
            "legacy",
            "Running");
    }

    public async Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var checkpoint = await checkpointStorage.LoadCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint is null)
        {
            throw new InvalidOperationException($"Workflow session not found. SessionId={sessionId}");
        }

        if (checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled)
        {
            throw new InvalidOperationException($"Workflow cannot be cancelled from status {checkpoint.Status}. SessionId={sessionId}");
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

        return new WorkflowCancelResponse(
            sessionId,
            checkpoint.WorkflowType,
            "legacy",
            "Cancelled");
    }

    private async Task PersistCancellationAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var workflowSession = await dbContext.WorkflowSessions
            .SingleOrDefaultAsync(item => item.SessionId == checkpoint.SessionId, cancellationToken);

        if (workflowSession is null)
        {
            throw new InvalidOperationException($"Workflow session not found. SessionId={checkpoint.SessionId}");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        workflowSession.WorkflowType = checkpoint.WorkflowType;
        workflowSession.Status = checkpoint.Status.ToString();
        workflowSession.State = JsonSerializer.Serialize(checkpoint, CreateSerializerOptions());
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
            throw new InvalidOperationException($"Workflow is already running. SessionId={sessionId}");
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

        throw new InvalidOperationException(
            $"Unable to resolve database engine. Provide DatabaseEngine or a DatabaseId that includes mysql/postgres. DatabaseId={databaseId}, DatabaseEngine={databaseEngine}");
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private async Task CreateWorkflowSessionEntityAsync(
        Guid sessionId,
        string workflowType,
        string sourceType,
        Guid? sourceRefId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = workflowType,
            Status = "Running",
            State = "{}",
            EngineType = "legacy",
            EngineRunId = string.Empty,
            EngineCheckpointRef = string.Empty,
            SourceType = sourceType,
            SourceRefId = sourceRefId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WorkflowSessions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
