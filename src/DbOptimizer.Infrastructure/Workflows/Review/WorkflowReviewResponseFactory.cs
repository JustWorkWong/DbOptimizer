using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Workflows.Review;

public sealed class WorkflowReviewResponseFactory : IWorkflowReviewResponseFactory
{
    private static readonly WorkflowResultEnvelope PlaceholderEnvelope = new()
    {
        ResultType = "review_request",
        DisplayName = "Review Request",
        Summary = string.Empty,
        Data = JsonSerializer.SerializeToElement(new { }),
        Metadata = JsonSerializer.SerializeToElement(new { })
    };

    public ExternalResponse CreateSqlResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string action,
        string? comment,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        var request = ExternalRequest.Create(
            MafReviewPorts.SqlReview,
            new SqlReviewRequestMessage(sessionId, taskId, PlaceholderEnvelope),
            requestId);

        return request.CreateResponse(new SqlReviewResponseMessage(
            sessionId,
            taskId,
            action,
            comment,
            adjustments,
            DateTimeOffset.UtcNow));
    }

    public ExternalResponse CreateDbConfigResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string action,
        string? comment,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        var request = ExternalRequest.Create(
            MafReviewPorts.ConfigReview,
            new ConfigReviewRequestMessage(sessionId, taskId, PlaceholderEnvelope),
            requestId);

        return request.CreateResponse(new ConfigReviewDecisionResponseMessage(
            sessionId,
            taskId,
            action,
            comment,
            adjustments,
            DateTimeOffset.UtcNow));
    }
}
