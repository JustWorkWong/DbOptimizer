using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime;

namespace DbOptimizer.Infrastructure.Tests.Maf;

/// <summary>
/// MafCheckpointStore 集成测试
/// 验证 checkpoint 自动保存机制
/// </summary>
public sealed class MafCheckpointStoreTests
{
    private readonly Mock<IMafRunStateStore> _runStateStoreMock;
    private readonly Mock<ILogger<MafCheckpointStore>> _loggerMock;
    private readonly MafCheckpointStore _store;

    public MafCheckpointStoreTests()
    {
        _runStateStoreMock = new Mock<IMafRunStateStore>();
        _loggerMock = new Mock<ILogger<MafCheckpointStore>>();
        _store = new MafCheckpointStore(_runStateStoreMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task SaveCheckpointAsync_SavesCheckpointSuccessfully()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var checkpointData = "test checkpoint data"u8.ToArray();

        // Act
        await _store.SaveCheckpointAsync(runId, checkpointRef, checkpointData);

        // Assert
        _runStateStoreMock.Verify(
            x => x.SaveAsync(
                sessionId,
                runId,
                checkpointRef,
                It.Is<string>(s => !string.IsNullOrEmpty(s)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveCheckpointAsync_ThrowsWhenCheckpointTooLarge()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var checkpointData = new byte[11 * 1024 * 1024]; // 11MB > 10MB limit

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.SaveCheckpointAsync(runId, checkpointRef, checkpointData));
    }

    [Fact]
    public async Task SaveCheckpointAsync_ThrowsWhenRunIdInvalid()
    {
        // Arrange
        var runId = "invalid_run_id";
        var checkpointRef = "checkpoint_001";
        var checkpointData = "test"u8.ToArray();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.SaveCheckpointAsync(runId, checkpointRef, checkpointData));
    }

    [Fact]
    public async Task LoadCheckpointAsync_ReturnsCheckpointWhenExists()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var checkpointData = "test checkpoint data"u8.ToArray();
        var base64Data = Convert.ToBase64String(checkpointData);

        var state = new MafRunState(
            sessionId,
            runId,
            checkpointRef,
            base64Data,
            DateTime.UtcNow,
            DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act
        var result = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(checkpointData, result);
    }

    [Fact]
    public async Task LoadCheckpointAsync_ReturnsNullWhenCheckpointNotFound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MafRunState?)null);

        // Act
        var result = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCheckpointAsync_ReturnsNullWhenCheckpointRefMismatch()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var checkpointData = "test"u8.ToArray();
        var base64Data = Convert.ToBase64String(checkpointData);

        var state = new MafRunState(
            sessionId,
            runId,
            "checkpoint_002", // Different checkpoint ref
            base64Data,
            DateTime.UtcNow,
            DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act
        var result = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadCheckpointAsync_ReturnsNullWhenCheckpointExpired()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var checkpointData = "test"u8.ToArray();
        var base64Data = Convert.ToBase64String(checkpointData);

        var state = new MafRunState(
            sessionId,
            runId,
            checkpointRef,
            base64Data,
            DateTime.UtcNow.AddDays(-8), // 8 days ago > 7 days retention
            DateTime.UtcNow.AddDays(-8));

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act
        var result = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteCheckpointAsync_DeletesCheckpointSuccessfully()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";

        // Act
        await _store.DeleteCheckpointAsync(runId);

        // Assert
        _runStateStoreMock.Verify(
            x => x.DeleteAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveCheckpointAsync_HandlesRoundTripCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";
        var originalData = "test checkpoint data with special chars: 中文测试"u8.ToArray();

        string? capturedEngineState = null;
        _runStateStoreMock.Setup(x => x.SaveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>(
                (_, _, _, engineState, _) => capturedEngineState = engineState)
            .Returns(Task.CompletedTask);

        // Act - Save
        await _store.SaveCheckpointAsync(runId, checkpointRef, originalData);

        // Assert - Verify saved
        Assert.NotNull(capturedEngineState);

        // Setup for Load
        var state = new MafRunState(
            sessionId,
            runId,
            checkpointRef,
            capturedEngineState,
            DateTime.UtcNow,
            DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act - Load
        var loadedData = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert - Verify round-trip
        Assert.NotNull(loadedData);
        Assert.Equal(originalData, loadedData);
    }

    [Fact]
    public async Task LoadCheckpointAsync_ReturnsNullWhenBase64Invalid()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"{sessionId}_20260417120000";
        var checkpointRef = "checkpoint_001";

        var state = new MafRunState(
            sessionId,
            runId,
            checkpointRef,
            "invalid_base64_data!!!",
            DateTime.UtcNow,
            DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        // Act
        var result = await _store.LoadCheckpointAsync(runId, checkpointRef);

        // Assert
        Assert.Null(result);
    }
}
