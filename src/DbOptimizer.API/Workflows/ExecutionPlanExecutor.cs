namespace DbOptimizer.API.Workflows;

/* =========================
 * ExecutionPlanExecutor
 * 职责：
 * 1) 从上下文读取 SQL 与 ParsedSql
 * 2) 通过 MCP explain 获取执行计划，失败时按配置直连降级
 * 3) 抽取最小可用的性能问题和指标，写回 ExecutionPlan
 * ========================= */
internal sealed class ExecutionPlanExecutor(
    IExecutionPlanProvider executionPlanProvider,
    IExecutionPlanAnalyzer executionPlanAnalyzer,
    ILogger<ExecutionPlanExecutor> logger) : IWorkflowExecutor
{
    public string Name => "ExecutionPlanExecutor";

    public async Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var sqlText, out var parsedSql))
        {
            return WorkflowExecutorResult.Failure("ExecutionPlanExecutor 缺少 SQL 或 ParsedSql 上下文。");
        }

        var databaseEngine = ResolveDatabaseEngine(context, parsedSql);
        if (databaseEngine == DatabaseOptimizationEngine.Unknown)
        {
            return WorkflowExecutorResult.Failure("ExecutionPlanExecutor 无法识别数据库类型。");
        }

        var invocationResult = await executionPlanProvider.ExplainAsync(
            databaseEngine,
            sqlText,
            cancellationToken);

        var executionPlan = executionPlanAnalyzer.Analyze(databaseEngine, parsedSql, invocationResult);
        context.Set(WorkflowContextKeys.ExecutionPlan, executionPlan);

        logger.LogInformation(
            "Execution plan executor completed. SessionId={SessionId}, DatabaseEngine={DatabaseEngine}, IssueCount={IssueCount}, WarningCount={WarningCount}, UsedFallback={UsedFallback}",
            context.SessionId,
            databaseEngine,
            executionPlan.Issues.Count,
            executionPlan.Warnings.Count,
            executionPlan.UsedFallback);

        return WorkflowExecutorResult.Success(executionPlan);
    }

    private static bool TryResolveInput(WorkflowContext context, out string sqlText, out ParsedSqlResult parsedSql)
    {
        parsedSql = new ParsedSqlResult();
        if (!context.TryGet<ParsedSqlResult>(WorkflowContextKeys.ParsedSql, out var parsed) || parsed is null)
        {
            sqlText = string.Empty;
            return false;
        }

        parsedSql = parsed;
        if (!string.IsNullOrWhiteSpace(parsedSql.RawSql))
        {
            sqlText = parsedSql.RawSql;
            return true;
        }

        if (context.TryGet<string>(WorkflowContextKeys.SqlText, out var directSqlText) &&
            !string.IsNullOrWhiteSpace(directSqlText))
        {
            sqlText = directSqlText;
            return true;
        }

        if (context.TryGet<string>(WorkflowContextKeys.Sql, out var fallbackSql) &&
            !string.IsNullOrWhiteSpace(fallbackSql))
        {
            sqlText = fallbackSql;
            return true;
        }

        sqlText = string.Empty;
        return false;
    }

    private static DatabaseOptimizationEngine ResolveDatabaseEngine(WorkflowContext context, ParsedSqlResult parsedSql)
    {
        var candidateValues = new List<string?>();

        if (context.TryGet<string>(WorkflowContextKeys.DatabaseDialect, out var databaseDialect))
        {
            candidateValues.Add(databaseDialect);
        }

        if (context.TryGet<string>(WorkflowContextKeys.DatabaseType, out var databaseType))
        {
            candidateValues.Add(databaseType);
        }

        if (context.TryGet<string>(WorkflowContextKeys.DbType, out var dbType))
        {
            candidateValues.Add(dbType);
        }

        candidateValues.Add(parsedSql.Dialect);

        foreach (var candidate in candidateValues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (candidate!.Contains("mysql", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseOptimizationEngine.MySql;
            }

            if (candidate.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseOptimizationEngine.PostgreSql;
            }
        }

        return DatabaseOptimizationEngine.Unknown;
    }
}
