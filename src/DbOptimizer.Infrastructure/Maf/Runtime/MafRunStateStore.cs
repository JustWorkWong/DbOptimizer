using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using System.Diagnostics;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Run 状态存储实现
/// 设计目标：
/// 1) PostgreSQL 保存权威状态，保证进程重启后可恢复
/// 2) Redis 保存热点副本，加速读取
/// 3) Redis 不可用时优先保证 PostgreSQL 落库成功
/// 4) 使用 Redis pipelining 优化批量操作
/// 5) 使用 EF Core compiled queries 优化查询性能
/// </summary>
public sealed class MafRunStateStore : IMafRunStateStore
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MafRunStateStore> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    // Compiled query for better performance
    private static readonly Func<DbOptimizerDbContext, Guid, Task<WorkflowSessionEntity?>> GetSessionQuery =
        EF.CompileAsyncQuery((DbOptimizerDbContext ctx, Guid sessionId) =>
            ctx.WorkflowSessions
                .AsNoTracking()
                .FirstOrDefault(x => x.SessionId == sessionId));

    public MafRunStateStore(
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        IConnectionMultiplexer redis,
        ILogger<MafRunStateStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _redis = redis;
        _logger = logger;
    }

    public async Task SaveAsync(
        Guid sessionId,
        string runId,
        string checkpointRef,
        string engineState,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var now = DateTime.UtcNow;

        // 保存到 PostgreSQL
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is null)
        {
            throw new InvalidOperationException($"WorkflowSession {sessionId} not found.");
        }

        session.EngineRunId = runId;
        session.EngineCheckpointRef = checkpointRef;
        session.EngineState = engineState;
        session.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "Saved MAF run state for session {SessionId}, runId={RunId}, dbDuration={Duration}ms",
            sessionId,
            runId,
            stopwatch.ElapsedMilliseconds);

        // 同步到 Redis（失败不影响主流程）
        await TryCacheStateAsync(sessionId, runId, checkpointRef, engineState, now);
    }

    public async Task<MafRunState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 优先从 Redis 读取
        var cachedState = await TryLoadFromRedisAsync(sessionId);
        if (cachedState is not null)
        {
            stopwatch.Stop();
            _logger.LogDebug(
                "Loaded MAF state from Redis cache for session {SessionId}, duration={Duration}ms",
                sessionId,
                stopwatch.ElapsedMilliseconds);
            return cachedState;
        }

        // Redis miss，从 PostgreSQL 读取（使用 compiled query）
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await GetSessionQuery(dbContext, sessionId);

        if (session is null ||
            string.IsNullOrWhiteSpace(session.EngineRunId) ||
            string.IsNullOrWhiteSpace(session.EngineCheckpointRef))
        {
            return null;
        }

        var state = new MafRunState(
            session.SessionId,
            session.EngineRunId,
            session.EngineCheckpointRef,
            session.EngineState ?? "{}",
            session.CreatedAt.UtcDateTime,
            session.UpdatedAt.UtcDateTime);

        stopwatch.Stop();

        _logger.LogInformation(
            "Loaded MAF state from PostgreSQL for session {SessionId}, duration={Duration}ms",
            sessionId,
            stopwatch.ElapsedMilliseconds);

        // 回写 Redis
        await TryCacheStateAsync(
            state.SessionId,
            state.RunId,
            state.CheckpointRef,
            state.EngineState,
            state.UpdatedAt ?? state.CreatedAt);

        return state;
    }

    public async Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // 从 Redis 删除
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(BuildCacheKey(sessionId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete MAF state cache for session {SessionId}.", sessionId);
        }

        // 从 PostgreSQL 清空 checkpoint 字段
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([sessionId], cancellationToken);

        if (session is not null)
        {
            session.EngineRunId = null;
            session.EngineCheckpointRef = null;
            session.EngineState = null;
            session.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Deleted MAF run state for session {SessionId}", sessionId);
    }

    private async Task<MafRunState?> TryLoadFromRedisAsync(Guid sessionId)
    {
        try
        {
            var cachedValue = await _redis.GetDatabase().StringGetAsync(BuildCacheKey(sessionId));
            if (!cachedValue.HasValue)
            {
                return null;
            }

            return JsonSerializer.Deserialize<MafRunState>(cachedValue.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to load MAF state cache for session {SessionId}. Falling back to PostgreSQL.",
                sessionId);
            return null;
        }
    }

    private async Task TryCacheStateAsync(
        Guid sessionId,
        string runId,
        string checkpointRef,
        string engineState,
        DateTime updatedAt)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var state = new MafRunState(
                sessionId,
                runId,
                checkpointRef,
                engineState,
                updatedAt,
                updatedAt);

            var serialized = JsonSerializer.Serialize(state);

            // 使用 Redis pipelining 优化性能
            var db = _redis.GetDatabase();
            var batch = db.CreateBatch();
            var setTask = batch.StringSetAsync(
                BuildCacheKey(sessionId),
                serialized,
                CacheTtl);

            batch.Execute();
            await setTask;

            stopwatch.Stop();

            _logger.LogDebug(
                "Cached MAF state to Redis for session {SessionId}, duration={Duration}ms",
                sessionId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache MAF state for session {SessionId}.", sessionId);
        }
    }

    private static string BuildCacheKey(Guid sessionId)
    {
        return $"checkpoint:{sessionId:N}";
    }
}
