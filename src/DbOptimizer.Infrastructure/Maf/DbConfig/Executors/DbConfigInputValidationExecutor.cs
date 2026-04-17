using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

/* =========================
 * DbConfigInputValidationExecutor
 * 职责：验证输入参数
 * ========================= */
public sealed class DbConfigInputValidationExecutor(
    ILogger<DbConfigInputValidationExecutor> logger)
    : Executor<DbConfigWorkflowCommand, DbConfigWorkflowCommand>("DbConfigInputValidationExecutor")
{
    public override ValueTask<DbConfigWorkflowCommand> HandleAsync(
        DbConfigWorkflowCommand message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "验证 DB Config 输入。SessionId={SessionId}, DatabaseId={DatabaseId}, DatabaseType={DatabaseType}",
            message.SessionId,
            message.DatabaseId,
            message.DatabaseType);

        if (string.IsNullOrWhiteSpace(message.DatabaseId))
        {
            throw new InvalidOperationException("DatabaseId 不能为空");
        }

        if (string.IsNullOrWhiteSpace(message.DatabaseType))
        {
            throw new InvalidOperationException("DatabaseType 不能为空");
        }

        var normalizedType = message.DatabaseType.ToLowerInvariant();
        if (normalizedType != "mysql" && normalizedType != "postgresql")
        {
            throw new InvalidOperationException($"不支持的数据库类型: {message.DatabaseType}");
        }

        logger.LogInformation("DB Config 输入验证通过。SessionId={SessionId}", message.SessionId);

        return ValueTask.FromResult(message);
    }
}
