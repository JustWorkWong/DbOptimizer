using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

public sealed class SqlHumanReviewGateExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    ILogger<SqlHumanReviewGateExecutor> logger)
    : Executor<SqlOptimizationDraftReadyMessage>("SqlHumanReviewGateExecutor")
{
    public override async ValueTask HandleAsync(
        SqlOptimizationDraftReadyMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ReadRequireHumanReview(message.DraftResult))
        {
            logger.LogInformation(
                "Human review not required. SessionId={SessionId}",
                message.SessionId);

            await context.YieldOutputAsync(
                new SqlOptimizationCompletedMessage(message.SessionId, message.DraftResult),
                cancellationToken);
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var taskId = await reviewTaskGateway.CreateAsync(
            message.SessionId,
            "SqlOptimization",
            requestId,
            string.Empty,
            string.Empty,
            message.DraftResult,
            cancellationToken);

        var request = ExternalRequest.Create(
            MafReviewPorts.SqlReview,
            new SqlReviewRequestMessage(
                message.SessionId,
                taskId,
                message.DraftResult),
            requestId);

        logger.LogInformation(
            "Review task created and external request queued. SessionId={SessionId}, TaskId={TaskId}, RequestId={RequestId}",
            message.SessionId,
            taskId,
            requestId);

        await context.SendMessageAsync(request, MafReviewPorts.SqlReview.Id, cancellationToken);
    }

    private static bool ReadRequireHumanReview(WorkflowResultEnvelope envelope)
    {
        return envelope.Metadata.TryGetProperty("requireHumanReview", out var element) &&
               element.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}

public sealed class WorkflowFailedException(string message) : Exception(message);
