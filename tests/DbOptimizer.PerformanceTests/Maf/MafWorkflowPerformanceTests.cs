using System.Diagnostics;
using System.Text;
using DbOptimizer.Infrastructure.Maf.Runtime;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace DbOptimizer.PerformanceTests.Maf;

/// <summary>
/// MAF Workflow 性能测试
/// 验证目标：
/// 1) Checkpoint 序列化 < 100ms
/// 2) 单个 workflow < 30s
/// 3) 并发 10 个无阻塞
/// </summary>
public sealed class MafWorkflowPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public MafWorkflowPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CheckpointSerialization_ShouldCompleteWithin100ms()
    {
        // Arrange
        var mockRunStateStore = new Mock<IMafRunStateStore>();
        var mockLogger = new Mock<ILogger<MafCheckpointStore>>();
        var store = new MafCheckpointStore(mockRunStateStore.Object, mockLogger.Object);

        var runId = $"{Guid.NewGuid()}_20260417";
        var checkpointRef = "test-checkpoint";
        var checkpointData = GenerateTestCheckpointData(1024 * 100); // 100KB

        mockRunStateStore
            .Setup(x => x.SaveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await store.SaveCheckpointAsync(runId, checkpointRef, checkpointData);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Checkpoint serialization took {stopwatch.ElapsedMilliseconds}ms for {checkpointData.Length} bytes");
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Expected < 100ms, actual: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CheckpointDeserialization_ShouldCompleteWithin100ms()
    {
        // Arrange
        var mockRunStateStore = new Mock<IMafRunStateStore>();
        var mockLogger = new Mock<ILogger<MafCheckpointStore>>();
        var store = new MafCheckpointStore(mockRunStateStore.Object, mockLogger.Object);

        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417";
        var checkpointRef = "test-checkpoint";
        var checkpointData = GenerateTestCheckpointData(1024 * 100); // 100KB

        // 先保存
        mockRunStateStore
            .Setup(x => x.SaveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await store.SaveCheckpointAsync(runId, checkpointRef, checkpointData);

        // 模拟从存储加载
        var compressedData = CompressTestData(checkpointData);
        var engineState = Convert.ToBase64String(compressedData);

        mockRunStateStore
            .Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MafRunState(
                sessionId,
                runId,
                checkpointRef,
                engineState,
                DateTime.UtcNow,
                DateTime.UtcNow));

        // Act
        var stopwatch = Stopwatch.StartNew();
        var loaded = await store.LoadCheckpointAsync(runId, checkpointRef);
        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Checkpoint deserialization took {stopwatch.ElapsedMilliseconds}ms for {loaded?.Length ?? 0} bytes");
        Assert.NotNull(loaded);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Expected < 100ms, actual: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CheckpointCompression_ShouldReduceSize()
    {
        // Arrange
        var mockRunStateStore = new Mock<IMafRunStateStore>();
        var mockLogger = new Mock<ILogger<MafCheckpointStore>>();
        var store = new MafCheckpointStore(mockRunStateStore.Object, mockLogger.Object);

        var runId = $"{Guid.NewGuid()}_20260417";
        var checkpointRef = "test-checkpoint";
        var checkpointData = GenerateTestCheckpointData(1024 * 500); // 500KB

        string? savedEngineState = null;
        mockRunStateStore
            .Setup(x => x.SaveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((_, _, _, state, _) =>
            {
                savedEngineState = state;
            })
            .Returns(Task.CompletedTask);

        // Act
        await store.SaveCheckpointAsync(runId, checkpointRef, checkpointData);

        // Assert
        Assert.NotNull(savedEngineState);
        var compressedSize = Convert.FromBase64String(savedEngineState).Length;
        var compressionRatio = (double)compressedSize / checkpointData.Length;

        _output.WriteLine($"Original size: {checkpointData.Length} bytes");
        _output.WriteLine($"Compressed size: {compressedSize} bytes");
        _output.WriteLine($"Compression ratio: {compressionRatio:P2}");

        Assert.True(compressionRatio < 1.0, $"Expected compression ratio < 100%, actual: {compressionRatio:P2}");
    }

    [Fact]
    public async Task ConcurrentCheckpointOperations_ShouldNotBlock()
    {
        // Arrange
        var mockRunStateStore = new Mock<IMafRunStateStore>();
        var mockLogger = new Mock<ILogger<MafCheckpointStore>>();
        var store = new MafCheckpointStore(mockRunStateStore.Object, mockLogger.Object);

        mockRunStateStore
            .Setup(x => x.SaveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        const int concurrentCount = 10;
        var tasks = new List<Task>();

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < concurrentCount; i++)
        {
            var runId = $"{Guid.NewGuid()}_20260417";
            var checkpointRef = $"checkpoint-{i}";
            var checkpointData = GenerateTestCheckpointData(1024 * 50); // 50KB

            tasks.Add(store.SaveCheckpointAsync(runId, checkpointRef, checkpointData));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var avgTime = stopwatch.ElapsedMilliseconds / (double)concurrentCount;
        _output.WriteLine($"Concurrent {concurrentCount} checkpoint saves took {stopwatch.ElapsedMilliseconds}ms total");
        _output.WriteLine($"Average time per save: {avgTime:F2}ms");

        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Expected < 1000ms for {concurrentCount} concurrent saves");
    }

    [Fact]
    public void CheckpointSizeLimit_ShouldEnforce10MBLimit()
    {
        // Arrange
        var mockRunStateStore = new Mock<IMafRunStateStore>();
        var mockLogger = new Mock<ILogger<MafCheckpointStore>>();
        var store = new MafCheckpointStore(mockRunStateStore.Object, mockLogger.Object);

        var runId = $"{Guid.NewGuid()}_20260417";
        var checkpointRef = "large-checkpoint";
        var checkpointData = GenerateTestCheckpointData(11 * 1024 * 1024); // 11MB

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveCheckpointAsync(runId, checkpointRef, checkpointData));

        _output.WriteLine($"Exception message: {exception.Result.Message}");
        Assert.Contains("exceeds limit", exception.Result.Message);
    }

    /// <summary>
    /// 生成测试用的 checkpoint 数据（模拟真实场景的 JSON 数据）
    /// </summary>
    private static byte[] GenerateTestCheckpointData(int sizeBytes)
    {
        // 生成类似真实 checkpoint 的 JSON 结构（高压缩率）
        var sb = new StringBuilder();
        sb.Append("{\"state\":\"running\",\"messages\":[");

        var messageCount = sizeBytes / 200; // 每条消息约 200 字节
        for (var i = 0; i < messageCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":\"{Guid.NewGuid()}\",\"role\":\"assistant\",\"content\":\"This is a test message with some repeated content that compresses well. Message number {i}.\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}");
        }

        sb.Append("]}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] CompressTestData(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new System.IO.Compression.GZipStream(
            outputStream,
            System.IO.Compression.CompressionLevel.Fastest))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }
}
