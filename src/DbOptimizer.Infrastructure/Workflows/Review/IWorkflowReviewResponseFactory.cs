using DbOptimizer.Core.Models;
using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Workflows.Review;

/* =========================
 * Workflow Review Response Factory
 * 职责：从 review submission 构建 MAF response message
 * ========================= */
public interface IWorkflowReviewResponseFactory
{
    ExternalResponse CreateSqlResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string action,
        string? comment,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> adjustments);

    ExternalResponse CreateDbConfigResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string action,
        string? comment,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> adjustments);
}
