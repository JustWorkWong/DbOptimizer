namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

public sealed class ConfigAnalyzerLlmResponse
{
    public List<ConfigAnalyzerLlmRecommendation> Recommendations { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class ConfigAnalyzerLlmRecommendation
{
    public string ParameterName { get; set; } = string.Empty;

    public string CurrentValue { get; set; } = string.Empty;

    public string RecommendedValue { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public string Impact { get; set; } = string.Empty;

    public bool RequiresRestart { get; set; }

    public List<string> EvidenceRefs { get; set; } = [];

    public string RuleName { get; set; } = string.Empty;
}
