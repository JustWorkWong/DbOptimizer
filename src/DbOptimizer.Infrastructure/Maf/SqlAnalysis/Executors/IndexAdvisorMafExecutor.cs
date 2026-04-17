using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * 索引推荐 Executor
 * 职责：
 * 1) 根据 EnableIndexRecommendation 开关决定是否生成索引建议
 * 2) 调用 IIndexRecommendationGenerator 生成建议
 * 3) 输出 IndexRecommendationCompletedMessage
 * ========================= */
public sealed class IndexAdvisorMafExecutor(
    IIndexRecommendationGenerator indexRecommendationGenerator,
    ILogger<IndexAdvisorMafExecutor> logger)
    : Executor<ExecutionPlanCompletedMessage, IndexRecommendationCompletedMessage>("IndexAdvisorMafExecutor")
{
    public override ValueTask<IndexRecommendationCompletedMessage> HandleAsync(
        ExecutionPlanCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = message.Command;
        IReadOnlyList<IndexRecommendationContract> recommendations;

        if (!command.EnableIndexRecommendation)
        {
            logger.LogInformation(
                "Index recommendation disabled. SessionId={SessionId}",
                message.SessionId);
            recommendations = Array.Empty<IndexRecommendationContract>();
        }
        else
        {
            var databaseEngine = ParseDatabaseEngine(command.DatabaseEngine);

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

            var indexRecommendations = indexRecommendationGenerator.Generate(
                databaseEngine,
                parsedSqlResult,
                executionPlanResult,
                new Dictionary<string, DbOptimizer.Infrastructure.Workflows.TableIndexMetadata>());

            recommendations = indexRecommendations.Select(r => new IndexRecommendationContract(
                TableName: r.TableName,
                Columns: r.Columns,
                IndexType: r.IndexType,
                CreateDdl: r.CreateDdl,
                EstimatedBenefit: r.EstimatedBenefit,
                Reasoning: r.Reasoning,
                EvidenceRefs: r.EvidenceRefs,
                Confidence: r.Confidence)).ToList();

            logger.LogInformation(
                "Index recommendation completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}",
                message.SessionId,
                recommendations.Count);
        }

        return ValueTask.FromResult(new IndexRecommendationCompletedMessage(
            message.SessionId,
            command,
            message.ParsedSql,
            message.ExecutionPlan,
            recommendations));
    }

    private static DbOptimizer.Infrastructure.Workflows.DatabaseOptimizationEngine ParseDatabaseEngine(string engineName)
    {
        return engineName.ToLowerInvariant() switch
        {
            "mysql" => DbOptimizer.Infrastructure.Workflows.DatabaseOptimizationEngine.MySql,
            "postgresql" or "postgres" => DbOptimizer.Infrastructure.Workflows.DatabaseOptimizationEngine.PostgreSql,
            _ => DbOptimizer.Infrastructure.Workflows.DatabaseOptimizationEngine.Unknown
        };
    }
}
