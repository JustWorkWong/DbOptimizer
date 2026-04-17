using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * 执行计划获取 Executor
 * 职责：
 * 1) 调用 ExecutionPlanProvider + Analyzer 获取执行计划
 * 2) 转换为 ExecutionPlanContract
 * 3) 输出 ExecutionPlanCompletedMessage
 * ========================= */
public sealed class ExecutionPlanMafExecutor(
    IExecutionPlanProvider executionPlanProvider,
    IExecutionPlanAnalyzer executionPlanAnalyzer,
    ILogger<ExecutionPlanMafExecutor> logger)
    : Executor<SqlParsingCompletedMessage, ExecutionPlanCompletedMessage>("ExecutionPlanMafExecutor")
{
    public override async ValueTask<ExecutionPlanCompletedMessage> HandleAsync(
        SqlParsingCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = message.Command;
        var databaseEngine = ParseDatabaseEngine(command.DatabaseEngine);

        var invocationResult = await executionPlanProvider.ExplainAsync(
            databaseEngine,
            command.SqlText,
            cancellationToken);

        // 重建 ParsedSqlResult 用于分析
        var parsedSqlResult = new DbOptimizer.Infrastructure.Workflows.ParsedSqlResult
        {
            QueryType = message.ParsedSql.QueryType,
            Dialect = message.ParsedSql.Dialect,
            IsPartial = message.ParsedSql.IsPartial,
            Confidence = message.ParsedSql.Confidence,
            RawSql = command.SqlText
        };

        var executionPlanResult = executionPlanAnalyzer.Analyze(databaseEngine, parsedSqlResult, invocationResult);

        var contract = new ExecutionPlanContract(
            DatabaseEngine: executionPlanResult.DatabaseEngine,
            RawPlan: executionPlanResult.RawPlan,
            UsedFallback: executionPlanResult.UsedFallback,
            Issues: executionPlanResult.Issues.Select(i => new ExecutionPlanIssueContract(
                Type: i.Type,
                Description: i.Description,
                TableName: i.TableName,
                ImpactScore: i.ImpactScore,
                Evidence: i.Evidence)).ToList(),
            Warnings: executionPlanResult.Warnings);

        logger.LogInformation(
            "Execution plan completed. SessionId={SessionId}, IssueCount={IssueCount}, UsedFallback={UsedFallback}",
            message.SessionId,
            contract.Issues.Count,
            contract.UsedFallback);

        return new ExecutionPlanCompletedMessage(message.SessionId, command, message.ParsedSql, contract);
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
