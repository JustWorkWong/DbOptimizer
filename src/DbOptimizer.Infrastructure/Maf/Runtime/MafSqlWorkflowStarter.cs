using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

internal sealed class MafSqlWorkflowStarter
{
    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger<MafSqlWorkflowStarter> _logger;
    private readonly IWorkflowEventPublisher _eventPublisher;
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;

    public MafSqlWorkflowStarter(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        CheckpointManager checkpointManager,
        ILoggerFactory loggerFactory,
        IWorkflowEventPublisher eventPublisher,
        MafGlobalErrorHandler errorHandler,
        RetryPolicy retryPolicy)
    {
        _workflowFactory = workflowFactory;
        _runStateStore = runStateStore;
        _dbContextFactory = dbContextFactory;
        _checkpointManager = checkpointManager;
        _logger = loggerFactory.CreateLogger<MafSqlWorkflowStarter>();
        _eventPublisher = eventPublisher;
        _errorHandler = errorHandler;
        _retryPolicy = retryPolicy;
    }

    public async Task<PreparedSqlWorkflowStart> PrepareStartAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Preparing SQL analysis workflow. SessionId={SessionId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseType);

        try
        {
            await CreateWorkflowSessionAsync(
                command.SessionId,
                "sql_analysis",
                "manual",
                null,
                cancellationToken);

            var mafCommand = new SqlAnalysis.SqlAnalysisWorkflowCommand(
                SessionId: command.SessionId,
                SqlText: command.SqlText,
                DatabaseId: command.SessionId.ToString(),
                DatabaseEngine: command.DatabaseType,
                SourceType: "manual",
                SourceRefId: null,
                EnableIndexRecommendation: true,
                EnableSqlRewrite: true,
                RequireHumanReview: command.RequireHumanReview);

            var workflow = _workflowFactory.BuildSqlAnalysisWorkflow();
            var runId = $"maf_run_{Guid.NewGuid():N}";

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowStarted,
                    command.SessionId,
                    "sql_analysis",
                    DateTimeOffset.UtcNow,
                    new
                    {
                        runId,
                        message = "SQL analysis workflow started."
                    }),
                cancellationToken);

            await _runStateStore.SaveAsync(
                command.SessionId,
                runId,
                checkpointRef: "{}",
                engineState: "{}",
                cancellationToken);

            return new PreparedSqlWorkflowStart(
                command.SessionId,
                runId,
                workflow,
                mafCommand,
                new WorkflowStartResponse(command.SessionId, runId, "running"));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to prepare SQL workflow. SessionId={SessionId}",
                command.SessionId);

            await _errorHandler.HandleWorkflowErrorAsync(
                command.SessionId,
                ex,
                currentStep: "workflow_prepare",
                cancellationToken);

            throw;
        }
    }

    public async Task ExecutePreparedStartAsync(
        PreparedSqlWorkflowStart start,
        CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                await ExecuteSqlWorkflowAsync(
                    start.Workflow,
                    start.Command,
                    start.SessionId,
                    start.RunId,
                    ct);
                return true;
            },
            $"SqlWorkflow-{start.SessionId}",
            cancellationToken);
    }

    private async Task<WorkflowSessionEntity> CreateWorkflowSessionAsync(
        Guid sessionId,
        string workflowType,
        string sourceType,
        Guid? sourceRefId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = workflowType,
            Status = WorkflowSessionStatus.Running,
            State = "{}",
            EngineType = "maf",
            SourceType = sourceType,
            SourceRefId = sourceRefId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.WorkflowSessions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task ExecuteSqlWorkflowAsync(
        Workflow workflow,
        SqlAnalysis.SqlAnalysisWorkflowCommand command,
        Guid sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var run = await InProcessExecution.RunAsync(
                workflow,
                command,
                _checkpointManager,
                sessionId.ToString(),
                cancellationToken);

            var status = await run.GetStatusAsync(cancellationToken);
            var pendingRequest = run.OutgoingEvents
                .OfType<RequestInfoEvent>()
                .LastOrDefault();
            var checkpointRef = run.Checkpoints.LastOrDefault()?.CheckpointId;
            var superstep = run.Checkpoints.Count;
            var requestId = pendingRequest?.Request.RequestId;
            Guid? taskId = null;

            if (status == RunStatus.PendingRequests &&
                pendingRequest?.Request.TryGetDataAs<SqlReviewRequestMessage>(out var request) == true)
            {
                taskId = request.TaskId;
                await UpdateReviewCorrelationAsync(
                    request.TaskId,
                    pendingRequest.Request.RequestId,
                    runId,
                    checkpointRef ?? string.Empty,
                    cancellationToken);

                await _eventPublisher.PublishAsync(
                    new WorkflowEventMessage(
                        WorkflowEventType.CheckpointSaved,
                        sessionId,
                        "sql_analysis",
                        DateTimeOffset.UtcNow,
                        new
                        {
                            runId,
                            requestId = pendingRequest.Request.RequestId,
                            checkpointId = checkpointRef,
                            taskId,
                            superstep
                        }),
                    cancellationToken);
            }

            await UpdateSessionFromStatusAsync(sessionId, runId, status, checkpointRef, requestId, taskId, superstep, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SQL workflow execution failed. SessionId={SessionId}",
                sessionId);

            await UpdateSessionToFailedAsync(sessionId, runId, ex, cancellationToken);
            throw;
        }
    }

    private async Task UpdateReviewCorrelationAsync(
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var reviewTask = await dbContext.ReviewTasks.FindAsync([taskId], cancellationToken);
        if (reviewTask is null)
        {
            return;
        }

        reviewTask.RequestId = requestId;
        reviewTask.EngineRunId = runId;
        reviewTask.EngineCheckpointRef = checkpointRef;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateSessionFromStatusAsync(
        Guid sessionId,
        string runId,
        RunStatus status,
        string? checkpointId,
        string? requestId,
        Guid? taskId,
        int superstep,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is null)
        {
            return;
        }

        if (status == RunStatus.Ended)
        {
            session.Status = WorkflowSessionStatus.Completed;
            session.CompletedAt = DateTimeOffset.UtcNow;

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowCompleted,
                    sessionId,
                    "sql_analysis",
                    DateTimeOffset.UtcNow,
                    new
                    {
                        runId,
                        checkpointId,
                        superstep,
                        message = "SQL workflow completed."
                    }),
                cancellationToken);
        }
        else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
        {
            session.Status = WorkflowSessionStatus.WaitingForReview;

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowWaitingReview,
                    sessionId,
                    "sql_analysis",
                    DateTimeOffset.UtcNow,
                    new
                    {
                        runId,
                        requestId,
                        checkpointId,
                        taskId,
                        superstep,
                        message = "SQL workflow is waiting for review."
                    }),
                cancellationToken);
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateSessionToFailedAsync(
        Guid sessionId,
        string runId,
        Exception ex,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is not null)
        {
            session.Status = WorkflowSessionStatus.Failed;
            session.ErrorMessage = ex.Message;
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await _eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowFailed,
                sessionId,
                "sql_analysis",
                DateTimeOffset.UtcNow,
                new
                {
                    runId,
                    checkpointId = await TryGetCheckpointRefAsync(sessionId, cancellationToken),
                    errorMessage = ex.Message,
                    exceptionType = ex.GetType().Name,
                    superstep = 0
                }),
            cancellationToken);
    }

    private async Task<string?> TryGetCheckpointRefAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
        return runState?.CheckpointRef;
    }
}

internal sealed record PreparedSqlWorkflowStart(
    Guid SessionId,
    string RunId,
    Workflow Workflow,
    SqlAnalysis.SqlAnalysisWorkflowCommand Command,
    WorkflowStartResponse Response);
