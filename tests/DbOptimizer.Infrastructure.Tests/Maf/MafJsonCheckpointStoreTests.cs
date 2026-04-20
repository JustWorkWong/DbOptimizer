using System.Text.Json;
using DbOptimizer.Infrastructure.Maf.Runtime;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Maf;

public sealed class MafJsonCheckpointStoreTests
{
    private readonly Mock<IMafRunStateStore> _runStateStoreMock = new();
    private readonly MafJsonCheckpointStore _store;

    public MafJsonCheckpointStoreTests()
    {
        _store = new MafJsonCheckpointStore(
            _runStateStoreMock.Object,
            Mock.Of<ILogger<MafJsonCheckpointStore>>());
    }

    [Fact]
    public async Task CreateCheckpointAsync_PersistsJsonPayloadIntoRunState()
    {
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";
        var payload = JsonDocument.Parse("""{"status":"pending","step":2}""").RootElement;
        MafRunState? existingState = new(
            sessionId,
            runId,
            string.Empty,
            "{}",
            DateTime.UtcNow);

        _runStateStoreMock
            .Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        string? checkpointRef = null;
        string? engineState = null;

        _runStateStoreMock
            .Setup(x => x.SaveAsync(
                sessionId,
                runId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((_, _, checkpoint, state, _) =>
            {
                checkpointRef = checkpoint;
                engineState = state;
            })
            .Returns(Task.CompletedTask);

        var checkpoint = await _store.CreateCheckpointAsync(sessionId.ToString(), payload, null);

        Assert.Equal(sessionId.ToString(), checkpoint.SessionId);
        Assert.Equal(checkpoint.CheckpointId, checkpointRef);
        Assert.Equal(payload.GetRawText(), engineState);
    }

    [Fact]
    public async Task RetrieveCheckpointAsync_ReturnsPersistedJsonPayload()
    {
        var sessionId = Guid.NewGuid();
        var checkpoint = new CheckpointInfo(sessionId.ToString(), Guid.NewGuid().ToString("N"));
        var payloadJson = """{"status":"suspended","requestId":"r-1"}""";

        _runStateStoreMock
            .Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MafRunState(
                sessionId,
                "run-123",
                checkpoint.CheckpointId,
                payloadJson,
                DateTime.UtcNow));

        var result = await _store.RetrieveCheckpointAsync(sessionId.ToString(), checkpoint);

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal("suspended", result.GetProperty("status").GetString());
        Assert.Equal("r-1", result.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task RetrieveIndexAsync_ReturnsCheckpointInfoForCurrentRunState()
    {
        var sessionId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid().ToString("N");

        _runStateStoreMock
            .Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MafRunState(
                sessionId,
                "run-123",
                checkpointId,
                """{"ok":true}""",
                DateTime.UtcNow));

        var result = (await _store.RetrieveIndexAsync(sessionId.ToString(), null)).ToList();

        Assert.Single(result);
        Assert.Equal(sessionId.ToString(), result[0].SessionId);
        Assert.Equal(checkpointId, result[0].CheckpointId);
    }
}
