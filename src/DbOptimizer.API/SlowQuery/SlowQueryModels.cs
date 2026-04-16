using DbOptimizer.API.Persistence;

namespace DbOptimizer.API.SlowQuery;

/* =========================
 * 慢查询数据模型
 * 职责：
 * 1) RawSlowQuery: MCP 采集的原始慢查询数据
 * 2) NormalizedSlowQuery: 清洗后的规范化数据（含指纹、哈希、表名）
 * ========================= */

/// <summary>
/// 原始慢查询数据（从 MCP 采集）
/// </summary>
internal sealed record RawSlowQuery
{
    public required string SqlText { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public string? UserName { get; init; }
    public string? DatabaseName { get; init; }
    public long? RowsExamined { get; init; }
    public long? RowsSent { get; init; }
}

/// <summary>
/// 规范化慢查询数据（清洗后）
/// </summary>
internal sealed record NormalizedSlowQuery
{
    public required string SqlFingerprint { get; init; }
    public required string QueryHash { get; init; }
    public required string OriginalSql { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required string DatabaseId { get; init; }
    public required string DatabaseType { get; init; }
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
    public string QueryType { get; init; } = string.Empty;
    public long RowsExamined { get; init; }
    public long RowsSent { get; init; }
}
