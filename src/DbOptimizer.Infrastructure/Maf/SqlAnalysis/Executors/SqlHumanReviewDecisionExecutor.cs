using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

public sealed class SqlHumanReviewDecisionExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    ISqlReviewAdjustmentService adjustmentService,
    ILogger<SqlHumanReviewDecisionExecutor> logger)
    : Executor<SqlReviewResponseMessage, SqlOptimizationCompletedMessage>("SqlHumanReviewDecisionExecutor")
{
    public override async ValueTask<SqlOptimizationCompletedMessage> HandleAsync(
        SqlReviewResponseMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var correlation = await reviewTaskGateway.GetCorrelationAsync(message.TaskId, cancellationToken);
        if (correlation is null)
        {
            logger.LogError("Review task not found. TaskId={TaskId}", message.TaskId);
            throw new InvalidOperationException($"Review task {message.TaskId} not found");
        }

        if (message.Action == "reject")
        {
            logger.LogWarning(
                "Review rejected. SessionId={SessionId}, TaskId={TaskId}, Comment={Comment}",
                message.SessionId,
                message.TaskId,
                message.Comment);

            throw new WorkflowFailedException($"Review rejected: {message.Comment}");
        }

        var finalResult = message.Action == "adjust"
            ? adjustmentService.ApplyAdjustments(correlation.Payload, message.Adjustments)
            : correlation.Payload;

        logger.LogInformation(
            "Review approved. SessionId={SessionId}, TaskId={TaskId}, Action={Action}",
            message.SessionId,
            message.TaskId,
            message.Action);

        return new SqlOptimizationCompletedMessage(message.SessionId, finalResult);
    }
}
