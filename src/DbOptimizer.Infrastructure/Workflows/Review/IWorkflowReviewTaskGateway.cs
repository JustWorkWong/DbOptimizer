using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Workflows.Review;

/* =========================
 * Workflow Review Task Gateway
 * 职责：持久化审核任务，包含 MAF correlation 字段
 * ========================= */
public interface IWorkflowReviewTaskGateway
{
    Task<Guid> CreateAsync(
        Guid sessionId,
        string taskType,
        string requestId,
        string engineRunId,
        string checkpointRef,
        WorkflowResultEnvelope payload,
        CancellationToken cancellationToken = default);

    Task<ReviewTaskCorrelation?> GetCorrelationAsync(
        Guid taskId,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        Guid taskId,
        string status,
        string? comment,
        string? adjustmentsJson,
        DateTimeOffset reviewedAt,
        CancellationToken cancellationToken = default);
}

public sealed record ReviewTaskCorrelation(
    Guid SessionId,
    string WorkflowType,
    string RequestId,
    string EngineRunId,
    string CheckpointRef,
    WorkflowResultEnvelope Payload);
