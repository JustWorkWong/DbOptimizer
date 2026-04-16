namespace DbOptimizer.API.Workflows;

/* =========================
 * Coordinator 汇总模型
 * 让后续 HumanReview / API / 前端都能直接消费统一结果。
 * ========================= */
internal sealed class OptimizationReport
{
    public string Summary { get; set; } = string.Empty;

    public List<IndexRecommendation> IndexRecommendations { get; set; } = new();

    public List<SqlRewriteSuggestion> SqlRewriteSuggestions { get; set; } = new();

    public double OverallConfidence { get; set; }

    public List<EvidenceItem> EvidenceChain { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SqlRewriteSuggestion
{
    public string Description { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public double Confidence { get; set; }
}

internal sealed class EvidenceItem
{
    public string SourceType { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string? Snippet { get; set; }
}
