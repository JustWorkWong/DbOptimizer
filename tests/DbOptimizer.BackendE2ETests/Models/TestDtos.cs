namespace DbOptimizer.BackendE2ETests.Models;

/// <summary>
/// 工作流提交响应
/// </summary>
public sealed class WorkflowSubmitResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// 工作流状态响应
/// </summary>
public sealed class WorkflowStatusResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CurrentExecutor { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public Guid? SourceSlowQueryId { get; set; }
}

/// <summary>
/// 慢查询上报响应
/// </summary>
public sealed class SlowQueryReportResponse
{
    public Guid SlowQueryId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// 工作流简要信息
/// </summary>
public sealed class WorkflowSummary
{
    public Guid SessionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 工作流日志条目
/// </summary>
public sealed class WorkflowLogEntry
{
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 工作流结果
/// </summary>
public sealed class WorkflowResult
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object>? Data { get; set; }
    public List<string>? Recommendations { get; set; }
    public bool? UsedFallback { get; set; }
}

/// <summary>
/// 错误响应
/// </summary>
public sealed class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
