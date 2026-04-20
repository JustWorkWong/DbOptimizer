using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// SQL Workflow 启动器
/// </summary>
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

    public async Task<WorkflowStartResponse> StartAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Starting SQL analysis workflow. SessionId={SessionId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseType);

        try
        {
            // 1. 创建 workflow session
            var session = await CreateWorkflowSessionAsync(
                command.SessionId,
                "sql_analysis",
                "manual",
                null,
                cancellationToken);

            // 2. 转换为 MAF 内部使用的完整 command
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

            // 3. 构建 workflow graph
            var workflow = _workflowFactory.BuildSqlAnalysisWorkflow();

            // 4. 生成 runId
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

            // 5. 保存 MAF run state（初始状态）
            await _runStateStore.SaveAsync(
                command.SessionId,
                runId,
                checkpointRef: string.Empty,
                engineState: "{}",
                cancellationToken);

            // 6. 异步执行 workflow（fire-and-forget）
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    await _retryPolicy.ExecuteAsync(
                        async ct =>
                        {
                            await ExecuteSqlWorkflowAsync(workflow, mafCommand, command.SessionId, runId, ct);
                            return true;
                        },
                        $"SqlAnalysisWorkflow-{command.SessionId}",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow execution failed. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    await _errorHandler.HandleWorkflowErrorAsync(
                        command.SessionId,
                        ex,
                        currentStep: "workflow_execution",
                        CancellationToken.None);
                }
            }, CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogCritical(task.Exception,
                        "Unhandled exception in background SQL workflow task. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014

            _logger.LogInformation(
                "SQL analysis workflow started successfully. SessionId={SessionId}, RunId={RunId}",
                command.SessionId,
                runId);

            return new WorkflowStartResponse(
                SessionId: command.SessionId,
                RunId: runId,
                Status: "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start SQL analysis workflow. SessionId={SessionId}",
                command.SessionId);

            await _errorHandler.HandleWorkflowErrorAsync(
                command.SessionId,
                ex,
                currentStep: "workflow_start",
                cancellationToken);

            throw;
        }
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

    /// <summary>
    /// 执行 SQL 分析 workflow
    /// </summary>
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

            // 使用 MAF InProcessExecution 执行 workflow
            var run = await InProcessExecution.RunAsync(
                workflow,
                command,
                _checkpointManager,
                sessionId.ToString(),
                cancellationToken);

            // 获取 workflow 状态
            var status = await run.GetStatusAsync(cancellationToken);

            // 更新 session 状态
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

            if (session != null)
            {
                if (status == RunStatus.Ended)
                {
                    session.Status = "completed";
                    session.CompletedAt = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "SQL workflow completed successfully. SessionId={SessionId}",
                        sessionId);
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
                    _logger.LogInformation(
                        "SQL workflow suspended for review. SessionId={SessionId}",
                        sessionId);
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
        }
        catch (SqlAnalysis.Executors.WorkflowSuspendedException ex)
        {
            // Workflow 挂起等待审核
            _logger.LogInformation(
                ex,
                "SQL workflow suspended. SessionId={SessionId}",
                sessionId);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
            if (session != null)
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
                    new { runId, message = ex.Message }),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SQL workflow execution failed. SessionId={SessionId}",
                sessionId);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
            if (session != null)
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

            throw;
        }
    }

    private static string BuildSqlPreview(string sqlText)
    {
        const int maxLength = 160;
        var singleLine = sqlText.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : $"{singleLine[..maxLength]}...";
    }
}
