namespace DbOptimizer.Infrastructure.Workflows.Monitoring;

/// <summary>
/// Token 使用量记录器配置选项
/// </summary>
public sealed class TokenUsageRecorderOptions
{
    /// <summary>
    /// 查询超时时间（秒），默认 30 秒
    /// </summary>
    public int QueryTimeoutSeconds { get; set; } = 30;
}
