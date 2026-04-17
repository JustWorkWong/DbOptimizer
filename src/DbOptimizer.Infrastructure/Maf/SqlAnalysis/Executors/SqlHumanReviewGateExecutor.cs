using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using DbOptimizer.Infrastructure.Workflows.Review;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * SQL Human Review Gate Executor
 * 职责：
 * 1) 接收 SqlOptimizationDraftReadyMessage，创建审核任务并挂起
 * 2) 接收 ReviewDecisionResponseMessage，应用调整并恢复
 * ========================= */
public sealed class SqlHumanReviewGateExecutor(
    IWorkflowReviewTaskGateway reviewTaskGateway,
    ISqlReviewAdjustmentService adjustmentService,
    ILogger<SqlHumanReviewGateExecutor> logger)
    : Executor<SqlOptimizationDraftReadyMessage, SqlOptimizationCompletedMessage>("SqlHumanReviewGateExecutor")
{
    public override async ValueTask<SqlOptimizationCompletedMessage> HandleAsync(
        SqlOptimizationDraftReadyMessage message,
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

            return new SqlOptimizationCompletedMessage(message.SessionId, message.DraftResult);
        }

        // MAF context 不提供直接访问，使用 session ID 作为 correlation
        var requestId = message.SessionId.ToString();
        var runId = Guid.NewGuid().ToString();
        var checkpointRef = $"review-gate-{message.SessionId}";

        var taskId = await reviewTaskGateway.CreateAsync(
            message.SessionId,
            "SqlOptimization",
            requestId,
            runId,
            checkpointRef,
            message.DraftResult,
            cancellationToken);

        logger.LogInformation(
            "Review task created, workflow suspended. SessionId={SessionId}, TaskId={TaskId}",
            message.SessionId,
            taskId);

        throw new WorkflowSuspendedException($"Waiting for review task {taskId}");
    }

    public async ValueTask<SqlOptimizationCompletedMessage> HandleReviewResponseAsync(
        ReviewDecisionResponseMessage message,
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
