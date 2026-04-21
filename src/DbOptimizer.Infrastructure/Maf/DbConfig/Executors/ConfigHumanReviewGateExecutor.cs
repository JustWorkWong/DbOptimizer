using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

#pragma warning disable CS0618
[SendsMessage(typeof(ConfigReviewRequestMessage))]
public sealed class ConfigHumanReviewGateExecutor(
    IMafExecutorInstrumentation instrumentation,
    IWorkflowReviewTaskGateway reviewTaskGateway,
    IConfigReviewAdjustmentService adjustmentService,
    ILogger<ConfigHumanReviewGateExecutor> logger)
    : ReflectingExecutor<ConfigHumanReviewGateExecutor>("ConfigHumanReviewGateExecutor"),
        IMessageHandler<DbConfigOptimizationDraftReadyMessage>,
        IMessageHandler<ConfigReviewDecisionResponseMessage, DbConfigOptimizationCompletedMessage>
{
    public async ValueTask HandleAsync(
        DbConfigOptimizationDraftReadyMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executionId = await instrumentation.OnStartedAsync(
            "db_config_optimization",
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
                    "Config human review not required. SessionId={SessionId}",
                    message.SessionId);

                var completed = new DbConfigOptimizationCompletedMessage(message.SessionId, message.DraftResult);
                await instrumentation.OnCompletedAsync(
                    "db_config_optimization",
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
                "DbConfigOptimization",
                string.Empty,
                string.Empty,
                string.Empty,
                message.DraftResult,
                cancellationToken);

            logger.LogInformation(
                "Config review task created and review request queued. SessionId={SessionId}, TaskId={TaskId}",
                message.SessionId,
                taskId);

            await context.SendMessageAsync(
                new ConfigReviewRequestMessage(
                    message.SessionId,
                    taskId,
                    message.DraftResult),
                cancellationToken);
            await instrumentation.OnCompletedAsync(
                "db_config_optimization",
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
                "db_config_optimization",
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

    public async ValueTask<DbConfigOptimizationCompletedMessage> HandleAsync(
        ConfigReviewDecisionResponseMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executionId = await instrumentation.OnStartedAsync(
            "db_config_optimization",
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

            var completed = new DbConfigOptimizationCompletedMessage(message.SessionId, finalResult);
            await instrumentation.OnCompletedAsync(
                "db_config_optimization",
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
                "db_config_optimization",
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
