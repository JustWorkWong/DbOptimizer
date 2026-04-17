using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;

namespace DbOptimizer.Infrastructure.Workflows.Review;

public sealed class WorkflowReviewResponseFactory : IWorkflowReviewResponseFactory
{
    public ReviewDecisionResponseMessage CreateSqlResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        string action,
        string? comment,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        return new ReviewDecisionResponseMessage(
            sessionId,
            taskId,
            requestId,
            runId,
            checkpointRef,
            action,
            comment,
            adjustments,
            DateTimeOffset.UtcNow);
    }

    public object CreateDbConfigResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        string action,
        string? comment,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        return new
        {
            sessionId,
            taskId,
            requestId,
            runId,
            checkpointRef,
            action,
            comment,
            adjustments,
            reviewedAt = DateTimeOffset.UtcNow
        };
    }
}
