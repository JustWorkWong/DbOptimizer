namespace DbOptimizer.API.Workflows;

/* =========================
 * IndexAdvisorExecutor
 * 职责：
 * 1) 基于 ParsedSql + ExecutionPlan 聚合候选列
 * 2) 读取目标表已有索引，避免输出明显重复的建议
 * 3) 生成可解释、可落地的 IndexRecommendation 列表并写回上下文
 * ========================= */
internal sealed class IndexAdvisorExecutor(
    ITableIndexMetadataProvider tableIndexMetadataProvider,
    ITableIndexMetadataAnalyzer tableIndexMetadataAnalyzer,
    IIndexRecommendationGenerator indexRecommendationGenerator,
    ILogger<IndexAdvisorExecutor> logger) : IWorkflowExecutor
{
    public string Name => "IndexAdvisorExecutor";

    public async Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var parsedSql, out var executionPlan))
        {
            return WorkflowExecutorResult.Failure("IndexAdvisorExecutor 缺少 ParsedSql 或 ExecutionPlan 上下文。");
        }

        var databaseEngine = ResolveDatabaseEngine(context, parsedSql, executionPlan);
        if (databaseEngine == DatabaseOptimizationEngine.Unknown)
        {
            return WorkflowExecutorResult.Failure("IndexAdvisorExecutor 无法识别数据库类型。");
        }

        var tableIndexes = new Dictionary<string, TableIndexMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in parsedSql.Tables
                     .Select(item => item.TableName)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var invocationResult = await tableIndexMetadataProvider.GetIndexesAsync(databaseEngine, table, cancellationToken);
            tableIndexes[table] = tableIndexMetadataAnalyzer.Analyze(table, invocationResult);
        }

        context.Set(WorkflowContextKeys.TableIndexMetadata, tableIndexes);

        var recommendations = indexRecommendationGenerator.Generate(
            databaseEngine,
            parsedSql,
            executionPlan,
            tableIndexes);

        context.Set(WorkflowContextKeys.IndexRecommendations, recommendations);

        logger.LogInformation(
            "Index advisor executor completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}, DatabaseEngine={DatabaseEngine}",
            context.SessionId,
            recommendations.Count,
            databaseEngine);

        return WorkflowExecutorResult.Success(recommendations);
    }

    private static bool TryResolveInput(
        WorkflowContext context,
        out ParsedSqlResult parsedSql,
        out ExecutionPlanResult executionPlan)
    {
        parsedSql = new ParsedSqlResult();
        executionPlan = new ExecutionPlanResult();

        return context.TryGet<ParsedSqlResult>(WorkflowContextKeys.ParsedSql, out var parsed) &&
               parsed is not null &&
               context.TryGet<ExecutionPlanResult>(WorkflowContextKeys.ExecutionPlan, out var plan) &&
               plan is not null &&
               Assign(parsed, plan, out parsedSql, out executionPlan);
    }

    private static bool Assign(
        ParsedSqlResult parsed,
        ExecutionPlanResult plan,
        out ParsedSqlResult parsedSql,
        out ExecutionPlanResult executionPlan)
    {
        parsedSql = parsed;
        executionPlan = plan;
        return true;
    }

    private static DatabaseOptimizationEngine ResolveDatabaseEngine(
        WorkflowContext context,
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan)
    {
        var values = new List<string?>();

        if (context.TryGet<string>(WorkflowContextKeys.DatabaseDialect, out var databaseDialect))
        {
            values.Add(databaseDialect);
        }

        if (context.TryGet<string>(WorkflowContextKeys.DatabaseType, out var databaseType))
        {
            values.Add(databaseType);
        }

        values.Add(parsedSql.Dialect);
        values.Add(executionPlan.DatabaseEngine);

        foreach (var value in values.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (value!.Contains("mysql", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseOptimizationEngine.MySql;
            }

            if (value.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseOptimizationEngine.PostgreSql;
            }
        }

        return DatabaseOptimizationEngine.Unknown;
    }
}
