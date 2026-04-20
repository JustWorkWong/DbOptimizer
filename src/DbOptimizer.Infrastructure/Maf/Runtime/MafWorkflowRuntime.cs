using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;
using DbOptimizer.Infrastructure.Maf.DbConfig.Executors;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 运行时实现
/// </summary>
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

        // 初始化错误处理组件
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

        // 初始化 Starter
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
        return await _sqlStarter.StartAsync(command, cancellationToken);
    }

    public async Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        return await _configStarter.StartAsync(command, cancellationToken);
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
            // 1. 读取 session 和 checkpoint
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
                throw new InvalidOperationException($"Session {sessionId} is not suspended (current status: {session.Status})");
            }

            // 2. 读取 checkpoint
            var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
            if (runState is null)
            {
                _logger.LogError("Checkpoint not found. SessionId={SessionId}", sessionId);
                throw new InvalidOperationException($"Checkpoint for session {sessionId} not found");
            }

            // 3. 获取 workflow graph
            var workflow = session.WorkflowType switch
            {
                "sql_analysis" => _workflowFactory.BuildSqlAnalysisWorkflow(),
                "db_config_optimization" => _workflowFactory.BuildDbConfigWorkflow(),
                _ => throw new InvalidOperationException($"Unknown workflow type: {session.WorkflowType}")
            };

            // 4. 更新 session 状态为 running
            session.Status = "running";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            // 5. 异步恢复 workflow 执行（fire-and-forget）
            // 注意：实际恢复需要通过 ResumeSqlWorkflowAsync 或 ResumeConfigWorkflowAsync
            // 传递 ReviewDecisionResponseMessage
#pragma warning disable CS4014 // Fire-and-forget is intentional
            Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Workflow resume started in background. SessionId={SessionId}, RunId={RunId}",
                        sessionId,
                        runState.RunId);

                    // TODO: 实现真正的 checkpoint 恢复
                    // 当前简化实现：标记为 running，等待 ResumeSqlWorkflowAsync 或 ResumeConfigWorkflowAsync 处理
                    _logger.LogWarning(
                        "Checkpoint resume not fully implemented. SessionId={SessionId}",
                        sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Workflow resume failed. SessionId={SessionId}, RunId={RunId}",
                        sessionId,
                        runState.RunId);

                    // 使用全局错误处理器
                    await _errorHandler.HandleWorkflowErrorAsync(
                        sessionId,
                        ex,
                        currentStep: "workflow_resume",
                        CancellationToken.None);
                }
            }, CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogCritical(task.Exception,
                        "Unhandled exception in background workflow resume task. SessionId={SessionId}, RunId={RunId}",
                        sessionId, runState.RunId);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014

            _logger.LogInformation(
                "Workflow resumed successfully. SessionId={SessionId}, RunId={RunId}",
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
            // 1. 验证 session 状态
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([reviewResponse.SessionId], cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} not found");
            }

            if (session.Status != "suspended")
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} is not suspended (current status: {session.Status})");
            }

            // 2. 更新 session 状态
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

            // 3. 如果是拒绝，直接返回
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

            // 4. 异步恢复 workflow 执行
#pragma warning disable CS4014 // Fire-and-forget is intentional
            Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "SQL workflow resume execution started. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    // TODO: 实现真正的 checkpoint 恢复和 review response 传递
                    // 当前简化实现：直接标记为 completed
                    await using var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                    var sess = await ctx.WorkflowSessions.FindAsync([reviewResponse.SessionId], CancellationToken.None);
                    if (sess != null)
                    {
                        sess.Status = "completed";
                        sess.CompletedAt = DateTimeOffset.UtcNow;
                        sess.UpdatedAt = DateTimeOffset.UtcNow;
                        await ctx.SaveChangesAsync(CancellationToken.None);
                    }

                    _logger.LogWarning(
                        "SQL workflow resume not fully implemented. SessionId={SessionId}",
                        reviewResponse.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "SQL workflow resume execution failed. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    // 使用全局错误处理器
                    await _errorHandler.HandleWorkflowErrorAsync(
                        reviewResponse.SessionId,
                        ex,
                        currentStep: "sql_workflow_resume",
                        CancellationToken.None);
                }
            }, CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogCritical(task.Exception,
                        "Unhandled exception in background SQL workflow resume task. SessionId={SessionId}",
                        reviewResponse.SessionId);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014

            _logger.LogInformation(
                "SQL workflow resumed successfully. SessionId={SessionId}",
                reviewResponse.SessionId);

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
            // 1. 验证 session 状态
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([reviewResponse.SessionId], cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} not found");
            }

            if (session.Status != "suspended")
            {
                throw new InvalidOperationException($"Session {reviewResponse.SessionId} is not suspended (current status: {session.Status})");
            }

            // 2. 更新 session 状态
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

            // 3. 如果是拒绝，直接返回
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

            // 4. 异步恢复 workflow 执行
#pragma warning disable CS4014 // Fire-and-forget is intentional
            Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Config workflow resume execution started. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    // TODO: 实现真正的 checkpoint 恢复和 review response 传递
                    // 当前简化实现：直接标记为 completed
                    await using var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                    var sess = await ctx.WorkflowSessions.FindAsync([reviewResponse.SessionId], CancellationToken.None);
                    if (sess != null)
                    {
                        sess.Status = "completed";
                        sess.CompletedAt = DateTimeOffset.UtcNow;
                        sess.UpdatedAt = DateTimeOffset.UtcNow;
                        await ctx.SaveChangesAsync(CancellationToken.None);
                    }

                    _logger.LogWarning(
                        "Config workflow resume not fully implemented. SessionId={SessionId}",
                        reviewResponse.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Config workflow resume execution failed. SessionId={SessionId}",
                        reviewResponse.SessionId);

                    // 使用全局错误处理器
                    await _errorHandler.HandleWorkflowErrorAsync(
                        reviewResponse.SessionId,
                        ex,
                        currentStep: "config_workflow_resume",
                        CancellationToken.None);
                }
            }, CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogCritical(task.Exception,
                        "Unhandled exception in background Config workflow resume task. SessionId={SessionId}",
                        reviewResponse.SessionId);
                }
            }, TaskScheduler.Default);
#pragma warning restore CS4014

            _logger.LogInformation(
                "Config workflow resumed successfully. SessionId={SessionId}",
                reviewResponse.SessionId);

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
            // 1. 读取 run state
            var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);
            if (runState == null)
            {
                _logger.LogWarning(
                    "Run state not found for session. SessionId={SessionId}",
                    sessionId);
                throw new InvalidOperationException($"Run state not found for session {sessionId}");
            }

            // 2. 获取 workflow session
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

            // 3. 验证 workflow type 是否有效
            // 注意：这里不实际构建 workflow，只验证类型
            // MAF 1.0.0-rc4 的 Workflow 类可能没有直接的 CancelAsync 方法
            // 取消操作通过更新 session 状态实现
            switch (session.WorkflowType)
            {
                case "sql_analysis":
                    // 验证可以构建 SQL workflow
                    _ = _workflowFactory.BuildSqlAnalysisWorkflow();
                    break;
                case "db_config_optimization":
                    // 验证可以构建 Config workflow
                    _ = _workflowFactory.BuildDbConfigWorkflow();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown workflow type: {session.WorkflowType}");
            }

            // 4. 更新 session 状态为 cancelled
            session.Status = "cancelled";
            session.UpdatedAt = DateTimeOffset.UtcNow;
            session.CompletedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            // 5. 清空 checkpoint 数据
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

            // 使用全局错误处理器
            await _errorHandler.HandleWorkflowErrorAsync(
                sessionId,
                ex,
                currentStep: "workflow_cancel",
                cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// 根据 MAF RunStatus 更新 session 状态
    /// </summary>
    private async Task UpdateSessionStatusFromStatusAsync(
        Guid sessionId,
        RunStatus status,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session != null)
        {
            if (status == RunStatus.Ended)
            {
                session.Status = "completed";
                session.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation(
                    "Workflow completed. SessionId={SessionId}",
                    sessionId);
            }
            else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
            {
                session.Status = "suspended";
                _logger.LogInformation(
                    "Workflow suspended. SessionId={SessionId}",
                    sessionId);
            }

            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
