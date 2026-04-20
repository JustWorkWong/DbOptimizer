using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class MafWorkflowRuntime : IMafWorkflowRuntime
{
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
        _logger.LogInformation(
            "Resuming workflow. SessionId={SessionId}",
            sessionId);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

            if (session is null)
            {
                _logger.LogError("Session not found. SessionId={SessionId}", sessionId);
                throw new InvalidOperationException($"Session {sessionId} not found");
            }

            if (session.Status != "suspended")
            {
                _logger.LogError(
                    "Session is not suspended. SessionId={SessionId}, Status={Status}",
                    sessionId,
                    session.Status);
                throw new InvalidOperationException(
                    $"Session {sessionId} is not suspended (current status: {session.Status})");
            }

            var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
            if (runState is null)
            {
                _logger.LogError("Checkpoint not found. SessionId={SessionId}", sessionId);
                throw new InvalidOperationException($"Checkpoint for session {sessionId} not found");
            }

            _ = session.WorkflowType switch
            {
                "sql_analysis" => _workflowFactory.BuildSqlAnalysisWorkflow(),
                "db_config_optimization" => _workflowFactory.BuildDbConfigWorkflow(),
                _ => throw new InvalidOperationException($"Unknown workflow type: {session.WorkflowType}")
            };

            session.Status = "running";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Checkpoint resume is not fully implemented yet. SessionId={SessionId}, RunId={RunId}",
                sessionId,
                runState.RunId);

            return new WorkflowResumeResponse(
                SessionId: sessionId,
                Status: "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resume workflow. SessionId={SessionId}",
                sessionId);
            throw;
        }
    }

    public async Task<WorkflowResumeResponse> ResumeSqlWorkflowAsync(
        SqlAnalysis.ReviewDecisionResponseMessage reviewResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewResponse);

        _logger.LogInformation(
            "Resuming SQL workflow with review response. SessionId={SessionId}, TaskId={TaskId}, Action={Action}",
            reviewResponse.SessionId,
            reviewResponse.TaskId,
            reviewResponse.Action);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([reviewResponse.SessionId], cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} not found");
            }

            if (session.Status != "suspended")
            {
                throw new InvalidOperationException(
                    $"Session {reviewResponse.SessionId} is not suspended (current status: {session.Status})");
            }

            if (reviewResponse.Action == "reject")
            {
                session.Status = "failed";
                session.ErrorMessage = $"Review rejected: {reviewResponse.Comment}";
                session.CompletedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                session.Status = "running";
            }

            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (reviewResponse.Action == "reject")
            {
                _logger.LogWarning(
                    "SQL workflow rejected. SessionId={SessionId}, Comment={Comment}",
                    reviewResponse.SessionId,
                    reviewResponse.Comment);

                return new WorkflowResumeResponse(
                    SessionId: reviewResponse.SessionId,
                    Status: "failed");
            }

            RunInBackground(
                async _ =>
                {
                    _logger.LogInformation(
                        "SQL workflow resume execution started. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    await using var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                    var resumedSession = await ctx.WorkflowSessions.FindAsync([reviewResponse.SessionId], CancellationToken.None);
                    if (resumedSession is not null)
                    {
                        resumedSession.Status = "completed";
                        resumedSession.CompletedAt = DateTimeOffset.UtcNow;
                        resumedSession.UpdatedAt = DateTimeOffset.UtcNow;
                        await ctx.SaveChangesAsync(CancellationToken.None);
                    }

                    _logger.LogWarning(
                        "SQL workflow resume not fully implemented. SessionId={SessionId}",
                        reviewResponse.SessionId);
                },
                ex => _errorHandler.HandleWorkflowErrorAsync(
                    reviewResponse.SessionId,
                    ex,
                    currentStep: "sql_workflow_resume",
                    CancellationToken.None),
                $"Unhandled exception in background SQL workflow resume task. SessionId={reviewResponse.SessionId}");

            return new WorkflowResumeResponse(
                SessionId: reviewResponse.SessionId,
                Status: "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resume SQL workflow. SessionId={SessionId}",
                reviewResponse.SessionId);
            throw;
        }
    }

    public async Task<WorkflowResumeResponse> ResumeConfigWorkflowAsync(
        DbConfig.ConfigReviewDecisionResponseMessage reviewResponse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewResponse);

        _logger.LogInformation(
            "Resuming Config workflow with review response. SessionId={SessionId}, TaskId={TaskId}, Action={Action}",
            reviewResponse.SessionId,
            reviewResponse.TaskId,
            reviewResponse.Action);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([reviewResponse.SessionId], cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} not found");
            }

            if (session.Status != "suspended")
            {
                throw new InvalidOperationException(
                    $"Session {reviewResponse.SessionId} is not suspended (current status: {session.Status})");
            }

            if (reviewResponse.Action == "reject")
            {
                session.Status = "failed";
                session.ErrorMessage = $"Review rejected: {reviewResponse.Comment}";
                session.CompletedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                session.Status = "running";
            }

            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (reviewResponse.Action == "reject")
            {
                _logger.LogWarning(
                    "Config workflow rejected. SessionId={SessionId}, Comment={Comment}",
                    reviewResponse.SessionId,
                    reviewResponse.Comment);

                return new WorkflowResumeResponse(
                    SessionId: reviewResponse.SessionId,
                    Status: "failed");
            }

            RunInBackground(
                async _ =>
                {
                    _logger.LogInformation(
                        "Config workflow resume execution started. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    await using var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                    var resumedSession = await ctx.WorkflowSessions.FindAsync([reviewResponse.SessionId], CancellationToken.None);
                    if (resumedSession is not null)
                    {
                        resumedSession.Status = "completed";
                        resumedSession.CompletedAt = DateTimeOffset.UtcNow;
                        resumedSession.UpdatedAt = DateTimeOffset.UtcNow;
                        await ctx.SaveChangesAsync(CancellationToken.None);
                    }

                    _logger.LogWarning(
                        "Config workflow resume not fully implemented. SessionId={SessionId}",
                        reviewResponse.SessionId);
                },
                ex => _errorHandler.HandleWorkflowErrorAsync(
                    reviewResponse.SessionId,
                    ex,
                    currentStep: "config_workflow_resume",
                    CancellationToken.None),
                $"Unhandled exception in background Config workflow resume task. SessionId={reviewResponse.SessionId}");

            return new WorkflowResumeResponse(
                SessionId: reviewResponse.SessionId,
                Status: "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resume Config workflow. SessionId={SessionId}",
                reviewResponse.SessionId);
            throw;
        }
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
