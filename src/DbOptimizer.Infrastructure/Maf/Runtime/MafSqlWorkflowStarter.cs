using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

internal sealed class MafSqlWorkflowStarter
{
    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly ILogger<MafSqlWorkflowStarter> _logger;
    private readonly IWorkflowEventPublisher _eventPublisher;
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;
    private readonly CheckpointManager _checkpointManager;

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
                DatabaseId: "default",
                DatabaseEngine: command.DatabaseType,
                SourceType: "manual",
                SourceRefId: null,
                EnableIndexRecommendation: true,
                EnableSqlRewrite: true,
                RequireHumanReview: false);

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
                        command.DatabaseType,
                        sqlLength = command.SqlText.Length,
                        sqlPreview = BuildSqlPreview(command.SqlText)
                    }),
                cancellationToken);

            await _runStateStore.SaveAsync(
                command.SessionId,
                runId,
                checkpointRef: string.Empty,
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
                "Failed to prepare SQL analysis workflow. SessionId={SessionId}",
                command.SessionId);

            await _errorHandler.HandleWorkflowErrorAsync(
                command.SessionId,
                ex,
                currentStep: "workflow_start",
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
            $"SqlAnalysisWorkflow-{start.SessionId}",
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

        var session = new WorkflowSessionEntity
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

        dbContext.WorkflowSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created workflow session. SessionId={SessionId}, WorkflowType={WorkflowType}",
            sessionId,
            workflowType);

        return session;
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
            _logger.LogInformation(
                "Executing SQL workflow. SessionId={SessionId}, RunId={RunId}",
                sessionId,
                runId);

            var run = await InProcessExecution.RunAsync(
                workflow,
                command,
                _checkpointManager,
                sessionId.ToString(),
                cancellationToken);

            var status = await run.GetStatusAsync(cancellationToken);
            await UpdateSessionFromStatusAsync(sessionId, runId, status, cancellationToken);
        }
        catch (SqlAnalysis.Executors.WorkflowSuspendedException ex)
        {
            _logger.LogInformation(
                ex,
                "SQL workflow suspended. SessionId={SessionId}",
                sessionId);

            await UpdateSessionToSuspendedAsync(sessionId, runId, ex.Message, cancellationToken);
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
                    "sql_analysis",
                    DateTimeOffset.UtcNow,
                    new { runId, message = "SQL workflow completed." }),
                cancellationToken);
        }
        else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
        {
            session.Status = "suspended";

            await _eventPublisher.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.WorkflowWaitingReview,
                    sessionId,
                    "sql_analysis",
                    DateTimeOffset.UtcNow,
                    new { runId, message = "SQL workflow is waiting for review." }),
                cancellationToken);
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateSessionToSuspendedAsync(
        Guid sessionId,
        string runId,
        string message,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is not null)
        {
            session.Status = "suspended";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await _eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowWaitingReview,
                sessionId,
                "sql_analysis",
                DateTimeOffset.UtcNow,
                new { runId, message }),
            cancellationToken);
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
                "sql_analysis",
                DateTimeOffset.UtcNow,
                new
                {
                    runId,
                    errorMessage = ex.Message,
                    exceptionType = ex.GetType().Name
                }),
            cancellationToken);
    }

    private static string BuildSqlPreview(string sqlText)
    {
        const int maxLength = 160;
        var singleLine = sqlText.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : $"{singleLine[..maxLength]}...";
    }
}

internal sealed record PreparedSqlWorkflowStart(
    Guid SessionId,
    string RunId,
    Workflow Workflow,
    SqlAnalysis.SqlAnalysisWorkflowCommand Command,
    WorkflowStartResponse Response);
