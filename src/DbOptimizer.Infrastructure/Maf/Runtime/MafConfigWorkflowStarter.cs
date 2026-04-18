using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// Config Workflow 启动器
/// </summary>
internal sealed class MafConfigWorkflowStarter
{
    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly ILogger<MafConfigWorkflowStarter> _logger;
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;

    public MafConfigWorkflowStarter(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        ILoggerFactory loggerFactory,
        MafGlobalErrorHandler errorHandler,
        RetryPolicy retryPolicy)
    {
        _workflowFactory = workflowFactory;
        _runStateStore = runStateStore;
        _dbContextFactory = dbContextFactory;
        _logger = loggerFactory.CreateLogger<MafConfigWorkflowStarter>();
        _errorHandler = errorHandler;
        _retryPolicy = retryPolicy;
    }

    public async Task<WorkflowStartResponse> StartAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Starting DB config optimization workflow. SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseId,
            command.DatabaseType);

        try
        {
            // 1. 创建 workflow session
            var session = await CreateWorkflowSessionAsync(
                command.SessionId,
                "db_config_optimization",
                "manual",
                null,
                cancellationToken);

            // 2. 转换为 MAF 内部使用的完整 command
            var mafCommand = new DbConfig.DbConfigWorkflowCommand(
                SessionId: command.SessionId,
                DatabaseId: command.DatabaseId,
                DatabaseType: command.DatabaseType,
                AllowFallbackSnapshot: command.AllowFallbackSnapshot,
                RequireHumanReview: command.RequireHumanReview);

            // 3. 构建 workflow graph
            var workflow = _workflowFactory.BuildDbConfigWorkflow();

            // 4. 生成 runId
            var runId = $"maf_run_{Guid.NewGuid():N}";

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
                    _logger.LogInformation("DB config workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    await _retryPolicy.ExecuteAsync(
                        async ct =>
                        {
                            await ExecuteConfigWorkflowAsync(workflow, mafCommand, command.SessionId, runId, ct);
                            return true;
                        },
                        $"DbConfigWorkflow-{command.SessionId}",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DB config workflow execution failed. SessionId={SessionId}, RunId={RunId}",
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
                        "Unhandled exception in background DB config workflow task. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014

            _logger.LogInformation(
                "DB config workflow started successfully. SessionId={SessionId}, RunId={RunId}",
                command.SessionId,
                runId);

            return new WorkflowStartResponse(
                SessionId: command.SessionId,
                RunId: runId,
                Status: "running");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start DB config workflow. SessionId={SessionId}",
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

    private async Task ExecuteConfigWorkflowAsync(
        Workflow workflow,
        DbConfig.DbConfigWorkflowCommand command,
        Guid sessionId,
        string runId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Executing Config workflow. SessionId={SessionId}, RunId={RunId}",
                sessionId,
                runId);

            // 使用 MAF InProcessExecution 执行 workflow
            var run = await InProcessExecution.RunAsync(
                workflow,
                command,
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
                        "Config workflow completed successfully. SessionId={SessionId}",
                        sessionId);
                }
                else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
                {
                    session.Status = "suspended";
                    _logger.LogInformation(
                        "Config workflow suspended for review. SessionId={SessionId}",
                        sessionId);
                }

                session.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbConfig.Executors.WorkflowSuspendedException ex)
        {
            // Workflow 挂起等待审核
            _logger.LogInformation(
                ex,
                "Config workflow suspended. SessionId={SessionId}",
                sessionId);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);
            if (session != null)
            {
                session.Status = "suspended";
                session.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Config workflow execution failed. SessionId={SessionId}",
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

            throw;
        }
    }
}
