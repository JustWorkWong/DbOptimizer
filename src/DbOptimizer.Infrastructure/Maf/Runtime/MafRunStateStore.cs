using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Run 状态存储实现（占位实现，使用内存存储）
/// </summary>
public sealed class MafRunStateStore : IMafRunStateStore
{
    private readonly Dictionary<Guid, MafRunState> _store = new();
    private readonly ILogger<MafRunStateStore> _logger;

    public MafRunStateStore(ILogger<MafRunStateStore> logger)
    {
        _logger = logger;
    }

    public Task SaveAsync(
        Guid sessionId,
        string runId,
        string checkpointRef,
        string engineState,
        CancellationToken cancellationToken = default)
    {
        var state = new MafRunState(
            sessionId,
            runId,
            checkpointRef,
            engineState,
            DateTime.UtcNow,
            DateTime.UtcNow);

        _store[sessionId] = state;
        _logger.LogInformation("Saved MAF run state for session {SessionId}", sessionId);

        return Task.CompletedTask;
    }

    public Task<MafRunState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(sessionId, out var state);
        return Task.FromResult(state);
    }

    public Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        _store.Remove(sessionId);
        _logger.LogInformation("Deleted MAF run state for session {SessionId}", sessionId);
        return Task.CompletedTask;
    }
}
