using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis;

/* =========================
 * SQL Analysis Workflow 消息契约
 * 设计原则：
 * 1) 每个 executor 输出一个明确的 completed message
 * 2) 消息携带累积的上下文数据（包括原始 command）
 * 3) 开关门控在 executor 内部实现，不改变消息流
 * ========================= */

public sealed record SqlAnalysisWorkflowCommand(
    Guid SessionId,
    string SqlText,
    string DatabaseId,
    string DatabaseEngine,
    string SourceType,
    Guid? SourceRefId,
    bool EnableIndexRecommendation,
    bool EnableSqlRewrite,
    bool RequireHumanReview);

public sealed record SqlParsingCompletedMessage(
    Guid SessionId,
    SqlAnalysisWorkflowCommand Command,
    ParsedSqlContract ParsedSql);

public sealed record ExecutionPlanCompletedMessage(
    Guid SessionId,
    SqlAnalysisWorkflowCommand Command,
    ParsedSqlContract ParsedSql,
    ExecutionPlanContract ExecutionPlan);

public sealed record IndexRecommendationCompletedMessage(
    Guid SessionId,
    SqlAnalysisWorkflowCommand Command,
    ParsedSqlContract ParsedSql,
    ExecutionPlanContract ExecutionPlan,
    IReadOnlyList<IndexRecommendationContract> IndexRecommendations);

public sealed record SqlRewriteCompletedMessage(
    Guid SessionId,
    SqlAnalysisWorkflowCommand Command,
    ParsedSqlContract ParsedSql,
    ExecutionPlanContract ExecutionPlan,
    IReadOnlyList<IndexRecommendationContract> IndexRecommendations,
    IReadOnlyList<SqlRewriteSuggestionContract> SqlRewriteSuggestions);

public sealed record SqlOptimizationDraftReadyMessage(
    Guid SessionId,
    DbOptimizer.Core.Models.WorkflowResultEnvelope DraftResult);

public sealed record SqlReviewRequestMessage(
    Guid SessionId,
    Guid TaskId,
    DbOptimizer.Core.Models.WorkflowResultEnvelope DraftResult);

public sealed record SqlReviewResponseMessage(
    Guid SessionId,
    Guid TaskId,
    string Action,
    string? Comment,
    Dictionary<string, JsonElement> Adjustments,
    DateTimeOffset ReviewedAt);

public sealed record SqlOptimizationCompletedMessage(
    Guid SessionId,
    DbOptimizer.Core.Models.WorkflowResultEnvelope FinalResult);

/* =========================
 * 契约模型（用于消息传递）
 * ========================= */

public sealed record ParsedSqlContract(
    string QueryType,
    string Dialect,
    bool IsPartial,
    double Confidence,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Warnings);

public sealed record ExecutionPlanContract(
    string DatabaseEngine,
    string RawPlan,
    bool UsedFallback,
    IReadOnlyList<ExecutionPlanIssueContract> Issues,
    IReadOnlyList<string> Warnings);

public sealed record ExecutionPlanIssueContract(
    string Type,
    string Description,
    string? TableName,
    double ImpactScore,
    string Evidence);

public sealed record IndexRecommendationContract(
    string TableName,
    IReadOnlyList<string> Columns,
    string IndexType,
    string CreateDdl,
    double EstimatedBenefit,
    string Reasoning,
    IReadOnlyList<string> EvidenceRefs,
    double Confidence);

public sealed record SqlRewriteSuggestionContract(
    string Category,
    string OriginalFragment,
    string SuggestedFragment,
    string Reasoning,
    double EstimatedBenefit,
    IReadOnlyList<string> EvidenceRefs,
    double Confidence);
