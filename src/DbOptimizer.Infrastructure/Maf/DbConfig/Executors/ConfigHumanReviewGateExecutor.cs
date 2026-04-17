using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows.Review;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

/* =========================
 * Config Human Review Gate Executor
 * 职责：
 * 1) 接收 DbConfigOptimizationDraftReadyMessage，根据 RequireHumanReview 决定是否创建审核任务
 * 2) RequireHumanReview = false 时直接输出 completed message
 * 3) RequireHumanReview = true 时创建审核任务并挂起
 * ========================= */
public sealed class ConfigHumanReviewGateExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    IConfigReviewAdjustmentService adjustmentService,
    ILogger<ConfigHumanReviewGateExecutor> logger)
    : Executor<DbConfigOptimizationDraftReadyMessage, DbConfigOptimizationCompletedMessage>("ConfigHumanReviewGateExecutor")
{
    public override async ValueTask<DbConfigOptimizationCompletedMessage> HandleAsync(
        DbConfigOptimizationDraftReadyMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requireHumanReview = ReadRequireHumanReview(message.DraftResult);

        if (!requireHumanReview)
        {
            logger.LogInformation(
                "Human review not required. SessionId={SessionId}",
                message.SessionId);

            return new DbConfigOptimizationCompletedMessage(message.SessionId, message.DraftResult);
        }

        var requestId = message.SessionId.ToString();
        var runId = Guid.NewGuid().ToString();
        var checkpointRef = $"config-review-gate-{message.SessionId}";

        var taskId = await reviewTaskGateway.CreateAsync(
            message.SessionId,
            "DbConfigOptimization",
            requestId,
            runId,
            checkpointRef,
            message.DraftResult,
            cancellationToken);

        logger.LogInformation(
            "Config review task created, workflow suspended. SessionId={SessionId}, TaskId={TaskId}",
            message.SessionId,
            taskId);

        throw new WorkflowSuspendedException($"Waiting for config review task {taskId}");
    }

    public async ValueTask<DbConfigOptimizationCompletedMessage> HandleReviewResponseAsync(
        ReviewDecisionResponseMessage message,
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

    private static bool ReadRequireHumanReview(WorkflowResultEnvelope envelope)
    {
        if (envelope.Metadata.TryGetProperty("requireHumanReview", out var element) &&
            element.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            return true;
        }

        return false;
    }
}

public sealed class WorkflowSuspendedException(string message) : Exception(message);

public sealed class WorkflowFailedException(string message) : Exception(message);
