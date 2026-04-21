using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

#pragma warning disable CS0618
[SendsMessage(typeof(SqlReviewRequestMessage))]
public sealed class SqlHumanReviewGateExecutor(
    IMafExecutorInstrumentation instrumentation,
    IWorkflowReviewTaskGateway reviewTaskGateway,
    ISqlReviewAdjustmentService adjustmentService,
    ILogger<SqlHumanReviewGateExecutor> logger)
    : ReflectingExecutor<SqlHumanReviewGateExecutor>("SqlHumanReviewGateExecutor"),
        IMessageHandler<SqlOptimizationDraftReadyMessage>,
        IMessageHandler<SqlReviewResponseMessage, SqlOptimizationCompletedMessage>
{
    public async ValueTask HandleAsync(
        SqlOptimizationDraftReadyMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executionId = await instrumentation.OnStartedAsync(
            "sql_analysis",
            Id,
            message,
            startedAt,
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ReadRequireHumanReview(message.DraftResult))
            {
                logger.LogInformation(
                    "Human review not required. SessionId={SessionId}",
                    message.SessionId);

                var completed = new SqlOptimizationCompletedMessage(message.SessionId, message.DraftResult);
                await instrumentation.OnCompletedAsync(
                    "sql_analysis",
                    Id,
                    message,
                    completed,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    executionId: executionId,
                    cancellationToken: cancellationToken);

                await context.YieldOutputAsync(completed, cancellationToken);
                return;
            }

            var taskId = await reviewTaskGateway.CreateAsync(
                message.SessionId,
                "SqlOptimization",
                string.Empty,
                string.Empty,
                string.Empty,
                message.DraftResult,
                cancellationToken);

            logger.LogInformation(
                "Review task created and review request queued. SessionId={SessionId}, TaskId={TaskId}",
                message.SessionId,
                taskId);

            await context.SendMessageAsync(
                new SqlReviewRequestMessage(
                    message.SessionId,
                    taskId,
                    message.DraftResult),
                cancellationToken);
            await instrumentation.OnCompletedAsync(
                "sql_analysis",
                Id,
                message,
                new
                {
                    taskId,
                    waitingForReview = true
                },
                startedAt,
                DateTimeOffset.UtcNow,
                waitingForReview: true,
                executionId: executionId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await instrumentation.OnFailedAsync(
                "sql_analysis",
                Id,
                message,
                ex,
                startedAt,
                DateTimeOffset.UtcNow,
                executionId,
                cancellationToken);
            throw;
        }
    }

    public async ValueTask<SqlOptimizationCompletedMessage> HandleAsync(
        SqlReviewResponseMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executionId = await instrumentation.OnStartedAsync(
            "sql_analysis",
            Id,
            message,
            startedAt,
            cancellationToken);

        try
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

            var completed = new SqlOptimizationCompletedMessage(message.SessionId, finalResult);
            await instrumentation.OnCompletedAsync(
                "sql_analysis",
                Id,
                message,
                completed,
                startedAt,
                DateTimeOffset.UtcNow,
                executionId: executionId,
                cancellationToken: cancellationToken);

            return completed;
        }
        catch (Exception ex)
        {
            await instrumentation.OnFailedAsync(
                "sql_analysis",
                Id,
                message,
                ex,
                startedAt,
                DateTimeOffset.UtcNow,
                executionId,
                cancellationToken);
            throw;
        }
    }

    private static bool ReadRequireHumanReview(WorkflowResultEnvelope envelope)
    {
        return envelope.Metadata.TryGetProperty("requireHumanReview", out var element) &&
               element.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}

public sealed class WorkflowFailedException(string message) : Exception(message);
#pragma warning restore CS0618
