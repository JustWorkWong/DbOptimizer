namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Run 状态存储接口，负责保存和恢复 workflow 运行状态
/// </summary>
public interface IMafRunStateStore
{
    /// <summary>
    /// 保存 workflow 运行状态
    /// </summary>
    Task SaveAsync(
        Guid sessionId,
        string runId,
        string checkpointRef,
        string engineState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 workflow 运行状态
    /// </summary>
    Task<MafRunState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除 workflow 运行状态
    /// </summary>
    Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MAF 运行状态
/// </summary>
public sealed record MafRunState(
    Guid SessionId,
    string RunId,
    string CheckpointRef,
    string EngineState,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null);
