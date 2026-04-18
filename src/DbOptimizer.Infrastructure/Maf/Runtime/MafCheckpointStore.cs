using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Checkpoint 存储实现
/// 设计目标：
/// 1) 集成 MafRunStateStore，复用 PostgreSQL + Redis 存储
/// 2) 支持 runId 到 sessionId 的映射
/// 3) 自动保存 checkpoint：executor 执行后、review gate 挂起前、错误发生时
/// 4) 支持 checkpoint 大小限制和保留策略
/// </summary>
public sealed class MafCheckpointStore : IMafCheckpointStore
{
    private readonly IMafRunStateStore _runStateStore;
    private readonly ILogger<MafCheckpointStore> _logger;

    // Checkpoint 策略配置
    private const int MaxCheckpointSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly TimeSpan CheckpointRetention = TimeSpan.FromDays(7);

    public MafCheckpointStore(
        IMafRunStateStore runStateStore,
        ILogger<MafCheckpointStore> logger)
    {
        _runStateStore = runStateStore;
        _logger = logger;
    }

    public async Task SaveCheckpointAsync(
        string runId,
        string checkpointRef,
        byte[] checkpointData,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointRef);
        ArgumentNullException.ThrowIfNull(checkpointData);

        // 检查 checkpoint 大小限制
        if (checkpointData.Length > MaxCheckpointSizeBytes)
        {
            _logger.LogWarning(
                "Checkpoint size {Size} bytes exceeds limit {Limit} bytes for runId={RunId}",
                checkpointData.Length,
                MaxCheckpointSizeBytes,
                runId);
            throw new InvalidOperationException(
                $"Checkpoint size {checkpointData.Length} bytes exceeds limit {MaxCheckpointSizeBytes} bytes.");
        }

        // 从 runId 提取 sessionId（格式：{sessionId}_{timestamp}）
        var sessionId = ExtractSessionIdFromRunId(runId);

        // 将 checkpoint 数据转换为 JSON 字符串存储
        var engineState = Convert.ToBase64String(checkpointData);

        await _runStateStore.SaveAsync(
            sessionId,
            runId,
            checkpointRef,
            engineState,
            cancellationToken);

        _logger.LogInformation(
            "Saved checkpoint for runId={RunId}, checkpointRef={CheckpointRef}, size={Size} bytes",
            runId,
            checkpointRef,
            checkpointData.Length);
    }

    public async Task<byte[]?> LoadCheckpointAsync(
        string runId,
        string checkpointRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointRef);

        var sessionId = ExtractSessionIdFromRunId(runId);

        var state = await _runStateStore.GetAsync(sessionId, cancellationToken);

        if (state is null)
        {
            _logger.LogWarning(
                "No checkpoint found for runId={RunId}, checkpointRef={CheckpointRef}",
                runId,
                checkpointRef);
            return null;
        }

        // 验证 checkpointRef 匹配
        if (state.CheckpointRef != checkpointRef)
        {
            _logger.LogWarning(
                "Checkpoint ref mismatch: expected={Expected}, actual={Actual} for runId={RunId}",
                checkpointRef,
                state.CheckpointRef,
                runId);
            return null;
        }

        // 检查 checkpoint 是否过期
        var age = DateTime.UtcNow - (state.UpdatedAt ?? state.CreatedAt);
        if (age > CheckpointRetention)
        {
            _logger.LogWarning(
                "Checkpoint expired (age={Age}) for runId={RunId}",
                age,
                runId);
            return null;
        }

        try
        {
            var checkpointData = Convert.FromBase64String(state.EngineState);

            _logger.LogInformation(
                "Loaded checkpoint for runId={RunId}, checkpointRef={CheckpointRef}, size={Size} bytes",
                runId,
                checkpointRef,
                checkpointData.Length);

            return checkpointData;
        }
        catch (FormatException ex)
        {
            _logger.LogError(
                ex,
                "Failed to decode checkpoint data for runId={RunId}",
                runId);
            return null;
        }
    }

    public async Task DeleteCheckpointAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var sessionId = ExtractSessionIdFromRunId(runId);

        await _runStateStore.DeleteAsync(sessionId, cancellationToken);

        _logger.LogInformation("Deleted checkpoint for runId={RunId}", runId);
    }

    /// <summary>
    /// 从 runId 提取 sessionId
    /// runId 格式：{sessionId}_{timestamp} 或直接是 sessionId
    /// </summary>
    private static Guid ExtractSessionIdFromRunId(string runId)
    {
        var parts = runId.Split('_', 2);
        var sessionIdStr = parts[0];

        if (!Guid.TryParse(sessionIdStr, out var sessionId))
        {
            throw new ArgumentException($"Invalid runId format: {runId}. Expected format: {{sessionId}}_{{timestamp}}");
        }

        return sessionId;
    }
}
