using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Workflows.Application;

namespace DbOptimizer.Infrastructure.Workflows.Events;

/// <summary>
/// MAF 工作流事件适配器实现
/// 将 MAF 内部事件转换为 SSE 可消费的业务事件
/// </summary>
public sealed class MafWorkflowEventAdapter(IWorkflowProgressCalculator progressCalculator) : IMafWorkflowEventAdapter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public WorkflowEventRecord CreateSnapshot(WorkflowStatusResponse snapshot)
    {
        var payload = new
        {
            sessionId = snapshot.SessionId,
            workflowType = snapshot.WorkflowType,
            status = snapshot.Status,
            currentNode = snapshot.CurrentNode,
            progressPercent = snapshot.ProgressPercent,
            startedAt = snapshot.StartedAt,
            updatedAt = snapshot.UpdatedAt,
            completedAt = snapshot.CompletedAt,
            source = snapshot.Source,
            review = snapshot.Review,
            result = snapshot.Result,
            error = snapshot.Error
        };

        return new WorkflowEventRecord(
            0,
            WorkflowEventType.WorkflowStarted,
            snapshot.SessionId,
            snapshot.WorkflowType,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(payload, SerializerOptions));
    }

    public IReadOnlyList<WorkflowEventRecord> Map(
        Guid sessionId,
        string workflowType,
        IReadOnlyList<WorkflowEventRecord> mafEvents)
    {
        var businessEvents = new List<WorkflowEventRecord>();

        foreach (var mafEvent in mafEvents)
        {
            var businessEvent = MapSingleEvent(sessionId, workflowType, mafEvent);
            if (businessEvent is not null)
            {
                businessEvents.Add(businessEvent);
            }
        }

        return businessEvents;
    }

    private WorkflowEventRecord? MapSingleEvent(
        Guid sessionId,
        string workflowType,
        WorkflowEventRecord mafEvent)
    {
        var businessEventType = mafEvent.EventType switch
        {
            WorkflowEventType.WorkflowStarted => "workflow.started",
            WorkflowEventType.ExecutorStarted => "executor.started",
            WorkflowEventType.ExecutorCompleted => "executor.completed",
            WorkflowEventType.ExecutorFailed => "executor.failed",
            WorkflowEventType.WorkflowWaitingReview => "review.requested",
            WorkflowEventType.WorkflowCompleted => "workflow.completed",
            WorkflowEventType.WorkflowFailed => "workflow.failed",
            WorkflowEventType.WorkflowCancelled => "workflow.cancelled",
            WorkflowEventType.CheckpointSaved => "checkpoint.saved",
            _ => null
        };

        if (businessEventType is null)
        {
            return null;
        }

        var payload = EnrichPayload(workflowType, mafEvent);

        return new WorkflowEventRecord(
            mafEvent.Sequence,
            mafEvent.EventType,
            sessionId,
            workflowType,
            mafEvent.Timestamp,
            JsonSerializer.SerializeToElement(new
            {
                eventType = businessEventType,
                sessionId,
                workflowType,
                timestamp = mafEvent.Timestamp,
                payload
            }, SerializerOptions));
    }

    private object EnrichPayload(string workflowType, WorkflowEventRecord mafEvent)
    {
        var originalPayload = mafEvent.Payload;

        if (mafEvent.EventType is WorkflowEventType.ExecutorStarted or WorkflowEventType.ExecutorCompleted)
        {
            var executorName = TryGetString(originalPayload, "executorName") ?? string.Empty;
            var status = mafEvent.EventType == WorkflowEventType.ExecutorStarted ? "Running" : "Completed";
            var progressPercent = progressCalculator.GetProgressPercent(workflowType, executorName, status);

            return new
            {
                executorName,
                stage = TryGetString(originalPayload, "stage"),
                message = TryGetString(originalPayload, "message"),
                progressPercent,
                timestamp = mafEvent.Timestamp,
                details = TryGetProperty(originalPayload, "details"),
                tokenUsage = TryGetProperty(originalPayload, "tokenUsage"),
                durationMs = TryGetProperty(originalPayload, "durationMs")
            };
        }

        return originalPayload;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static object? TryGetProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? JsonSerializer.Deserialize<object>(property.GetRawText(), SerializerOptions)
            : null;
    }
}
