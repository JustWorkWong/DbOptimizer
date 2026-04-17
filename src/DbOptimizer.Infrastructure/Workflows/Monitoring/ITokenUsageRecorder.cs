using System.Text.Json;

namespace DbOptimizer.Infrastructure.Workflows.Monitoring;

/// <summary>
/// Token 使用量记录器
/// 记录每个 Executor 的 Token 消耗
/// </summary>
public interface ITokenUsageRecorder
{
    /// <summary>
    /// 记录 Token 使用量
    /// </summary>
    Task RecordAsync(
        Guid sessionId,
        string executorName,
        JsonElement? tokenUsage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取会话总 Token 使用量
    /// </summary>
    Task<TokenUsageSummary> GetSessionSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Token 使用量汇总
/// </summary>
public sealed record TokenUsageSummary(
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    decimal EstimatedCost);
