using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

public sealed class ConfigHumanReviewGateExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    ILogger<ConfigHumanReviewGateExecutor> logger)
    : Executor<DbConfigOptimizationDraftReadyMessage>("ConfigHumanReviewGateExecutor")
{
    public override async ValueTask HandleAsync(
        DbConfigOptimizationDraftReadyMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReadRequireHumanReview(message.DraftResult))
        {
            logger.LogInformation(
                "Config human review not required. SessionId={SessionId}",
                message.SessionId);

            await context.YieldOutputAsync(
                new DbConfigOptimizationCompletedMessage(message.SessionId, message.DraftResult),
                cancellationToken);
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var taskId = await reviewTaskGateway.CreateAsync(
            message.SessionId,
            "DbConfigOptimization",
            requestId,
            string.Empty,
            string.Empty,
            message.DraftResult,
            cancellationToken);

        var request = ExternalRequest.Create(
            MafReviewPorts.ConfigReview,
            new ConfigReviewRequestMessage(
                message.SessionId,
                taskId,
                message.DraftResult),
            requestId);

        logger.LogInformation(
            "Config review task created and external request queued. SessionId={SessionId}, TaskId={TaskId}, RequestId={RequestId}",
            message.SessionId,
            taskId,
            requestId);

        await context.SendMessageAsync(request, MafReviewPorts.ConfigReview.Id, cancellationToken);
    }

    private static bool ReadRequireHumanReview(WorkflowResultEnvelope envelope)
    {
        return envelope.Metadata.TryGetProperty("requireHumanReview", out var element) &&
               element.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}

public sealed class WorkflowFailedException(string message) : Exception(message);
