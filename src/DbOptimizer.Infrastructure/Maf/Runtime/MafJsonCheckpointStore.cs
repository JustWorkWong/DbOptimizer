using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class MafJsonCheckpointStore(
    IMafRunStateStore runStateStore,
    ILogger<MafJsonCheckpointStore> logger) : ICheckpointStore<JsonElement>
{
    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent)
    {
        var state = await TryGetStateAsync(sessionId);
        if (state is null)
        {
            return Array.Empty<CheckpointInfo>();
        }

        return [new CheckpointInfo(sessionId, state.CheckpointRef)];
    }

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent)
    {
        var sessionGuid = ParseSessionId(sessionId);
        var existingState = await runStateStore.GetAsync(sessionGuid);
        var runId = existingState?.RunId ?? $"maf_run_{Guid.NewGuid():N}";
        var checkpoint = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));

        await runStateStore.SaveAsync(
            sessionGuid,
            runId,
            checkpoint.CheckpointId,
            value.GetRawText());

        logger.LogDebug(
            "Stored JSON checkpoint. SessionId={SessionId}, RunId={RunId}, CheckpointId={CheckpointId}",
            sessionId,
            runId,
            checkpoint.CheckpointId);

        return checkpoint;
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(
        string sessionId,
        CheckpointInfo key)
    {
        var state = await TryGetStateAsync(sessionId);
        if (state is null || !string.Equals(state.CheckpointRef, key.CheckpointId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checkpoint '{key.CheckpointId}' for session '{sessionId}' was not found.");
        }

        using var document = JsonDocument.Parse(state.EngineState);
        return document.RootElement.Clone();
    }

    private async Task<MafRunState?> TryGetStateAsync(string sessionId)
    {
        var sessionGuid = ParseSessionId(sessionId);
        return await runStateStore.GetAsync(sessionGuid);
    }

    private static Guid ParseSessionId(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var parsed))
        {
            throw new ArgumentException($"Invalid session id: {sessionId}", nameof(sessionId));
        }

        return parsed;
    }
}
