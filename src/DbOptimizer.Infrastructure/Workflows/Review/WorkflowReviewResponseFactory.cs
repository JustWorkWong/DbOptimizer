using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.DbConfig;

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

    public ConfigReviewDecisionResponseMessage CreateDbConfigResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        string action,
        string? comment,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        return new ConfigReviewDecisionResponseMessage(
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
}
