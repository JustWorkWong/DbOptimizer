namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

public sealed class SqlRewriteLlmResponse
{
    public List<SqlRewriteLlmSuggestion> Suggestions { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class SqlRewriteLlmSuggestion
{
    public string Category { get; set; } = string.Empty;

    public string OriginalFragment { get; set; } = string.Empty;

    public string SuggestedFragment { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public double EstimatedBenefit { get; set; }

    public List<string> EvidenceRefs { get; set; } = [];

    public double Confidence { get; set; }
}
