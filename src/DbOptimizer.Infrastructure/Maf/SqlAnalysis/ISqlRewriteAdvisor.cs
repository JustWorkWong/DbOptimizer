using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis;

/* =========================
 * SQL Rewrite Advisor 接口
 * 职责：
 * 1) 基于 ParsedSql 和 ExecutionPlan 生成 SQL 重写建议
 * 2) 返回结构化的 SqlRewriteSuggestion 列表
 * ========================= */
public interface ISqlRewriteAdvisor
{
    Task<IReadOnlyList<SqlRewriteSuggestion>> GenerateAsync(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        CancellationToken cancellationToken = default);
}

/* =========================
 * SQL Rewrite Suggestion 模型
 * ========================= */
public sealed class SqlRewriteSuggestion
{
    public string Category { get; set; } = string.Empty;
    public string OriginalFragment { get; set; } = string.Empty;
    public string SuggestedFragment { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public double EstimatedBenefit { get; set; }
    public List<string> EvidenceRefs { get; set; } = new();
    public double Confidence { get; set; }
}
