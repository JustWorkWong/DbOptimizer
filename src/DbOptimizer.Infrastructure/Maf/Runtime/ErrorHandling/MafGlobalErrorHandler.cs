using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DbOptimizer.Infrastructure.Persistence;

namespace DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

/// <summary>
/// MAF 全局错误处理器
/// </summary>
public sealed class MafGlobalErrorHandler
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly ILogger<MafGlobalErrorHandler> _logger;

    public MafGlobalErrorHandler(
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        IMafRunStateStore runStateStore,
        ILogger<MafGlobalErrorHandler> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _runStateStore = runStateStore ?? throw new ArgumentNullException(nameof(runStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理 workflow 执行错误
    /// </summary>
    public async Task HandleWorkflowErrorAsync(
        Guid sessionId,
        Exception exception,
        string? currentStep = null,
        CancellationToken cancellationToken = default)
    {
        var category = MafErrorClassifier.Classify(exception);
        var userMessage = MafErrorClassifier.GetUserFriendlyMessage(category, exception);

        _logger.LogError(
            exception,
            "Workflow execution failed. SessionId={SessionId}, CurrentStep={CurrentStep}, ErrorCategory={Category}",
            sessionId,
            currentStep ?? "unknown",
            category);

        try
        {
            // 1. 尝试保存当前 checkpoint（如果可能）
            await TrySaveCheckpointOnErrorAsync(sessionId, cancellationToken);

            // 2. 更新 session 状态为 failed
            await UpdateSessionStatusToFailedAsync(
                sessionId,
                userMessage,
                exception,
                currentStep,
                category,
                cancellationToken);

            _logger.LogInformation(
                "Workflow error handled successfully. SessionId={SessionId}",
                sessionId);
        }
        catch (Exception handlerEx)
        {
            _logger.LogError(
                handlerEx,
                "Failed to handle workflow error. SessionId={SessionId}",
                sessionId);
        }
    }

    /// <summary>
    /// 尝试保存 checkpoint（失败时不抛出异常）
    /// </summary>
    private async Task TrySaveCheckpointOnErrorAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 尝试从 Redis 或数据库获取最新的 run state
            var runState = await _runStateStore.GetAsync(sessionId, cancellationToken);

            if (runState is not null)
            {
                // 保存当前状态（标记为失败状态在 session 中处理）
                await _runStateStore.SaveAsync(
                    sessionId,
                    runState.RunId,
                    runState.CheckpointRef,
                    runState.EngineState,
                    cancellationToken);

                _logger.LogInformation(
                    "Run state saved before marking workflow as failed. SessionId={SessionId}",
                    sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to save run state on error. SessionId={SessionId}",
                sessionId);
        }
    }

    /// <summary>
    /// 更新 session 状态为 failed
    /// </summary>
    private async Task UpdateSessionStatusToFailedAsync(
        Guid sessionId,
        string userMessage,
        Exception exception,
        string? currentStep,
        MafErrorCategory category,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is null)
        {
            _logger.LogWarning(
                "Session not found when handling error. SessionId={SessionId}",
                sessionId);
            return;
        }

        session.Status = "failed";
        session.ErrorMessage = userMessage;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        session.CompletedAt = DateTimeOffset.UtcNow;

        // 保存详细错误信息到 engine_state
        var errorDetails = new
        {
            errorCategory = category.ToString(),
            currentStep,
            exceptionType = exception.GetType().FullName,
            exceptionMessage = exception.Message,
            stackTrace = exception.StackTrace,
            innerException = exception.InnerException?.Message,
            timestamp = DateTimeOffset.UtcNow
        };

        session.EngineState = System.Text.Json.JsonSerializer.Serialize(errorDetails);

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Session status updated to failed. SessionId={SessionId}, ErrorCategory={Category}",
            sessionId,
            category);
    }

    /// <summary>
    /// 处理 executor 错误
    /// </summary>
    public async Task HandleExecutorErrorAsync(
        Guid sessionId,
        string executorName,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        var category = MafErrorClassifier.Classify(exception);

        _logger.LogError(
            exception,
            "Executor execution failed. SessionId={SessionId}, Executor={Executor}, ErrorCategory={Category}",
            sessionId,
            executorName,
            category);

        // 记录 executor 错误到数据库
        await RecordExecutorErrorAsync(
            sessionId,
            executorName,
            exception,
            category,
            cancellationToken);
    }

    /// <summary>
    /// 记录 executor 错误
    /// </summary>
    private async Task RecordExecutorErrorAsync(
        Guid sessionId,
        string executorName,
        Exception exception,
        MafErrorCategory category,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var errorRecord = new
            {
                sessionId,
                executorName,
                errorCategory = category.ToString(),
                exceptionType = exception.GetType().FullName,
                exceptionMessage = exception.Message,
                stackTrace = exception.StackTrace,
                timestamp = DateTimeOffset.UtcNow
            };

            // 这里可以扩展为专门的 executor_errors 表
            // 目前先记录到日志
            _logger.LogInformation(
                "Executor error recorded. SessionId={SessionId}, Executor={Executor}, ErrorCategory={Category}",
                sessionId,
                executorName,
                category);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record executor error. SessionId={SessionId}, Executor={Executor}",
                sessionId,
                executorName);
        }
    }
}
