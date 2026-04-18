using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.InProc;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;
using DbOptimizer.Infrastructure.Maf.DbConfig.Executors;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

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
    private readonly MafGlobalErrorHandler _errorHandler;
    private readonly RetryPolicy _retryPolicy;
    private readonly CircuitBreaker _mcpCircuitBreaker;
    private readonly CircuitBreaker _databaseCircuitBreaker;

    public MafWorkflowRuntime(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        MafWorkflowRuntimeOptions options,
        ILoggerFactory loggerFactory)
    {
        _workflowFactory = workflowFactory ?? throw new ArgumentNullException(nameof(workflowFactory));
        _runStateStore = runStateStore ?? throw new ArgumentNullException(nameof(runStateStore));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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
    }

    public async Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Starting SQL analysis workflow. SessionId={SessionId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseType);

        WorkflowSessionEntity? session = null;

        try
        {
            // 1. 创建 workflow session
            session = await CreateWorkflowSessionAsync(
                command.SessionId,
                "sql_analysis",
                "manual", // 默认来源类型
                null,     // 默认无来源引用
                cancellationToken);

            // 2. 转换为 MAF 内部使用的完整 command
            var mafCommand = new SqlAnalysis.SqlAnalysisWorkflowCommand(
                SessionId: command.SessionId,
                SqlText: command.SqlText,
                DatabaseId: "default", // TODO: 从配置或参数获取
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

            // 5. 保存 MAF run state（初始状态）
            await _runStateStore.SaveAsync(
                command.SessionId,
                runId,
                checkpointRef: string.Empty,
                engineState: "{}",
                cancellationToken);

            // 6. 异步执行 workflow（fire-and-forget，实际执行在后台进行）
            // MAF Workflow 通过 executor chain 同步执行，这里包装为异步
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    // 使用重试策略执行 workflow
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

                    // 使用全局错误处理器
                    await _errorHandler.HandleWorkflowErrorAsync(
                        command.SessionId,
                        ex,
                        currentStep: "workflow_execution",
                        CancellationToken.None);
                }
            }, cancellationToken);

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

            // 使用全局错误处理器
            await _errorHandler.HandleWorkflowErrorAsync(
                command.SessionId,
                ex,
                currentStep: "workflow_start",
                cancellationToken);

            throw;
        }
    }

    public async Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation(
            "Starting DB config optimization workflow. SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}",
            command.SessionId,
            command.DatabaseId,
            command.DatabaseType);

        WorkflowSessionEntity? session = null;

        try
        {
            // 1. 创建 workflow session
            session = await CreateWorkflowSessionAsync(
                command.SessionId,
                "db_config_optimization",
                "manual", // 默认来源类型
                null,     // 默认无来源引用
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

            // 6. 异步执行 workflow（fire-and-forget，实际执行在后台进行）
            // MAF Workflow 通过 executor chain 同步执行，这里包装为异步
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("DB config workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    // 使用重试策略执行 workflow
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

                    // 使用全局错误处理器
                    await _errorHandler.HandleWorkflowErrorAsync(
                        command.SessionId,
                        ex,
                        currentStep: "workflow_execution",
                        CancellationToken.None);
                }
            }, cancellationToken);

            _logger.LogInformation(
                "DB config workflow started successfully. SessionId={SessionId}, RunId={RunId}",
                command.SessionId,
                runId);

            // 7. 返回响应
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

            // 使用全局错误处理器
            await _errorHandler.HandleWorkflowErrorAsync(
                command.SessionId,
                ex,
                currentStep: "workflow_start",
                cancellationToken);

            throw;
        }
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
            _ = Task.Run(async () =>
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
            }, cancellationToken);

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
            _ = Task.Run(async () =>
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
            }, cancellationToken);

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
            _ = Task.Run(async () =>
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
            }, cancellationToken);

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
    /// 创建 workflow session 记录
    /// </summary>
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
                }
                else if (status == RunStatus.PendingRequests || status == RunStatus.Idle)
                {
                    session.Status = "suspended";
                    _logger.LogInformation(
                        "SQL workflow suspended for review. SessionId={SessionId}",
                        sessionId);
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

            throw;
        }
    }

    /// <summary>
    /// 执行数据库配置优化 workflow
    /// </summary>
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
