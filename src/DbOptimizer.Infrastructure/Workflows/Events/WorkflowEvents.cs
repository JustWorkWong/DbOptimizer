using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace DbOptimizer.Infrastructure.Workflows;

public enum WorkflowEventType
{
    WorkflowStarted,
    ExecutorStarted,
    ExecutorCompleted,
    ExecutorFailed,
    WorkflowWaitingReview,
    WorkflowCancelled,
    WorkflowCompleted,
    WorkflowFailed,
    CheckpointSaved
}

public sealed record WorkflowEventMessage(
    WorkflowEventType EventType,
    Guid SessionId,
    string WorkflowType,
    DateTimeOffset Timestamp,
    object Payload,
    long? Sequence = null);

public sealed record WorkflowEventRecord(
    long Sequence,
    WorkflowEventType EventType,
    Guid SessionId,
    string WorkflowType,
    DateTimeOffset Timestamp,
    JsonElement Payload);

public sealed record WorkflowEventSubscription(
    IReadOnlyList<WorkflowEventRecord> Backlog,
    ChannelReader<WorkflowEventRecord> Reader,
    Action Dispose);

public interface IWorkflowEventPublisher
{
    Task PublishAsync(WorkflowEventMessage workflowEvent, CancellationToken cancellationToken = default);
}

public interface IWorkflowEventQueryService
{
    IReadOnlyList<WorkflowEventRecord> GetEvents(Guid sessionId, long afterSequence = 0, int limit = 200);

    WorkflowEventSubscription Subscribe(Guid sessionId, long afterSequence = 0);
}

/* =========================
 * Workflow 事件中心
 * 设计目标：
 * 1) 继续承担统一事件发布入口
 * 2) 为 SSE 与回放提供进程内事件缓冲与订阅能力
 * 3) 保留结构化日志，便于联调和排障
 * ========================= */
public sealed class WorkflowEventHub(ILogger<WorkflowEventHub> logger) : IWorkflowEventPublisher, IWorkflowEventQueryService
{
    private const int MaxBufferedEventsPerSession = 512;
    private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, SessionEventBuffer> _buffers = new();

    public Task PublishAsync(WorkflowEventMessage workflowEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CleanupExpiredBuffers();

        var buffer = _buffers.GetOrAdd(workflowEvent.SessionId, static _ => new SessionEventBuffer());
        WorkflowEventRecord record;
        ChannelWriter<WorkflowEventRecord>[] subscribers;

        lock (buffer.Gate)
        {
            buffer.LastTouchedAt = DateTimeOffset.UtcNow;
            var nextSequence = workflowEvent.Sequence ?? buffer.NextSequence + 1;
            buffer.NextSequence = Math.Max(buffer.NextSequence, nextSequence);

            record = new WorkflowEventRecord(
                nextSequence,
                workflowEvent.EventType,
                workflowEvent.SessionId,
                workflowEvent.WorkflowType,
                workflowEvent.Timestamp,
                JsonSerializer.SerializeToElement(workflowEvent.Payload, SerializerOptions));

            buffer.Events.Add(record);
            if (buffer.Events.Count > MaxBufferedEventsPerSession)
            {
                buffer.Events.RemoveRange(0, buffer.Events.Count - MaxBufferedEventsPerSession);
            }

            subscribers = buffer.Subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.TryWrite(record);
        }

        logger.LogInformation(
            "Workflow event published. EventType={EventType}, SessionId={SessionId}, WorkflowType={WorkflowType}, Sequence={Sequence}, Payload={Payload}",
            record.EventType,
            record.SessionId,
            record.WorkflowType,
            record.Sequence,
            workflowEvent.Payload);

        return Task.CompletedTask;
    }

    public IReadOnlyList<WorkflowEventRecord> GetEvents(Guid sessionId, long afterSequence = 0, int limit = 200)
    {
        CleanupExpiredBuffers();
        if (!_buffers.TryGetValue(sessionId, out var buffer))
        {
            return [];
        }

        lock (buffer.Gate)
        {
            buffer.LastTouchedAt = DateTimeOffset.UtcNow;
            return buffer.Events
                .Where(item => item.Sequence > afterSequence)
                .OrderBy(item => item.Sequence)
                .Take(limit)
                .ToArray();
        }
    }

    public WorkflowEventSubscription Subscribe(Guid sessionId, long afterSequence = 0)
    {
        CleanupExpiredBuffers();
        var buffer = _buffers.GetOrAdd(sessionId, static _ => new SessionEventBuffer());
        var channel = Channel.CreateUnbounded<WorkflowEventRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        WorkflowEventRecord[] backlog;
        lock (buffer.Gate)
        {
            buffer.LastTouchedAt = DateTimeOffset.UtcNow;
            backlog = buffer.Events
                .Where(item => item.Sequence > afterSequence)
                .OrderBy(item => item.Sequence)
                .ToArray();
            buffer.Subscribers.Add(channel.Writer);
        }

        void Dispose()
        {
            lock (buffer.Gate)
            {
                buffer.LastTouchedAt = DateTimeOffset.UtcNow;
                buffer.Subscribers.Remove(channel.Writer);
            }

            channel.Writer.TryComplete();
            TryCleanupSessionBuffer(sessionId, buffer);
        }

        return new WorkflowEventSubscription(backlog, channel.Reader, Dispose);
    }

    private void CleanupExpiredBuffers()
    {
        var threshold = DateTimeOffset.UtcNow - SessionRetention;

        foreach (var pair in _buffers)
        {
            var shouldRemove = false;
            lock (pair.Value.Gate)
            {
                shouldRemove = pair.Value.Subscribers.Count == 0 && pair.Value.LastTouchedAt < threshold;
            }

            if (shouldRemove)
            {
                _buffers.TryRemove(pair.Key, out _);
            }
        }
    }

    private void TryCleanupSessionBuffer(Guid sessionId, SessionEventBuffer buffer)
    {
        lock (buffer.Gate)
        {
            if (buffer.Subscribers.Count > 0)
            {
                return;
            }

            if (buffer.LastTouchedAt >= DateTimeOffset.UtcNow - SessionRetention)
            {
                return;
            }
        }

        _buffers.TryRemove(sessionId, out _);
    }

    private sealed class SessionEventBuffer
    {
        public object Gate { get; } = new();

        public long NextSequence { get; set; }

        public DateTimeOffset LastTouchedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<WorkflowEventRecord> Events { get; } = [];

        public HashSet<ChannelWriter<WorkflowEventRecord>> Subscribers { get; } = [];
    }
}
