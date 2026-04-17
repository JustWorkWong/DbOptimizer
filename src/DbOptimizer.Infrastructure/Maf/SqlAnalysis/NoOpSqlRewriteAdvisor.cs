using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis;

/* =========================
 * 空实现的 SQL Rewrite Advisor
 * 用于第一版占位，后续可替换为真实的 LLM 或规则引擎实现
 * ========================= */
public sealed class NoOpSqlRewriteAdvisor(ILogger<NoOpSqlRewriteAdvisor> logger) : ISqlRewriteAdvisor
{
    public Task<IReadOnlyList<SqlRewriteSuggestion>> GenerateAsync(
        DbOptimizer.Infrastructure.Workflows.ParsedSqlResult parsedSql,
        DbOptimizer.Infrastructure.Workflows.ExecutionPlanResult executionPlan,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("NoOpSqlRewriteAdvisor: SQL rewrite generation skipped (no-op implementation)");
        return Task.FromResult<IReadOnlyList<SqlRewriteSuggestion>>(Array.Empty<SqlRewriteSuggestion>());
    }
}
