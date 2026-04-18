using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.DbConfig;

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

    public MafWorkflowRuntime(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        MafWorkflowRuntimeOptions options,
        ILogger<MafWorkflowRuntime> logger)
    {
        _workflowFactory = workflowFactory ?? throw new ArgumentNullException(nameof(workflowFactory));
        _runStateStore = runStateStore ?? throw new ArgumentNullException(nameof(runStateStore));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                    // MAF Workflow 没有 RunAsync/StartAsync，直接通过第一个 executor 触发
                    // 实际执行由 MAF 内部的 graph 驱动
                    _logger.LogInformation("Workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    // TODO: 实际的 MAF workflow 执行需要通过 WorkflowHost 或类似机制
                    // 当前版本先返回 running 状态，实际执行逻辑待 MAF API 确认后补充
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow execution failed. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);
                    await CleanupSessionOnFailureAsync(command.SessionId, ex.Message, CancellationToken.None);
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

            // 启动失败时清理 session
            if (session is not null)
            {
                await CleanupSessionOnFailureAsync(session.SessionId, ex.Message, cancellationToken);
            }

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
                    // MAF Workflow 没有 RunAsync/StartAsync，直接通过第一个 executor 触发
                    // 实际执行由 MAF 内部的 graph 驱动
                    _logger.LogInformation("DB config workflow execution started in background. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);

                    // TODO: 实际的 MAF workflow 执行需要通过 WorkflowHost 或类似机制
                    // 当前版本先返回 running 状态，实际执行逻辑待 MAF API 确认后补充
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DB config workflow execution failed. SessionId={SessionId}, RunId={RunId}",
                        command.SessionId, runId);
                    await CleanupSessionOnFailureAsync(command.SessionId, ex.Message, CancellationToken.None);
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

            // 启动失败时清理 session
            if (session is not null)
            {
                await CleanupSessionOnFailureAsync(session.SessionId, ex.Message, cancellationToken);
            }

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
            // MAF workflow 通过 ReviewGateExecutor.HandleReviewResponseAsync 恢复
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation(
                        "Workflow resume started in background. SessionId={SessionId}, RunId={RunId}",
                        sessionId,
                        runState.RunId);

                    // TODO: 实际的 MAF workflow 恢复需要通过 ReviewDecisionResponseMessage 触发
                    // 当前版本先返回 running 状态，实际执行逻辑待 MAF API 确认后补充
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Workflow resume failed. SessionId={SessionId}, RunId={RunId}",
                        sessionId,
                        runState.RunId);
                    await CleanupSessionOnFailureAsync(sessionId, ex.Message, CancellationToken.None);
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

    [Obsolete("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) instead.")]
    public Task<WorkflowCancelResponse> CancelAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MAF engine not implemented. Use WorkflowApplicationService instead.");
        throw new NotImplementedException("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) for workflow cancellation.");
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
    /// 启动失败时清理 session
    /// </summary>
    private async Task CleanupSessionOnFailureAsync(
        Guid sessionId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

            if (session is not null)
            {
                session.Status = "failed";
                session.ErrorMessage = errorMessage;
                session.UpdatedAt = DateTimeOffset.UtcNow;
                session.CompletedAt = DateTimeOffset.UtcNow;

                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Cleaned up failed workflow session. SessionId={SessionId}",
                    sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cleanup workflow session. SessionId={SessionId}",
                sessionId);
        }
    }
}
