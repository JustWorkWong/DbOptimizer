using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;

namespace DbOptimizer.Infrastructure.Workflows.Review;

/* =========================
 * Workflow Review Response Factory
 * 职责：从 review submission 构建 MAF response message
 * ========================= */
public interface IWorkflowReviewResponseFactory
{
    ReviewDecisionResponseMessage CreateSqlResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        string action,
        string? comment,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> adjustments);

    object CreateDbConfigResponse(
        Guid sessionId,
        Guid taskId,
        string requestId,
        string runId,
        string checkpointRef,
        string action,
        string? comment,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> adjustments);
}
