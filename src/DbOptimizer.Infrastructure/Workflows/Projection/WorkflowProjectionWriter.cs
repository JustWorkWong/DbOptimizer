using System.Text.Json;
using System.Text.Json.Serialization;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Workflows.Projection;

/// <summary>
/// 工作流投影写入器实现
/// 将 MAF 事件投影到 workflow_sessions 表
/// </summary>
public sealed class WorkflowProjectionWriter(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowResultSerializer workflowResultSerializer,
    ILogger<WorkflowProjectionWriter> logger) : IWorkflowProjectionWriter
{
    private static readonly JsonSerializerOptions CheckpointSerializerOptions = CreateCheckpointSerializerOptions();
    private static readonly JsonSerializerOptions TimelineSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task ApplyEventAsync(
        Guid sessionId,
        WorkflowEventRecord workflowEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var session = await dbContext.WorkflowSessions
                .Where(x => x.SessionId == sessionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (session is null)
            {
                logger.LogWarning(
                    "Workflow session not found for projection. SessionId={SessionId}",
                    sessionId);
                return;
            }

            ApplyEventToSession(session, workflowEvent);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogDebug(
                "Workflow projection applied. SessionId={SessionId}, EventType={EventType}",
                sessionId,
                workflowEvent.EventType);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to apply workflow projection. SessionId={SessionId}, EventType={EventType}",
                sessionId,
                workflowEvent.EventType);
        }
    }

    public async Task SyncFromCheckpointAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var session = await dbContext.WorkflowSessions
                .Where(x => x.SessionId == sessionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (session is null)
            {
                logger.LogWarning(
                    "Workflow session not found for checkpoint sync. SessionId={SessionId}",
                    sessionId);
                return;
            }

            var checkpoint = DeserializeCheckpoint(session.State);
            if (checkpoint is null)
            {
                return;
            }

            SyncSessionFromCheckpoint(session, checkpoint);
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Workflow projection synced from checkpoint. SessionId={SessionId}, Status={Status}",
                sessionId,
                checkpoint.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to sync workflow projection from checkpoint. SessionId={SessionId}",
                sessionId);
        }
    }

    private void ApplyEventToSession(WorkflowSessionEntity session, WorkflowEventRecord workflowEvent)
    {
        PersistTimelineEvent(session, workflowEvent);
        session.UpdatedAt = workflowEvent.Timestamp;

        switch (workflowEvent.EventType)
        {
            case WorkflowEventType.WorkflowStarted:
                session.Status = WorkflowSessionStatus.Running;
                break;

            case WorkflowEventType.ExecutorStarted:
            case WorkflowEventType.ExecutorCompleted:
                session.Status = WorkflowSessionStatus.Running;
                break;

            case WorkflowEventType.WorkflowWaitingReview:
                session.Status = WorkflowSessionStatus.WaitingForReview;
                break;

            case WorkflowEventType.WorkflowCompleted:
                session.Status = WorkflowSessionStatus.Completed;
                session.CompletedAt = workflowEvent.Timestamp;
                ExtractResultType(session);
                break;

            case WorkflowEventType.WorkflowFailed:
                session.Status = WorkflowSessionStatus.Failed;
                session.CompletedAt = workflowEvent.Timestamp;
                ExtractErrorMessage(session, workflowEvent);
                break;

            case WorkflowEventType.WorkflowCancelled:
                session.Status = WorkflowSessionStatus.Cancelled;
                session.CompletedAt = workflowEvent.Timestamp;
                break;
        }
    }

    private void SyncSessionFromCheckpoint(WorkflowSessionEntity session, WorkflowCheckpoint checkpoint)
    {
        session.Status = checkpoint.Status.ToString();
        session.UpdatedAt = checkpoint.UpdatedAt;

        if (checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled)
        {
            session.CompletedAt = checkpoint.UpdatedAt;
        }

        if (checkpoint.Context.TryGetValue(WorkflowContextKeys.FinalResult, out var resultElement) &&
            resultElement.ValueKind != JsonValueKind.Undefined)
        {
            var databaseId = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseId, out var dbIdElement)
                ? dbIdElement.Deserialize<string>(CheckpointSerializerOptions)
                : null;

            var databaseType = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseType, out var dbTypeElement)
                ? dbTypeElement.Deserialize<string>(CheckpointSerializerOptions)
                : null;

            var envelope = workflowResultSerializer.ToEnvelope(
                checkpoint.WorkflowType,
                resultElement,
                databaseId,
                databaseType);

            session.ResultType = envelope.ResultType;
        }

        if (checkpoint.Context.TryGetValue("LastError", out var errorElement) &&
            errorElement.ValueKind == JsonValueKind.String)
        {
            session.ErrorMessage = errorElement.GetString();
        }
    }

    private void ExtractResultType(WorkflowSessionEntity session)
    {
        try
        {
            var checkpoint = DeserializeCheckpoint(session.State);
            if (checkpoint is null)
            {
                return;
            }

            if (!checkpoint.Context.TryGetValue(WorkflowContextKeys.FinalResult, out var resultElement) ||
                resultElement.ValueKind == JsonValueKind.Undefined)
            {
                return;
            }

            var databaseId = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseId, out var dbIdElement)
                ? dbIdElement.Deserialize<string>(CheckpointSerializerOptions)
                : null;

            var databaseType = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseType, out var dbTypeElement)
                ? dbTypeElement.Deserialize<string>(CheckpointSerializerOptions)
                : null;

            var envelope = workflowResultSerializer.ToEnvelope(
                checkpoint.WorkflowType,
                resultElement,
                databaseId,
                databaseType);

            session.ResultType = envelope.ResultType;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract result type. SessionId={SessionId}", session.SessionId);
        }
    }

    private void ExtractErrorMessage(WorkflowSessionEntity session, WorkflowEventRecord workflowEvent)
    {
        try
        {
            if (workflowEvent.Payload.TryGetProperty("errorMessage", out var errorProp) &&
                errorProp.ValueKind == JsonValueKind.String)
            {
                session.ErrorMessage = errorProp.GetString();
                return;
            }

            if (workflowEvent.Payload.TryGetProperty("error", out var legacyErrorProp) &&
                legacyErrorProp.ValueKind == JsonValueKind.String)
            {
                session.ErrorMessage = legacyErrorProp.GetString();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract error message. SessionId={SessionId}", session.SessionId);
        }
    }

    private static WorkflowCheckpoint? DeserializeCheckpoint(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowCheckpoint>(json, CheckpointSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private void PersistTimelineEvent(WorkflowSessionEntity session, WorkflowEventRecord workflowEvent)
    {
        try
        {
            var checkpoint = DeserializeCheckpoint(session.State);
            if (checkpoint is null)
            {
                return;
            }

            var events = WorkflowTimeline.GetEvents(checkpoint)
                .Where(item => item.Sequence != workflowEvent.Sequence)
                .Append(workflowEvent)
                .OrderBy(item => item.Sequence)
                .TakeLast(2048)
                .ToList();

            var context = new Dictionary<string, JsonElement>(checkpoint.Context, StringComparer.OrdinalIgnoreCase)
            {
                [WorkflowContextKeys.WorkflowTimeline] = JsonSerializer.SerializeToElement(events, TimelineSerializerOptions),
                [WorkflowContextKeys.WorkflowTimelineNextSequence] = JsonSerializer.SerializeToElement(
                    events.Count == 0 ? 0L : events[^1].Sequence,
                    TimelineSerializerOptions)
            };

            session.State = JsonSerializer.Serialize(
                checkpoint with
                {
                    Context = context
                },
                CheckpointSerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist workflow timeline event. SessionId={SessionId}", session.SessionId);
        }
    }

    private static JsonSerializerOptions CreateCheckpointSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
