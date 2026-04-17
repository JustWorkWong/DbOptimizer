using System.Text.Json;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis;

/* =========================
 * SQL Review Adjustment Service
 * 职责：应用审核调整到 WorkflowResultEnvelope
 * ========================= */
public interface ISqlReviewAdjustmentService
{
    WorkflowResultEnvelope ApplyAdjustments(
        WorkflowResultEnvelope draft,
        IReadOnlyDictionary<string, JsonElement> adjustments);
}

public sealed class SqlReviewAdjustmentService : ISqlReviewAdjustmentService
{
    public WorkflowResultEnvelope ApplyAdjustments(
        WorkflowResultEnvelope draft,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        if (adjustments.Count == 0)
        {
            return draft;
        }

        var updatedSummary = draft.Summary;
        if (adjustments.TryGetValue("summary", out var summaryElement) &&
            summaryElement.ValueKind == JsonValueKind.String)
        {
            updatedSummary = summaryElement.GetString() ?? draft.Summary;
        }

        return draft with
        {
            Summary = updatedSummary,
            Metadata = MergeMetadata(draft.Metadata, adjustments)
        };
    }

    private static JsonElement MergeMetadata(
        JsonElement originalMetadata,
        IReadOnlyDictionary<string, JsonElement> adjustments)
    {
        var metadataDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            originalMetadata.GetRawText()) ?? new Dictionary<string, JsonElement>();

        metadataDict["reviewAdjustments"] = JsonSerializer.SerializeToElement(adjustments);
        metadataDict["adjustedAt"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);

        return JsonSerializer.SerializeToElement(metadataDict);
    }
}
