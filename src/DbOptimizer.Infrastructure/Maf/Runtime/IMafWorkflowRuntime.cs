namespace DbOptimizer.Infrastructure.Maf.Runtime;

using Microsoft.Agents.AI.Workflows;

/// <summary>
/// MAF Workflow 运行时接口，负责启动、恢复、取消 workflow
/// </summary>
public interface IMafWorkflowRuntime
{
    /// <summary>
    /// 启动 SQL 分析 workflow
    /// </summary>
    Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动数据库配置优化 workflow
    /// </summary>
    Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已暂停的 workflow（例如从 HITL 审核点恢复）
    /// </summary>
    Task<WorkflowResumeResponse> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已暂停的 SQL workflow，传递审核决策消息
    /// </summary>
    Task<WorkflowResumeResponse> ResumeSqlWorkflowAsync(
        Guid sessionId,
        ExternalResponse reviewResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已暂停的 Config workflow，传递审核决策消息
    /// </summary>
    Task<WorkflowResumeResponse> ResumeConfigWorkflowAsync(
        Guid sessionId,
        ExternalResponse reviewResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消正在运行的 workflow
    /// </summary>
    Task<WorkflowCancelResponse> CancelAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SQL 分析 workflow 命令
/// </summary>
public sealed record SqlAnalysisWorkflowCommand(
    Guid SessionId,
    string SqlText,
    string DatabaseType,
    string? SchemaName = null,
    bool EnableIndexRecommendation = true,
    bool EnableSqlRewrite = true,
    bool RequireHumanReview = false,
    string DatabaseId = "",
    string SourceType = "manual",
    Guid? SourceRefId = null);

/// <summary>
/// 数据库配置优化 workflow 命令
/// </summary>
public sealed record DbConfigWorkflowCommand(
    Guid SessionId,
    string DatabaseId,
    string DatabaseType,
    bool AllowFallbackSnapshot = false,
    bool RequireHumanReview = false,
    string SourceType = "manual",
    Guid? SourceRefId = null);

/// <summary>
/// Workflow 启动响应
/// </summary>
public sealed record WorkflowStartResponse(
    Guid SessionId,
    string RunId,
    string Status);

/// <summary>
/// Workflow 恢复响应
/// </summary>
public sealed record WorkflowResumeResponse(
    Guid SessionId,
    string Status);

/// <summary>
/// Workflow 取消响应
/// </summary>
public sealed record WorkflowCancelResponse(
    Guid SessionId,
    string Status);
