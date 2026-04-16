using DbOptimizer.API.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.SlowQuery;

/* =========================
 * 慢查询数据访问层
 * 职责：
 * 1) 保存规范化慢查询到 slow_queries 表
 * 2) 去重逻辑：根据 QueryHash + DatabaseId + 时间窗口（1 小时）
 * 3) 更新 ExecutionCount 和 AvgExecutionTime
 * ========================= */
internal sealed class SlowQueryRepository(IDbContextFactory<DbOptimizerDbContext> dbContextFactory) : ISlowQueryRepository
{
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromHours(1);

    public async Task SaveAsync(NormalizedSlowQuery normalized, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var windowStart = normalized.ExecutedAt.Add(-DeduplicationWindow);
        var existing = await dbContext.SlowQueries
            .Where(q =>
                q.QueryHash == normalized.QueryHash &&
                q.DatabaseId == normalized.DatabaseId &&
                q.LastSeenAt >= windowStart)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            existing.ExecutionCount++;
            existing.TotalRowsExamined += normalized.RowsExamined;
            existing.TotalRowsSent += normalized.RowsSent;
            existing.AvgExecutionTime = TimeSpan.FromMilliseconds(
                (existing.AvgExecutionTime.TotalMilliseconds * (existing.ExecutionCount - 1) +
                 normalized.ExecutionTime.TotalMilliseconds) / existing.ExecutionCount);
            existing.MaxExecutionTime = normalized.ExecutionTime > existing.MaxExecutionTime
                ? normalized.ExecutionTime
                : existing.MaxExecutionTime;
            existing.LastSeenAt = normalized.ExecutedAt;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            var entity = new SlowQueryEntity
            {
                QueryId = Guid.NewGuid(),
                DatabaseId = normalized.DatabaseId,
                DatabaseType = normalized.DatabaseType,
                SqlFingerprint = normalized.SqlFingerprint,
                QueryHash = normalized.QueryHash,
                OriginalSql = normalized.OriginalSql,
                QueryType = normalized.QueryType,
                Tables = string.Join(",", normalized.Tables),
                AvgExecutionTime = normalized.ExecutionTime,
                MaxExecutionTime = normalized.ExecutionTime,
                ExecutionCount = 1,
                TotalRowsExamined = normalized.RowsExamined,
                TotalRowsSent = normalized.RowsSent,
                FirstSeenAt = normalized.ExecutedAt,
                LastSeenAt = normalized.ExecutedAt,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            dbContext.SlowQueries.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SlowQueryEntity>> GetRecentSlowQueriesAsync(
        string databaseId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.SlowQueries
            .Where(q => q.DatabaseId == databaseId)
            .OrderByDescending(q => q.LastSeenAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}

internal interface ISlowQueryRepository
{
    Task SaveAsync(NormalizedSlowQuery normalized, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SlowQueryEntity>> GetRecentSlowQueriesAsync(string databaseId, int limit = 100, CancellationToken cancellationToken = default);
}
