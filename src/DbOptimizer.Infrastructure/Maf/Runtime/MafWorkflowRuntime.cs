using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 运行时实现（占位实现）
/// </summary>
public sealed class MafWorkflowRuntime : IMafWorkflowRuntime
{
    private readonly IMafWorkflowFactory _workflowFactory;
    private readonly IMafRunStateStore _runStateStore;
    private readonly MafWorkflowRuntimeOptions _options;
    private readonly ILogger<MafWorkflowRuntime> _logger;

    public MafWorkflowRuntime(
        IMafWorkflowFactory workflowFactory,
        IMafRunStateStore runStateStore,
        MafWorkflowRuntimeOptions options,
        ILogger<MafWorkflowRuntime> logger)
    {
        _workflowFactory = workflowFactory;
        _runStateStore = runStateStore;
        _options = options;
        _logger = logger;
    }

    [Obsolete("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) instead.")]
    public Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        SqlAnalysisWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MAF engine not implemented. Use WorkflowApplicationService instead.");
        throw new NotImplementedException("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) for SQL analysis workflows.");
    }

    [Obsolete("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) instead.")]
    public Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        DbConfigWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MAF engine not implemented. Use WorkflowApplicationService instead.");
        throw new NotImplementedException("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) for DB config workflows.");
    }

    [Obsolete("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) instead.")]
    public Task<WorkflowResumeResponse> ResumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MAF engine not implemented. Use WorkflowApplicationService instead.");
        throw new NotImplementedException("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) for workflow resume.");
    }

    [Obsolete("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) instead.")]
    public Task<WorkflowCancelResponse> CancelAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("MAF engine not implemented. Use WorkflowApplicationService instead.");
        throw new NotImplementedException("MAF engine not yet implemented. Use WorkflowApplicationService (legacy engine) for workflow cancellation.");
    }
}
