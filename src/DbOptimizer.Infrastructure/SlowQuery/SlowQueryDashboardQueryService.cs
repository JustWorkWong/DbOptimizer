using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.SlowQuery;

/* =========================
 * 慢查询 Dashboard 查询服务实现
 * 职责：
 * 1) 从数据库查询慢查询趋势数据
 * 2) 从数据库查询慢查询告警
 * 3) 从数据库查询慢查询列表和详情
 * ========================= */

public sealed class SlowQueryDashboardQueryService(DbOptimizerDbContext dbContext) : ISlowQueryDashboardQueryService
{
    public async Task<SlowQueryTrendResponse> GetTrendAsync(
        string databaseId,
        int days,
        CancellationToken cancellationToken = default)
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-days).Date;
        var endDate = DateTimeOffset.UtcNow.Date;

        // 按天聚合慢查询数据
        var trendData = await dbContext.SlowQueries
            .Where(q => q.DatabaseId == databaseId && q.LastSeenAt >= startDate)
            .GroupBy(q => q.LastSeenAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                SlowQueryCount = g.Count(),
                AvgExecutionTimeMs = g.Average(q => q.AvgExecutionTime.TotalMilliseconds)
            })
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        // 查询每天触发的分析数量
        var analysisData = await dbContext.WorkflowSessions
            .Where(s => s.SourceType == "slow-query" && s.CreatedAt >= startDate)
            .Join(
                dbContext.SlowQueries.Where(q => q.DatabaseId == databaseId),
                s => s.SourceRefId,
                q => q.QueryId,
                (s, q) => new { s.CreatedAt })
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count()
            })
            .ToListAsync(cancellationToken);

        var analysisDict = analysisData.ToDictionary(x => x.Date, x => x.Count);

        var points = trendData.Select(t => new SlowQueryTrendPoint(
            t.Date.ToString("yyyy-MM-dd"),
            t.SlowQueryCount,
            Math.Round(t.AvgExecutionTimeMs, 2),
            analysisDict.GetValueOrDefault(t.Date, 0)
        )).ToList();

        return new SlowQueryTrendResponse(databaseId, days, points);
    }

    public async Task<SlowQueryAlertListResponse> GetAlertsAsync(
        string? databaseId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        // 简化实现：基于慢查询频率生成告警
        // 规则：1小时内同一查询指纹出现 >= 10 次视为告警
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);

        var query = dbContext.SlowQueries.AsQueryable();

        if (!string.IsNullOrEmpty(databaseId))
        {
            query = query.Where(q => q.DatabaseId == databaseId);
        }

        // 查找高频慢查询
        var alerts = await query
            .Where(q => q.LastSeenAt >= oneHourAgo && q.ExecutionCount >= 10)
            .OrderByDescending(q => q.ExecutionCount)
            .Take(50)
            .Select(q => new SlowQueryAlertItem(
                Guid.NewGuid(), // 临时生成 AlertId
                q.DatabaseId,
                q.ExecutionCount >= 20 ? "high" : "medium",
                q.QueryId,
                $"同一 SQL 指纹在 1 小时内出现 {q.ExecutionCount} 次慢查询",
                "open",
                q.LastSeenAt
            ))
            .ToListAsync(cancellationToken);

        return new SlowQueryAlertListResponse(alerts);
    }

    public async Task<SlowQueryListResponse> GetSlowQueriesAsync(
        string? databaseId,
        string? queryHash,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.SlowQueries.AsQueryable();

        if (!string.IsNullOrEmpty(databaseId))
        {
            query = query.Where(q => q.DatabaseId == databaseId);
        }

        if (!string.IsNullOrEmpty(queryHash))
        {
            query = query.Where(q => q.QueryHash == queryHash);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(q => q.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new SlowQueryListItem(
                q.QueryId,
                q.DatabaseId,
                q.DatabaseType,
                q.QueryHash,
                q.SqlFingerprint,
                Math.Round(q.AvgExecutionTime.TotalMilliseconds, 2),
                q.ExecutionCount,
                q.LastSeenAt,
                q.LatestAnalysisSessionId
            ))
            .ToListAsync(cancellationToken);

        return new SlowQueryListResponse(items, total, page, pageSize);
    }

    public async Task<SlowQueryDetailResponse?> GetSlowQueryAsync(
        Guid queryId,
        CancellationToken cancellationToken = default)
    {
        var slowQuery = await dbContext.SlowQueries
            .Where(q => q.QueryId == queryId)
            .FirstOrDefaultAsync(cancellationToken);

        if (slowQuery is null)
        {
            return null;
        }

        LatestAnalysisInfo? latestAnalysis = null;
        if (slowQuery.LatestAnalysisSessionId.HasValue)
        {
            var session = await dbContext.WorkflowSessions
                .Where(s => s.SessionId == slowQuery.LatestAnalysisSessionId.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (session is not null)
            {
                latestAnalysis = new LatestAnalysisInfo(
                    session.SessionId,
                    session.Status,
                    session.CreatedAt,
                    session.CompletedAt,
                    session.ResultType
                );
            }
        }

        var tables = string.IsNullOrEmpty(slowQuery.Tables)
            ? Array.Empty<string>()
            : slowQuery.Tables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new SlowQueryDetailResponse(
            slowQuery.QueryId,
            slowQuery.DatabaseId,
            slowQuery.DatabaseType,
            slowQuery.QueryHash,
            slowQuery.SqlFingerprint,
            slowQuery.OriginalSql,
            slowQuery.QueryType,
            tables,
            Math.Round(slowQuery.AvgExecutionTime.TotalMilliseconds, 2),
            Math.Round(slowQuery.MaxExecutionTime.TotalMilliseconds, 2),
            slowQuery.ExecutionCount,
            slowQuery.TotalRowsExamined,
            slowQuery.TotalRowsSent,
            slowQuery.FirstSeenAt,
            slowQuery.LastSeenAt,
            slowQuery.LatestAnalysisSessionId,
            latestAnalysis
        );
    }
}
