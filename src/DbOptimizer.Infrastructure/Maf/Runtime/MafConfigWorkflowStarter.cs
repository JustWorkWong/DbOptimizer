using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

internal sealed class MafConfigWorkflowStarter
{
    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly CheckpointManager _checkpointManager;
    private readonly ILogger<MafConfigWorkflowStarter> _logger;
    private readonly IWorkflowEventPublisher _eventPublisher;
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;

    public MafConfigWorkflowStarter(
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
        _logger = loggerFactory.CreateLogger<MafConfigWorkflowStarter>();
        _eventPublisher = eventPublisher;
        _errorHandler = errorHandler;
        _retryPolicy = retryPolicy;
    }

    public async Task<PreparedConfigWorkflowStart> PrepareStartAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Preparing DB config optimization workflow. SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseId,
            command.DatabaseType);

        try
        {
            await CreateWorkflowSessionAsync(
                command.SessionId,
                "db_config_optimization",
                "manual",
                null,
                cancellationToken);

            var mafCommand = new DbConfig.DbConfigWorkflowCommand(
                SessionId: command.SessionId,
                DatabaseId: command.DatabaseId,
                DatabaseType: command.DatabaseType,
                AllowFallbackSnapshot: command.AllowFallbackSnapshot,
                RequireHumanReview: command.RequireHumanReview);

            var workflow = _workflowFactory.BuildDbConfigWorkflow();
            var runId = $"maf_run_{Guid.NewGuid():N}";

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowStarted,
                    command.SessionId,
                    "db_config_optimization",
                    DateTimeOffset.UtcNow,
                    new
                    {
                        runId,
                        message = "DB config optimization workflow started."
                    }),
                cancellationToken);

            await _runStateStore.SaveAsync(
                command.SessionId,
                runId,
                checkpointRef: "{}",
                engineState: "{}",
                cancellationToken);

            return new PreparedConfigWorkflowStart(
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
                "Failed to prepare DB config workflow. SessionId={SessionId}",
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
        PreparedConfigWorkflowStart start,
        CancellationToken cancellationToken = default)
    {
        await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                await ExecuteConfigWorkflowAsync(
                    start.Workflow,
                    start.Command,
                    start.SessionId,
                    start.RunId,
                    ct);
                return true;
            },
            $"DbConfigWorkflow-{start.SessionId}",
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
            Status = "running",
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

    private async Task ExecuteConfigWorkflowAsync(
        Workflow workflow,
        DbConfig.DbConfigWorkflowCommand command,
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

            if (status == RunStatus.PendingRequests &&
                pendingRequest?.Request.TryGetDataAs<ConfigReviewRequestMessage>(out var request) == true)
            {
                var checkpointRef = run.Checkpoints.LastOrDefault()?.CheckpointId ?? string.Empty;
                await UpdateReviewCorrelationAsync(
                    request.TaskId,
                    pendingRequest.Request.RequestId,
                    runId,
                    checkpointRef,
                    cancellationToken);
            }

            await UpdateSessionFromStatusAsync(sessionId, runId, status, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Config workflow execution failed. SessionId={SessionId}",
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
            session.Status = "completed";
            session.CompletedAt = DateTimeOffset.UtcNow;

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowCompleted,
                    sessionId,
                    "db_config_optimization",
                    DateTimeOffset.UtcNow,
                    new { runId, message = "Config workflow completed." }),
                cancellationToken);
        }
        else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
        {
            session.Status = "suspended";

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowWaitingReview,
                    sessionId,
                    "db_config_optimization",
                    DateTimeOffset.UtcNow,
                    new { runId, message = "Config workflow is waiting for review." }),
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
            session.Status = "failed";
            session.ErrorMessage = ex.Message;
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await _eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowFailed,
                sessionId,
                "db_config_optimization",
                DateTimeOffset.UtcNow,
                new
                {
                    runId,
                    errorMessage = ex.Message,
                    exceptionType = ex.GetType().Name
                }),
            cancellationToken);
    }
}

internal sealed record PreparedConfigWorkflowStart(
    Guid SessionId,
    string RunId,
    Workflow Workflow,
    DbConfig.DbConfigWorkflowCommand Command,
    WorkflowStartResponse Response);
