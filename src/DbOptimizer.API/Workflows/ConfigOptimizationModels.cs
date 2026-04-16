namespace DbOptimizer.API.Workflows;

/* =========================
 * 配置优化相关数据模型
 * ========================= */

/// <summary>
/// 数据库配置快照
/// </summary>
internal sealed record DbConfigSnapshot
{
    /// <summary>
    /// 数据库类型（MySQL/PostgreSQL）
    /// </summary>
    public required string DatabaseType { get; init; }

    /// <summary>
    /// 数据库 ID
    /// </summary>
    public required string DatabaseId { get; init; }

    /// <summary>
    /// 配置参数列表
    /// </summary>
    public required IReadOnlyList<ConfigParameter> Parameters { get; init; }

    /// <summary>
    /// 系统指标
    /// </summary>
    public required SystemMetrics Metrics { get; init; }

    /// <summary>
    /// 收集时间
    /// </summary>
    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 是否使用了降级方案（MCP 失败时）
    /// </summary>
    public bool UsedFallback { get; init; }

    /// <summary>
    /// 降级原因
    /// </summary>
    public string? FallbackReason { get; init; }
}

/// <summary>
/// 单个配置参数
/// </summary>
internal sealed record ConfigParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 当前值
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// 默认值
    /// </summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>
    /// 参数描述
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 是否可动态修改
    /// </summary>
    public bool IsDynamic { get; init; }

    /// <summary>
    /// 参数类型（string/integer/boolean/enum）
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 最小值（数值类型）
    /// </summary>
    public string? MinValue { get; init; }

    /// <summary>
    /// 最大值（数值类型）
    /// </summary>
    public string? MaxValue { get; init; }
}

/// <summary>
/// 系统指标
/// </summary>
internal sealed record SystemMetrics
{
    /// <summary>
    /// CPU 核心数
    /// </summary>
    public int CpuCores { get; init; }

    /// <summary>
    /// 总内存（字节）
    /// </summary>
    public long TotalMemoryBytes { get; init; }

    /// <summary>
    /// 可用内存（字节）
    /// </summary>
    public long AvailableMemoryBytes { get; init; }

    /// <summary>
    /// 磁盘总空间（字节）
    /// </summary>
    public long TotalDiskBytes { get; init; }

    /// <summary>
    /// 磁盘可用空间（字节）
    /// </summary>
    public long AvailableDiskBytes { get; init; }

    /// <summary>
    /// 数据库版本
    /// </summary>
    public string DatabaseVersion { get; init; } = string.Empty;

    /// <summary>
    /// 数据库运行时长（秒）
    /// </summary>
    public long UptimeSeconds { get; init; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; init; }

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; init; }
}

/// <summary>
/// 配置收集选项
/// </summary>
internal sealed class ConfigCollectionOptions
{
    public const string SectionName = "DbOptimizer:ConfigCollection";

    public ConfigCollectionMcpServerOptions MySql { get; set; } = new();

    public ConfigCollectionMcpServerOptions PostgreSql { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 2;

    public int RetryDelayMilliseconds { get; set; } = 1_000;
}

/// <summary>
/// 配置收集 MCP 服务器选项
/// </summary>
internal sealed class ConfigCollectionMcpServerOptions
{
    public bool Enabled { get; set; } = true;

    public string Transport { get; set; } = "stdio";

    public string Command { get; set; } = "npx";

    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// 配置优化建议
/// </summary>
internal sealed record ConfigRecommendation
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public required string ParameterName { get; init; }

    /// <summary>
    /// 当前值
    /// </summary>
    public required string CurrentValue { get; init; }

    /// <summary>
    /// 推荐值
    /// </summary>
    public required string RecommendedValue { get; init; }

    /// <summary>
    /// 推荐理由
    /// </summary>
    public required string Reasoning { get; init; }

    /// <summary>
    /// 置信度（0.0-1.0）
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// 影响级别（High/Medium/Low）
    /// </summary>
    public required string Impact { get; init; }

    /// <summary>
    /// 是否需要重启数据库
    /// </summary>
    public bool RequiresRestart { get; init; }

    /// <summary>
    /// 证据引用
    /// </summary>
    public IReadOnlyList<string> EvidenceRefs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 规则名称
    /// </summary>
    public string RuleName { get; init; } = string.Empty;
}
