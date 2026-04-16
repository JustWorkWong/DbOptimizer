namespace DbOptimizer.API.Workflows;

/* =========================
 * ExecutionPlan 结果模型
 * 既保留原始执行计划，又抽取最小可消费的性能问题和指标。
 * ========================= */
internal sealed class ExecutionPlanResult
{
    public string DatabaseEngine { get; set; } = "Unknown";

    public string ToolName { get; set; } = "explain";

    public string RawPlan { get; set; } = string.Empty;

    public bool UsedFallback { get; set; }

    public bool IsPartial { get; set; }

    public int AttemptCount { get; set; }

    public string? DiagnosticTag { get; set; }

    public long ElapsedMs { get; set; }

    public Dictionary<string, object> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ExecutionPlanIssue> Issues { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

internal sealed class ExecutionPlanIssue
{
    public string Type { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? TableName { get; set; }

    public double ImpactScore { get; set; }

    public string Evidence { get; set; } = string.Empty;
}

internal enum DatabaseOptimizationEngine
{
    Unknown,
    MySql,
    PostgreSql
}

internal sealed class ExecutionPlanOptions
{
    public const string SectionName = "DbOptimizer:ExecutionPlan";

    public ExecutionPlanMcpServerOptions MySql { get; set; } = new();

    public ExecutionPlanMcpServerOptions PostgreSql { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 2;

    public int RetryDelayMilliseconds { get; set; } = 1_000;

    public bool EnableDirectDbFallback { get; set; } = true;
}

internal sealed class ExecutionPlanMcpServerOptions
{
    public bool Enabled { get; set; } = true;

    public string Transport { get; set; } = "stdio";

    public string Command { get; set; } = "npx";

    public string Arguments { get; set; } = string.Empty;
}

internal sealed class ExecutionPlanInvocationResult
{
    public string ToolName { get; set; } = "explain";

    public string RawText { get; set; } = string.Empty;

    public bool UsedFallback { get; set; }

    public int AttemptCount { get; set; }

    public string? DiagnosticTag { get; set; }

    public long ElapsedMs { get; set; }
}
