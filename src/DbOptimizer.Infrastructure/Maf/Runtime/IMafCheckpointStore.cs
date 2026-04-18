namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Checkpoint 存储接口（MAF 标准接口）
/// 设计目标：
/// 1) 符合 MAF 1.0.0-rc4 checkpoint 接口规范
/// 2) 支持 workflow 运行时自动保存 checkpoint
/// 3) 支持 checkpoint 恢复和清理
/// </summary>
public interface IMafCheckpointStore
{
    /// <summary>
    /// 保存 checkpoint 数据
    /// </summary>
    /// <param name="runId">MAF workflow run ID</param>
    /// <param name="checkpointRef">Checkpoint 引用标识</param>
    /// <param name="checkpointData">序列化的 checkpoint 数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SaveCheckpointAsync(
        string runId,
        string checkpointRef,
        byte[] checkpointData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载 checkpoint 数据
    /// </summary>
    /// <param name="runId">MAF workflow run ID</param>
    /// <param name="checkpointRef">Checkpoint 引用标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>序列化的 checkpoint 数据，不存在时返回 null</returns>
    Task<byte[]?> LoadCheckpointAsync(
        string runId,
        string checkpointRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定 run 的所有 checkpoint
    /// </summary>
    /// <param name="runId">MAF workflow run ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DeleteCheckpointAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
