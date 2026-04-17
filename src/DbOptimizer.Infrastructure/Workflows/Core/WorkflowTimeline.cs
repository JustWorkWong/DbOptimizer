using DbOptimizer.Core.Models;
using System.Text.Json;
using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.Infrastructure.Workflows;

public static class WorkflowTimeline
{
    private const int MaxPersistedEventsPerSession = 2048;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static WorkflowEventRecord Append(WorkflowContext context, WorkflowEventMessage workflowEvent)
    {
        var existingEvents = GetEvents(context).ToList();
        var nextSequence = 1L;

        if (context.TryGet<long>(WorkflowContextKeys.WorkflowTimelineNextSequence, out var persistedSequence) &&
            persistedSequence > 0)
        {
            nextSequence = persistedSequence + 1;
        }
        else if (existingEvents.Count > 0)
        {
            nextSequence = existingEvents[^1].Sequence + 1;
        }

        var record = new WorkflowEventRecord(
            nextSequence,
            workflowEvent.EventType,
            workflowEvent.SessionId,
            workflowEvent.WorkflowType,
            workflowEvent.Timestamp,
            JsonSerializer.SerializeToElement(workflowEvent.Payload, SerializerOptions));

        existingEvents.Add(record);
        if (existingEvents.Count > MaxPersistedEventsPerSession)
        {
            existingEvents.RemoveRange(0, existingEvents.Count - MaxPersistedEventsPerSession);
        }

        context.Set(WorkflowContextKeys.WorkflowTimeline, existingEvents);
        context.Set(WorkflowContextKeys.WorkflowTimelineNextSequence, nextSequence);
        return record;
    }

    public static IReadOnlyList<WorkflowEventRecord> GetEvents(WorkflowContext context)
    {
        return context.TryGet<List<WorkflowEventRecord>>(WorkflowContextKeys.WorkflowTimeline, out var items) &&
               items is not null
            ? items
            : [];
    }

    public static IReadOnlyList<WorkflowEventRecord> GetEvents(WorkflowCheckpoint? checkpoint)
    {
        if (checkpoint is null ||
            !checkpoint.Context.TryGetValue(WorkflowContextKeys.WorkflowTimeline, out var itemsElement))
        {
            return [];
        }

        return itemsElement.Deserialize<List<WorkflowEventRecord>>(SerializerOptions) ?? [];
    }
}
