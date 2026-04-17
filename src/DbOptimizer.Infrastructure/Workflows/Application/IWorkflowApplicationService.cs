using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Workflows.Application;

/// <summary>
/// Workflow 应用服务接口
/// </summary>
public interface IWorkflowApplicationService
{
    /// <summary>
    /// 启动 SQL 分析 Workflow
    /// </summary>
    Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动数据库配置优化 Workflow
    /// </summary>
    Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Workflow 状态
    /// </summary>
    Task<WorkflowStatusResponse?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复 Workflow
    /// </summary>
    Task<WorkflowResumeResponse> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消 Workflow
    /// </summary>
    Task<WorkflowCancelResponse> CancelAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Workflow 启动响应
/// </summary>
public sealed record WorkflowStartResponse(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string Status,
    DateTimeOffset StartedAt);

/// <summary>
/// Workflow 状态响应
/// </summary>
public sealed record WorkflowStatusResponse(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string Status,
    string? CurrentNode,
    int ProgressPercent,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    WorkflowSourceDto Source,
    WorkflowReviewSummaryDto? Review,
    WorkflowResultEnvelope? Result,
    WorkflowErrorDto? Error);

/// <summary>
/// Workflow 恢复响应
/// </summary>
public sealed record WorkflowResumeResponse(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string Status);

/// <summary>
/// Workflow 取消响应
/// </summary>
public sealed record WorkflowCancelResponse(
    Guid SessionId,
    string WorkflowType,
    string EngineType,
    string Status);

/// <summary>
/// Workflow 来源 DTO
/// </summary>
public sealed record WorkflowSourceDto(
    string SourceType,
    string? SourceRefId);

/// <summary>
/// Workflow 审核摘要 DTO
/// </summary>
public sealed record WorkflowReviewSummaryDto(
    Guid TaskId,
    string Status);

/// <summary>
/// Workflow 错误 DTO
/// </summary>
public sealed record WorkflowErrorDto(
    string Code,
    string Message,
    string? StackTrace);
