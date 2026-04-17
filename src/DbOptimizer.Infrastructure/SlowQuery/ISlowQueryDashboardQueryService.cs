namespace DbOptimizer.Infrastructure.SlowQuery;

/* =========================
 * 慢查询 Dashboard 查询服务接口
 * 职责：
 * 1) 提供慢查询趋势数据
 * 2) 提供慢查询告警列表
 * 3) 提供慢查询列表和详情
 * ========================= */

public interface ISlowQueryDashboardQueryService
{
    Task<SlowQueryTrendResponse> GetTrendAsync(
        string databaseId,
        int days,
        CancellationToken cancellationToken = default);

    Task<SlowQueryAlertListResponse> GetAlertsAsync(
        string? databaseId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SlowQueryListResponse> GetSlowQueriesAsync(
        string? databaseId,
        string? queryHash,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SlowQueryDetailResponse?> GetSlowQueryAsync(
        Guid queryId,
        CancellationToken cancellationToken = default);
}

public sealed record SlowQueryTrendResponse(
    string DatabaseId,
    int Days,
    IReadOnlyList<SlowQueryTrendPoint> Points);

public sealed record SlowQueryTrendPoint(
    string Date,
    int SlowQueryCount,
    double AvgExecutionTimeMs,
    int AnalysisTriggeredCount);

public sealed record SlowQueryAlertListResponse(
    IReadOnlyList<SlowQueryAlertItem> Items);

public sealed record SlowQueryAlertItem(
    Guid AlertId,
    string DatabaseId,
    string Severity,
    Guid QueryId,
    string Title,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record SlowQueryListResponse(
    IReadOnlyList<SlowQueryListItem> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record SlowQueryListItem(
    Guid QueryId,
    string DatabaseId,
    string DatabaseType,
    string QueryHash,
    string SqlFingerprint,
    double AvgExecutionTimeMs,
    int ExecutionCount,
    DateTimeOffset LastSeenAt,
    Guid? LatestAnalysisSessionId);

public sealed record SlowQueryDetailResponse(
    Guid QueryId,
    string DatabaseId,
    string DatabaseType,
    string QueryHash,
    string SqlFingerprint,
    string OriginalSql,
    string QueryType,
    IReadOnlyList<string> Tables,
    double AvgExecutionTimeMs,
    double MaxExecutionTimeMs,
    int ExecutionCount,
    long TotalRowsExamined,
    long TotalRowsSent,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    Guid? LatestAnalysisSessionId,
    LatestAnalysisInfo? LatestAnalysis);

public sealed record LatestAnalysisInfo(
    Guid SessionId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultType);
