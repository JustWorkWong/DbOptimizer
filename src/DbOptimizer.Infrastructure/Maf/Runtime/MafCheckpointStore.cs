using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Checkpoint 存储实现
/// 当前职责：
/// 1) 提供 checkpoint 数据的压缩/解压与存储适配
/// 2) 复用 MafRunStateStore 对 PostgreSQL + Redis 的持久化能力
/// 3) 作为 native checkpointing 重构中的适配层存在
/// 注意：
/// - 当前生产主链路尚未统一通过 CheckpointManager 驱动此存储
/// - “自动保存 checkpoint”的完整生命周期仍属于目标态，而非现状
/// </summary>
public sealed class MafCheckpointStore : IMafCheckpointStore
{
    private readonly IMafRunStateStore _runStateStore;
    private readonly ILogger<MafCheckpointStore> _logger;

    // Checkpoint 策略配置
    private const int MaxCheckpointSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly TimeSpan CheckpointRetention = TimeSpan.FromDays(7);
    private const CompressionLevel CompressionLevel = System.IO.Compression.CompressionLevel.Fastest;

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

        var startTime = DateTime.UtcNow;

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

        // 压缩 checkpoint 数据
        var compressedData = CompressData(checkpointData);
        var compressionRatio = (double)compressedData.Length / checkpointData.Length;

        // 将压缩后的数据转换为 Base64 存储
        var engineState = Convert.ToBase64String(compressedData);

        await _runStateStore.SaveAsync(
            sessionId,
            runId,
            checkpointRef,
            engineState,
            cancellationToken);

        var duration = DateTime.UtcNow - startTime;

        _logger.LogInformation(
            "Saved checkpoint for runId={RunId}, checkpointRef={CheckpointRef}, " +
            "originalSize={OriginalSize} bytes, compressedSize={CompressedSize} bytes, " +
            "compressionRatio={CompressionRatio:P2}, duration={Duration}ms",
            runId,
            checkpointRef,
            checkpointData.Length,
            compressedData.Length,
            compressionRatio,
            duration.TotalMilliseconds);
    }

    public async Task<byte[]?> LoadCheckpointAsync(
        string runId,
        string checkpointRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointRef);

        var startTime = DateTime.UtcNow;
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
            var compressedData = Convert.FromBase64String(state.EngineState);
            var checkpointData = DecompressData(compressedData);

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Loaded checkpoint for runId={RunId}, checkpointRef={CheckpointRef}, " +
                "compressedSize={CompressedSize} bytes, decompressedSize={DecompressedSize} bytes, " +
                "duration={Duration}ms",
                runId,
                checkpointRef,
                compressedData.Length,
                checkpointData.Length,
                duration.TotalMilliseconds);

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
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to decompress checkpoint data for runId={RunId}",
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

    /// <summary>
    /// 使用 GZip 压缩数据
    /// </summary>
    private static byte[] CompressData(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }

    /// <summary>
    /// 解压 GZip 数据
    /// </summary>
    private static byte[] DecompressData(byte[] compressedData)
    {
        using var inputStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
