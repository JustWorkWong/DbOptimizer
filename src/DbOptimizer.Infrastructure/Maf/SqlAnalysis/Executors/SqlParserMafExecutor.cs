using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Workflows;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * SQL 解析 Executor
 * 职责：
 * 1) 调用 ISqlParser 解析 SQL
 * 2) 转换为 ParsedSqlContract
 * 3) 输出 SqlParsingCompletedMessage
 * ========================= */
public sealed class SqlParserMafExecutor(
    ISqlParser sqlParser,
    ILogger<SqlParserMafExecutor> logger)
    : Executor<SqlAnalysisWorkflowCommand, SqlParsingCompletedMessage>("SqlParserMafExecutor")
{
    public override ValueTask<SqlParsingCompletedMessage> HandleAsync(
        SqlAnalysisWorkflowCommand message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var parsedSql = sqlParser.Parse(message.SqlText, message.DatabaseEngine);

        var contract = new ParsedSqlContract(
            QueryType: parsedSql.QueryType,
            Dialect: parsedSql.Dialect,
            IsPartial: parsedSql.IsPartial,
            Confidence: parsedSql.Confidence,
            Tables: parsedSql.Tables.Select(t => t.TableName).ToList(),
            Columns: parsedSql.Columns.Select(c => c.ColumnName).ToList(),
            Warnings: parsedSql.Warnings);

        logger.LogInformation(
            "SQL parsing completed. SessionId={SessionId}, QueryType={QueryType}, TableCount={TableCount}, Confidence={Confidence}",
            message.SessionId,
            contract.QueryType,
            contract.Tables.Count,
            contract.Confidence);

        return ValueTask.FromResult(new SqlParsingCompletedMessage(message.SessionId, message, contract));
    }
}
