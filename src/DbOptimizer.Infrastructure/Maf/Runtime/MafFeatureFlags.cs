namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class MafFeatureFlags
{
    public const string SectionName = "MafFeatureFlags";

    public bool EnableIndexAdvisorLlm { get; set; } = true;

    public bool EnableSqlRewriteLlm { get; set; } = true;

    public bool EnableConfigAnalyzerLlm { get; set; } = true;

    public bool EnableLlmStreaming { get; set; }

    public bool EnableFallback { get; set; }
}
