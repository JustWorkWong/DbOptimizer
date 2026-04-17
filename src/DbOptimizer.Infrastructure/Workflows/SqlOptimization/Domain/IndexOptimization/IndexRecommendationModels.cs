using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * 索引建议模型
 * 输出尽量贴近设计文档，便于后续 Coordinator / Review 直接消费。
 * ========================= */
internal sealed class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = new();

    public string IndexType { get; set; } = "BTREE";

    public string CreateDdl { get; set; } = string.Empty;

    public double EstimatedBenefit { get; set; }

    public string Reasoning { get; set; } = string.Empty;

    public List<string> EvidenceRefs { get; set; } = new();

    public double Confidence { get; set; }
}

internal sealed class ExistingIndexDefinition
{
    public string IndexName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = new();

    public bool IsUnique { get; set; }

    public string RawDefinition { get; set; } = string.Empty;
}

internal sealed class TableIndexMetadata
{
    public string TableName { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public bool UsedFallback { get; set; }

    public List<ExistingIndexDefinition> ExistingIndexes { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

internal sealed class IndexMetadataInvocationResult
{
    public string ToolName { get; set; } = "show_indexes";

    public string RawText { get; set; } = string.Empty;

    public bool UsedFallback { get; set; }

    public int AttemptCount { get; set; }

    public string? DiagnosticTag { get; set; }

    public long ElapsedMs { get; set; }
}
