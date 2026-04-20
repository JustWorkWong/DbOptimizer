using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class MafWorkflowRuntime : IMafWorkflowRuntime
{
    private static readonly string[] WaitingStatuses = ["suspended", "WaitingReview", "WaitingForReview"];

    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly MafWorkflowRuntimeOptions _options;
    private readonly ILogger<MafWorkflowRuntime> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IWorkflowEventPublisher _eventPublisher;
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;
    private readonly CircuitBreaker _mcpCircuitBreaker;
    private readonly CircuitBreaker _databaseCircuitBreaker;
    private readonly MafSqlWorkflowStarter _sqlStarter;
    private readonly MafConfigWorkflowStarter _configStarter;
    private readonly CheckpointManager _checkpointManager;

    public MafWorkflowRuntime(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        CheckpointManager checkpointManager,
        MafWorkflowRuntimeOptions options,
        ILoggerFactory loggerFactory,
        IWorkflowEventPublisher eventPublisher)
    {
        _workflowFactory = workflowFactory ?? throw new ArgumentNullException(nameof(workflowFactory));
        _runStateStore = runStateStore ?? throw new ArgumentNullException(nameof(runStateStore));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _checkpointManager = checkpointManager ?? throw new ArgumentNullException(nameof(checkpointManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = loggerFactory.CreateLogger<MafWorkflowRuntime>();

        _errorHandler = new MafGlobalErrorHandler(
            _dbContextFactory,
            _runStateStore,
            _loggerFactory.CreateLogger<MafGlobalErrorHandler>());

        var retryConfig = new RetryPolicyConfig
        {
            MaxRetryAttempts = 3,
            InitialDelayMs = 100,
            MaxDelayMs = 5000,
            BackoffMultiplier = 2.0,
            EnableJitter = true
        };
        _retryPolicy = new RetryPolicy(retryConfig, _loggerFactory.CreateLogger<RetryPolicy>());

        var circuitBreakerConfig = new CircuitBreakerConfig
        {
            FailureThreshold = 5,
            SuccessThreshold = 2,
            TimeoutMs = 30000
        };
        _mcpCircuitBreaker = new CircuitBreaker(circuitBreakerConfig, _loggerFactory.CreateLogger<CircuitBreaker>());
        _databaseCircuitBreaker = new CircuitBreaker(circuitBreakerConfig, _loggerFactory.CreateLogger<CircuitBreaker>());

        _sqlStarter = new MafSqlWorkflowStarter(
            _workflowFactory,
            _runStateStore,
            _dbContextFactory,
            _checkpointManager,
            _loggerFactory,
            _eventPublisher,
            _errorHandler,
            _retryPolicy);

        _configStarter = new MafConfigWorkflowStarter(
            _workflowFactory,
            _runStateStore,
            _dbContextFactory,
            _checkpointManager,
            _loggerFactory,
            _eventPublisher,
            _errorHandler,
            _retryPolicy);
    }

    public async Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        var start = await _sqlStarter.PrepareStartAsync(command, cancellationToken);

        RunInBackground(
            ct => _sqlStarter.ExecutePreparedStartAsync(start, ct),
            ex => _errorHandler.HandleWorkflowErrorAsync(
                start.SessionId,
                ex,
                currentStep: "workflow_execution",
                CancellationToken.None),
            $"Unhandled exception in background SQL workflow task. SessionId={start.SessionId}, RunId={start.RunId}");

        return start.Response;
    }

    public async Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        var start = await _configStarter.PrepareStartAsync(command, cancellationToken);

        RunInBackground(
            ct => _configStarter.ExecutePreparedStartAsync(start, ct),
            ex => _errorHandler.HandleWorkflowErrorAsync(
                start.SessionId,
                ex,
                currentStep: "workflow_execution",
                CancellationToken.None),
            $"Unhandled exception in background DB config workflow task. SessionId={start.SessionId}, RunId={start.RunId}");

        return start.Response;
    }

    public async Task<WorkflowResumeResponse> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var (session, runState, workflow) = await LoadResumeContextAsync(sessionId, cancellationToken);

        session.Status = "running";
        session.UpdatedAt = DateTimeOffset.UtcNow;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.Attach(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        await using var run = await InProcessExecution.ResumeAsync(
            workflow,
            new CheckpointInfo(sessionId.ToString(), runState.CheckpointRef),
            _checkpointManager,
            cancellationToken);

        var status = await run.GetStatusAsync(cancellationToken);
        await UpdateSessionAfterResumeAsync(sessionId, runState.RunId, status, null, cancellationToken);

        return new WorkflowResumeResponse(sessionId, MapResumeStatus(status));
    }

    public Task<WorkflowResumeResponse> ResumeSqlWorkflowAsync(
        Guid sessionId,
        ExternalResponse reviewResponse,
        CancellationToken cancellationToken = default)
    {
        return ResumeWithReviewResponseAsync(sessionId, reviewResponse, cancellationToken);
    }

    public Task<WorkflowResumeResponse> ResumeConfigWorkflowAsync(
        Guid sessionId,
        ExternalResponse reviewResponse,
        CancellationToken cancellationToken = default)
    {
        return ResumeWithReviewResponseAsync(sessionId, reviewResponse, cancellationToken);
    }

    public async Task<WorkflowCancelResponse> CancelAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancelling workflow. SessionId={SessionId}",
            sessionId);

        try
        {
            var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
            if (runState == null)
            {
                _logger.LogWarning(
                    "Run state not found for session. SessionId={SessionId}",
                    sessionId);
                throw new InvalidOperationException($"Run state not found for session {sessionId}");
            }

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

            if (session == null)
            {
                _logger.LogWarning(
                    "Workflow session not found. SessionId={SessionId}",
                    sessionId);
                throw new InvalidOperationException($"Workflow session not found: {sessionId}");
            }

            switch (session.WorkflowType)
            {
                case "sql_analysis":
                    _ = _workflowFactory.BuildSqlAnalysisWorkflow();
                    break;
                case "db_config_optimization":
                    _ = _workflowFactory.BuildDbConfigWorkflow();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown workflow type: {session.WorkflowType}");
            }

            session.Status = "cancelled";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            session.CompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await _runStateStore.DeleteAsync(sessionId, cancellationToken);

            _logger.LogInformation(
                "Workflow cancellation completed. SessionId={SessionId}, RunId={RunId}",
                sessionId,
                runState.RunId);

            return new WorkflowCancelResponse(
                SessionId: sessionId,
                Status: "cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cancel workflow. SessionId={SessionId}",
                sessionId);

            await _errorHandler.HandleWorkflowErrorAsync(
                sessionId,
                ex,
                currentStep: "workflow_cancel",
                cancellationToken);

            throw;
        }
    }

    private async Task<WorkflowResumeResponse> ResumeWithReviewResponseAsync(
        Guid sessionId,
        ExternalResponse reviewResponse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewResponse);

        var (session, runState, workflow) = await LoadResumeContextAsync(sessionId, cancellationToken);
        await UpdateSessionStatusAsync(sessionId, "running", cancellationToken);

        await using var run = await InProcessExecution.ResumeAsync(
            workflow,
            new CheckpointInfo(sessionId.ToString(), runState.CheckpointRef),
            _checkpointManager,
            cancellationToken);

        try
        {
            await run.ResumeAsync([reviewResponse], cancellationToken);
            var status = await run.GetStatusAsync(cancellationToken);
            await UpdateSessionAfterResumeAsync(sessionId, runState.RunId, status, null, cancellationToken);
            return new WorkflowResumeResponse(sessionId, MapResumeStatus(status));
        }
        catch (Exception ex)
        {
            await MarkSessionFailedAsync(sessionId, runState.RunId, ex, cancellationToken);
            throw;
        }
    }

    private async Task<(WorkflowSessionEntity Session, MafRunState RunState, Workflow Workflow)> LoadResumeContextAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (!WaitingStatuses.Any(status => string.Equals(status, session.Status, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Session {sessionId} is not waiting for review (current status: {session.Status})");
        }

        var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
        if (runState is null)
        {
            throw new InvalidOperationException($"Checkpoint for session {sessionId} not found");
        }

        var workflow = session.WorkflowType switch
        {
            "sql_analysis" => _workflowFactory.BuildSqlAnalysisWorkflow(),
            "db_config_optimization" => _workflowFactory.BuildDbConfigWorkflow(),
            _ => throw new InvalidOperationException($"Unknown workflow type: {session.WorkflowType}")
        };

        return (session, runState, workflow);
    }

    private async Task UpdateSessionStatusAsync(
        Guid sessionId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Status = status;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateSessionAfterResumeAsync(
        Guid sessionId,
        string runId,
        RunStatus status,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
        if (session is null)
        {
            return;
        }

        switch (status)
        {
            case RunStatus.Ended:
                session.Status = "completed";
                session.CompletedAt = DateTimeOffset.UtcNow;
                session.ErrorMessage = null;
                await _eventPublisher.PublishAsync(
                    new WorkflowEventMessage(
                        WorkflowEventType.WorkflowCompleted,
                        sessionId,
                        session.WorkflowType,
                        DateTimeOffset.UtcNow,
                        new { runId, message = "Workflow completed." }),
                    cancellationToken);
                break;

            case RunStatus.PendingRequests:
            case RunStatus.Idle:
                session.Status = "suspended";
                await _eventPublisher.PublishAsync(
                    new WorkflowEventMessage(
                        WorkflowEventType.WorkflowWaitingReview,
                        sessionId,
                        session.WorkflowType,
                        DateTimeOffset.UtcNow,
                        new { runId, message = "Workflow is waiting for review." }),
                    cancellationToken);
                break;

            default:
                session.Status = "failed";
                session.ErrorMessage = exception?.Message ?? "Workflow resume failed.";
                session.CompletedAt = DateTimeOffset.UtcNow;
                await _eventPublisher.PublishAsync(
                    new WorkflowEventMessage(
                        WorkflowEventType.WorkflowFailed,
                        sessionId,
                        session.WorkflowType,
                        DateTimeOffset.UtcNow,
                        new
                        {
                            runId,
                            errorMessage = exception?.Message ?? "Workflow resume failed.",
                            exceptionType = exception?.GetType().Name
                        }),
                    cancellationToken);
                break;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkSessionFailedAsync(
        Guid sessionId,
        string runId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Status = "failed";
        session.ErrorMessage = exception.Message;
        session.CompletedAt = DateTimeOffset.UtcNow;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        await _eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowFailed,
                sessionId,
                session.WorkflowType,
                DateTimeOffset.UtcNow,
                new
                {
                    runId,
                    errorMessage = exception.Message,
                    exceptionType = exception.GetType().Name
                }),
            cancellationToken);
    }

    private static string MapResumeStatus(RunStatus status)
    {
        return status switch
        {
            RunStatus.Ended => "completed",
            RunStatus.PendingRequests or RunStatus.Idle => "suspended",
            _ => "failed"
        };
    }

    private void RunInBackground(
        Func<CancellationToken, Task> work,
        Func<Exception, Task> handleError,
        string unhandledLogMessage)
    {
#pragma warning disable CS4014
        Task.Run(async () =>
        {
            try
            {
                await work(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", unhandledLogMessage);
                await handleError(ex);
            }
        }, CancellationToken.None)
        .ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                _logger.LogCritical(task.Exception, "{Message}", unhandledLogMessage);
            }
        }, TaskScheduler.Default);
#pragma warning restore CS4014
    }
}
