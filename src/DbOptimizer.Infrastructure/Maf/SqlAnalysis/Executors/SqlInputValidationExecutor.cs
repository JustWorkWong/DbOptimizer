using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * SQL 输入验证 Executor
 * 职责：
 * 1) 验证 SQL 文本非空
 * 2) 验证 DatabaseId 和 DatabaseEngine 有效
 * 3) 通过验证后直接返回原始 command
 * ========================= */
public sealed class SqlInputValidationExecutor(ILogger<SqlInputValidationExecutor> logger)
    : Executor<SqlAnalysisWorkflowCommand, SqlAnalysisWorkflowCommand>("SqlInputValidationExecutor")
{
    public override ValueTask<SqlAnalysisWorkflowCommand> HandleAsync(
        SqlAnalysisWorkflowCommand message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message.SqlText))
        {
            logger.LogError("SQL text is empty. SessionId={SessionId}", message.SessionId);
            throw new InvalidOperationException("SQL text cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(message.DatabaseId))
        {
            logger.LogError("DatabaseId is empty. SessionId={SessionId}", message.SessionId);
            throw new InvalidOperationException("DatabaseId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(message.DatabaseEngine))
        {
            logger.LogError("DatabaseEngine is empty. SessionId={SessionId}", message.SessionId);
            throw new InvalidOperationException("DatabaseEngine cannot be empty.");
        }

        logger.LogInformation(
            "SQL input validation passed. SessionId={SessionId}, DatabaseEngine={DatabaseEngine}, SqlLength={SqlLength}",
            message.SessionId,
            message.DatabaseEngine,
            message.SqlText.Length);

        return ValueTask.FromResult(message);
    }
}
