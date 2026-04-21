namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

public sealed class IndexAdvisorLlmResponse
{
    public List<IndexAdvisorLlmRecommendation> Recommendations { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class IndexAdvisorLlmRecommendation
{
    public string TableName { get; set; } = string.Empty;

    public List<string> Columns { get; set; } = [];

    public string IndexType { get; set; } = "BTREE";

    public string CreateDdl { get; set; } = string.Empty;

    public double EstimatedBenefit { get; set; }

    public string Reasoning { get; set; } = string.Empty;

    public List<string> EvidenceRefs { get; set; } = [];

    public double Confidence { get; set; }
}
