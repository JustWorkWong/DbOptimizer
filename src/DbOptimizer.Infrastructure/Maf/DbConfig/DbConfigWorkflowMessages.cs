using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.DbConfig;

/* =========================
 * DB Config Workflow 消息契约
 * 设计原则：
 * 1) 每个 executor 输出一个明确的 completed message
 * 2) 消息携带累积的上下文数据（包括原始 command）
 * 3) 开关门控在 executor 内部实现，不改变消息流
 * ========================= */

public sealed record DbConfigWorkflowCommand(
    Guid SessionId,
    string DatabaseId,
    string DatabaseType,
    bool AllowFallbackSnapshot,
    bool RequireHumanReview);

public sealed record ConfigSnapshotCollectedMessage(
    Guid SessionId,
    DbConfigWorkflowCommand Command,
    DbConfigSnapshotContract Snapshot);

public sealed record ConfigRecommendationsGeneratedMessage(
    Guid SessionId,
    DbConfigWorkflowCommand Command,
    DbConfigSnapshotContract Snapshot,
    IReadOnlyList<ConfigRecommendationContract> Recommendations);

public sealed record DbConfigOptimizationDraftReadyMessage(
    Guid SessionId,
    DbOptimizer.Core.Models.WorkflowResultEnvelope DraftResult);

public sealed record DbConfigOptimizationCompletedMessage(
    Guid SessionId,
    DbOptimizer.Core.Models.WorkflowResultEnvelope FinalResult);

/* =========================
 * 复用 SQL workflow 的 ReviewDecisionResponseMessage
 * ========================= */
public sealed record ReviewDecisionResponseMessage(
    Guid SessionId,
    Guid TaskId,
    string RequestId,
    string RunId,
    string CheckpointRef,
    string Action,
    string? Comment,
    IReadOnlyDictionary<string, JsonElement> Adjustments,
    DateTimeOffset ReviewedAt);

/* =========================
 * 契约模型（用于消息传递）
 * ========================= */

public sealed record DbConfigSnapshotContract(
    string DatabaseType,
    string DatabaseId,
    IReadOnlyList<ConfigParameterContract> Parameters,
    SystemMetricsContract Metrics,
    DateTimeOffset CollectedAt,
    bool UsedFallback,
    string? FallbackReason);

public sealed record ConfigParameterContract(
    string Name,
    string Value,
    string DefaultValue,
    string Description,
    bool IsDynamic,
    string Type,
    string? MinValue,
    string? MaxValue);

public sealed record SystemMetricsContract(
    int CpuCores,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    long TotalDiskBytes,
    long AvailableDiskBytes,
    string DatabaseVersion,
    long UptimeSeconds,
    int ActiveConnections,
    int MaxConnections);

public sealed record ConfigRecommendationContract(
    string ParameterName,
    string CurrentValue,
    string RecommendedValue,
    string Reasoning,
    double Confidence,
    string Impact,
    bool RequiresRestart,
    IReadOnlyList<string> EvidenceRefs,
    string RuleName);
