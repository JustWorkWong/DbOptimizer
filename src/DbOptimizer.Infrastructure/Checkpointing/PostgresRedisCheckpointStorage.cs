using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace DbOptimizer.Infrastructure.Checkpointing;

/* =========================
 * Checkpoint 双层存储
 * 设计目标：
 * 1) PostgreSQL 保存权威快照，保证进程重启后仍可恢复
 * 2) Redis 保存热点副本，减少恢复与轮询场景的重复读库
 * 3) Redis 不可用时优先保证 PostgreSQL 落库成功
 * ========================= */
internal sealed class PostgresRedisCheckpointStorage(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IConnectionMultiplexer connectionMultiplexer,
    ILogger<PostgresRedisCheckpointStorage> logger) : ICheckpointStorage
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public async Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        var normalizedCheckpoint = NormalizeCheckpoint(checkpoint);
        var serializedCheckpoint = JsonSerializer.Serialize(normalizedCheckpoint, SerializerOptions);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions.FindAsync([normalizedCheckpoint.SessionId], cancellationToken);

        if (session is null)
        {
            session = new WorkflowSessionEntity
            {
                SessionId = normalizedCheckpoint.SessionId,
                WorkflowType = normalizedCheckpoint.WorkflowType,
                Status = normalizedCheckpoint.Status.ToString(),
                State = serializedCheckpoint,
                CreatedAt = normalizedCheckpoint.CreatedAt,
                UpdatedAt = normalizedCheckpoint.UpdatedAt,
                CompletedAt = ResolveCompletedAt(normalizedCheckpoint)
            };

            dbContext.WorkflowSessions.Add(session);
        }
        else
        {
            session.WorkflowType = normalizedCheckpoint.WorkflowType;
            session.Status = normalizedCheckpoint.Status.ToString();
            session.State = serializedCheckpoint;
            session.UpdatedAt = normalizedCheckpoint.UpdatedAt;
            session.CompletedAt = ResolveCompletedAt(normalizedCheckpoint);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await TryCacheCheckpointAsync(normalizedCheckpoint, serializedCheckpoint);
    }

    public async Task<WorkflowCheckpoint?> LoadCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var cachedCheckpoint = await TryLoadFromRedisAsync(sessionId);
        if (cachedCheckpoint is not null)
        {
            return cachedCheckpoint;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (session is null || string.IsNullOrWhiteSpace(session.State))
        {
            return null;
        }

        var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpoint>(session.State, SerializerOptions);
        if (checkpoint is null)
        {
            return null;
        }

        await TryCacheCheckpointAsync(checkpoint, session.State);
        return checkpoint;
    }

    public async Task DeleteCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await connectionMultiplexer.GetDatabase().KeyDeleteAsync(BuildCacheKey(sessionId));
        }
        catch (Exception ex)
        {
            // 删除热缓存失败不应影响主存储中的会话追溯能力。
            logger.LogWarning(ex, "Failed to delete checkpoint cache for session {SessionId}.", sessionId);
        }
    }

    private async Task<WorkflowCheckpoint?> TryLoadFromRedisAsync(Guid sessionId)
    {
        try
        {
            var cachedValue = await connectionMultiplexer.GetDatabase().StringGetAsync(BuildCacheKey(sessionId));
            if (!cachedValue.HasValue)
            {
                return null;
            }

            return JsonSerializer.Deserialize<WorkflowCheckpoint>(cachedValue.ToString(), SerializerOptions);
        }
        catch (Exception ex)
        {
            // Redis 作为热缓存，读取失败时直接回退到 PostgreSQL。
            logger.LogWarning(ex, "Failed to load checkpoint cache for session {SessionId}. Falling back to PostgreSQL.", sessionId);
            return null;
        }
    }

    private async Task TryCacheCheckpointAsync(WorkflowCheckpoint checkpoint, string serializedCheckpoint)
    {
        try
        {
            await connectionMultiplexer.GetDatabase().StringSetAsync(
                BuildCacheKey(checkpoint.SessionId),
                serializedCheckpoint,
                CacheTtl);
        }
        catch (Exception ex)
        {
            // Redis 写入失败时不回滚 PostgreSQL，避免缓存问题影响持久化链路。
            logger.LogWarning(ex, "Failed to cache checkpoint for session {SessionId}.", checkpoint.SessionId);
        }
    }

    private static WorkflowCheckpoint NormalizeCheckpoint(WorkflowCheckpoint checkpoint)
    {
        var createdAt = checkpoint.CreatedAt == default ? DateTimeOffset.UtcNow : checkpoint.CreatedAt;
        var updatedAt = checkpoint.UpdatedAt == default ? DateTimeOffset.UtcNow : checkpoint.UpdatedAt;

        return checkpoint with
        {
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            LastCheckpointAt = checkpoint.LastCheckpointAt ?? updatedAt
        };
    }

    private static DateTimeOffset? ResolveCompletedAt(WorkflowCheckpoint checkpoint)
    {
        return checkpoint.Status switch
        {
            WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled => checkpoint.UpdatedAt,
            _ => null
        };
    }

    private static string BuildCacheKey(Guid sessionId)
    {
        return $"checkpoint:{sessionId:N}";
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
