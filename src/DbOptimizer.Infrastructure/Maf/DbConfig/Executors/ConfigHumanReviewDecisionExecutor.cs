using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

public sealed class ConfigHumanReviewDecisionExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    IConfigReviewAdjustmentService adjustmentService,
    ILogger<ConfigHumanReviewDecisionExecutor> logger)
    : Executor<ConfigReviewDecisionResponseMessage, DbConfigOptimizationCompletedMessage>("ConfigHumanReviewDecisionExecutor")
{
    public override async ValueTask<DbConfigOptimizationCompletedMessage> HandleAsync(
        ConfigReviewDecisionResponseMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var correlation = await reviewTaskGateway.GetCorrelationAsync(message.TaskId, cancellationToken);
        if (correlation is null)
        {
            logger.LogError("Config review task not found. TaskId={TaskId}", message.TaskId);
            throw new InvalidOperationException($"Config review task {message.TaskId} not found");
        }

        if (message.Action == "reject")
        {
            logger.LogWarning(
                "Config review rejected. SessionId={SessionId}, TaskId={TaskId}, Comment={Comment}",
                message.SessionId,
                message.TaskId,
                message.Comment);

            throw new WorkflowFailedException($"Config review rejected: {message.Comment}");
        }

        var finalResult = message.Action == "adjust"
            ? adjustmentService.ApplyAdjustments(correlation.Payload, message.Adjustments)
            : correlation.Payload;

        logger.LogInformation(
            "Config review approved. SessionId={SessionId}, TaskId={TaskId}, Action={Action}",
            message.SessionId,
            message.TaskId,
            message.Action);

        return new DbConfigOptimizationCompletedMessage(message.SessionId, finalResult);
    }
}
