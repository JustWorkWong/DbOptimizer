using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * SQL Rewrite Executor
 * 职责：
 * 1) 根据 EnableSqlRewrite 开关决定是否生成 SQL 重写建议
 * 2) 调用 ISqlRewriteAdvisor 生成建议
 * 3) 输出 SqlRewriteCompletedMessage
 * ========================= */
public sealed class SqlRewriteMafExecutor(
    ISqlRewriteAdvisor sqlRewriteAdvisor,
    ILogger<SqlRewriteMafExecutor> logger)
    : Executor<IndexRecommendationCompletedMessage, SqlRewriteCompletedMessage>("SqlRewriteMafExecutor")
{
    public override async ValueTask<SqlRewriteCompletedMessage> HandleAsync(
        IndexRecommendationCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = message.Command;
        IReadOnlyList<SqlRewriteSuggestionContract> suggestions;

        if (!command.EnableSqlRewrite)
        {
            logger.LogInformation(
                "SQL rewrite disabled. SessionId={SessionId}",
                message.SessionId);
            suggestions = Array.Empty<SqlRewriteSuggestionContract>();
        }
        else
        {
            // 重建 ParsedSqlResult 和 ExecutionPlanResult
            var parsedSqlResult = new DbOptimizer.Infrastructure.Workflows.ParsedSqlResult
            {
                QueryType = message.ParsedSql.QueryType,
                Dialect = message.ParsedSql.Dialect,
                IsPartial = message.ParsedSql.IsPartial,
                Confidence = message.ParsedSql.Confidence,
                RawSql = command.SqlText
            };

            var executionPlanResult = new DbOptimizer.Infrastructure.Workflows.ExecutionPlanResult
            {
                DatabaseEngine = message.ExecutionPlan.DatabaseEngine,
                RawPlan = message.ExecutionPlan.RawPlan,
                UsedFallback = message.ExecutionPlan.UsedFallback,
                Issues = message.ExecutionPlan.Issues.Select(i => new DbOptimizer.Infrastructure.Workflows.ExecutionPlanIssue
                {
                    Type = i.Type,
                    Description = i.Description,
                    TableName = i.TableName,
                    ImpactScore = i.ImpactScore,
                    Evidence = i.Evidence
                }).ToList()
            };

            var rewriteSuggestions = await sqlRewriteAdvisor.GenerateAsync(
                parsedSqlResult,
                executionPlanResult,
                cancellationToken);

            suggestions = rewriteSuggestions.Select(s => new SqlRewriteSuggestionContract(
                Category: s.Category,
                OriginalFragment: s.OriginalFragment,
                SuggestedFragment: s.SuggestedFragment,
                Reasoning: s.Reasoning,
                EstimatedBenefit: s.EstimatedBenefit,
                EvidenceRefs: s.EvidenceRefs,
                Confidence: s.Confidence)).ToList();

            logger.LogInformation(
                "SQL rewrite completed. SessionId={SessionId}, SuggestionCount={SuggestionCount}",
                message.SessionId,
                suggestions.Count);
        }

        return new SqlRewriteCompletedMessage(
            message.SessionId,
            command,
            message.ParsedSql,
            message.ExecutionPlan,
            message.IndexRecommendations,
            suggestions);
    }
}
