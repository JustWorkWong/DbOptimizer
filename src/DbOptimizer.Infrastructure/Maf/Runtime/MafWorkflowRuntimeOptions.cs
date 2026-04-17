namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 运行时配置选项
/// </summary>
public sealed class MafWorkflowRuntimeOptions
{
    /// <summary>
    /// 是否启用 checkpoint 自动刷盘
    /// </summary>
    public bool CheckpointFlushEnabled { get; set; } = true;

    /// <summary>
    /// 最大并发运行数
    /// </summary>
    public int MaxConcurrentRuns { get; set; } = 10;

    /// <summary>
    /// Checkpoint 存储路径（可选，默认使用数据库）
    /// </summary>
    public string? CheckpointStorePath { get; set; }

    /// <summary>
    /// Workflow 执行超时时间（秒）
    /// </summary>
    public int WorkflowTimeoutSeconds { get; set; } = 3600;
}
