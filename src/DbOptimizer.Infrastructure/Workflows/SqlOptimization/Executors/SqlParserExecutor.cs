using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * SqlParserExecutor
 * 职责：
 * 1) 从 WorkflowContext 读取 SQL 文本
 * 2) 调用轻量解析器提取表/字段/JOIN/WHERE 等关键信息
 * 3) 将结构化结果写回 ParsedSql，供后续执行器继续使用
 * ========================= */
internal sealed class SqlParserExecutor(
    ISqlParser sqlParser,
    ILogger<SqlParserExecutor> logger) : IWorkflowExecutor
{
    public string Name => "SqlParserExecutor";

    public Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var sqlText, out var dialect))
        {
            return Task.FromResult(WorkflowExecutorResult.Failure("WorkflowContext 中缺少可解析的 SQL 文本。"));
        }

        var parsedSql = sqlParser.Parse(sqlText, dialect);
        context.Set(WorkflowContextKeys.ParsedSql, parsedSql);

        logger.LogInformation(
            "SQL parser executor completed. SessionId={SessionId}, QueryType={QueryType}, TableCount={TableCount}, JoinCount={JoinCount}, PredicateCount={PredicateCount}, WarningCount={WarningCount}",
            context.SessionId,
            parsedSql.QueryType,
            parsedSql.Tables.Count,
            parsedSql.Joins.Count,
            parsedSql.WhereConditions.Count,
            parsedSql.Warnings.Count);

        return Task.FromResult(WorkflowExecutorResult.Success(parsedSql));
    }

    private static bool TryResolveInput(WorkflowContext context, out string sqlText, out string? dialect)
    {
        if (context.TryGet<SqlParserInput>(WorkflowContextKeys.SqlParserInput, out var parserInput) &&
            parserInput is not null &&
            !string.IsNullOrWhiteSpace(parserInput.SqlText))
        {
            sqlText = parserInput.SqlText;
            dialect = parserInput.DatabaseDialect;
            return true;
        }

        if (context.TryGet<string>(WorkflowContextKeys.SqlText, out var directSqlText) &&
            !string.IsNullOrWhiteSpace(directSqlText))
        {
            sqlText = directSqlText;
            dialect = ResolveDialect(context);
            return true;
        }

        if (context.TryGet<string>(WorkflowContextKeys.Sql, out var fallbackSql) &&
            !string.IsNullOrWhiteSpace(fallbackSql))
        {
            sqlText = fallbackSql;
            dialect = ResolveDialect(context);
            return true;
        }

        sqlText = string.Empty;
        dialect = null;
        return false;
    }

    private static string? ResolveDialect(WorkflowContext context)
    {
        if (context.TryGet<string>(WorkflowContextKeys.DatabaseDialect, out var databaseDialect) &&
            !string.IsNullOrWhiteSpace(databaseDialect))
        {
            return databaseDialect;
        }

        if (context.TryGet<string>(WorkflowContextKeys.DatabaseType, out var databaseType) &&
            !string.IsNullOrWhiteSpace(databaseType))
        {
            return databaseType;
        }

        if (context.TryGet<string>(WorkflowContextKeys.DbType, out var dbType) &&
            !string.IsNullOrWhiteSpace(dbType))
        {
            return dbType;
        }

        return null;
    }
}
