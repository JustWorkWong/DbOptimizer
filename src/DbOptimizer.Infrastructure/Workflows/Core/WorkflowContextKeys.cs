using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * Workflow 上下文键名约定
 * 统一常用键名，避免不同 Executor 间出现大小写和命名漂移。
 * ========================= */
public static class WorkflowContextKeys
{
    public const string DatabaseId = "DatabaseId";
    public const string WorkflowTimeline = "WorkflowTimeline";
    public const string WorkflowTimelineNextSequence = "WorkflowTimelineNextSequence";
    public const string SqlText = "SqlText";
    public const string Sql = "Sql";
    public const string SqlParserInput = "SqlParserInput";
    public const string ParsedSql = "ParsedSql";
    public const string ExecutionPlan = "ExecutionPlan";
    public const string TableIndexMetadata = "TableIndexMetadata";
    public const string IndexRecommendations = "IndexRecommendations";
    public const string FinalResult = "FinalResult";
    public const string ReviewId = "ReviewId";
    public const string ReviewStatus = "ReviewStatus";
    public const string RejectionReason = "RejectionReason";
    public const string RegenerationCount = "RegenerationCount";
    public const string DatabaseDialect = "DatabaseDialect";
    public const string DatabaseType = "DatabaseType";
    public const string DbType = "DbType";
    public const string ConfigSnapshot = "ConfigSnapshot";
    public const string ConfigRecommendations = "ConfigRecommendations";
    public const string ConfigOptimizationReport = "ConfigOptimizationReport";
}
