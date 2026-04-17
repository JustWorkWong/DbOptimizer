namespace DbOptimizer.Infrastructure.Workflows.Projection;

/// <summary>
/// 工作流投影写入器
/// 将 MAF 事件投影到持久化存储
/// </summary>
public interface IWorkflowProjectionWriter
{
    /// <summary>
    /// 应用单个事件到投影
    /// </summary>
    Task ApplyEventAsync(
        Guid sessionId,
        WorkflowEventRecord workflowEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从 Checkpoint 同步投影状态
    /// </summary>
    Task SyncFromCheckpointAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
