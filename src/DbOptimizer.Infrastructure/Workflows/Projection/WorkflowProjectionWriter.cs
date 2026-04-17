using System.Text.Json;
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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
        session.UpdatedAt = DateTimeOffset.UtcNow;

        switch (workflowEvent.EventType)
        {
            case WorkflowEventType.WorkflowStarted:
                session.Status = "Running";
                break;

            case WorkflowEventType.ExecutorStarted:
            case WorkflowEventType.ExecutorCompleted:
                session.Status = "Running";
                break;

            case WorkflowEventType.WorkflowWaitingReview:
                session.Status = "WaitingReview";
                break;

            case WorkflowEventType.WorkflowCompleted:
                session.Status = "Completed";
                session.CompletedAt = DateTimeOffset.UtcNow;
                ExtractResultType(session);
                break;

            case WorkflowEventType.WorkflowFailed:
                session.Status = "Failed";
                session.CompletedAt = DateTimeOffset.UtcNow;
                ExtractErrorMessage(session, workflowEvent);
                break;

            case WorkflowEventType.WorkflowCancelled:
                session.Status = "Cancelled";
                session.CompletedAt = DateTimeOffset.UtcNow;
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
                ? dbIdElement.Deserialize<string>(SerializerOptions)
                : null;

            var databaseType = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseType, out var dbTypeElement)
                ? dbTypeElement.Deserialize<string>(SerializerOptions)
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
                ? dbIdElement.Deserialize<string>(SerializerOptions)
                : null;

            var databaseType = checkpoint.Context.TryGetValue(WorkflowContextKeys.DatabaseType, out var dbTypeElement)
                ? dbTypeElement.Deserialize<string>(SerializerOptions)
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
            return JsonSerializer.Deserialize<WorkflowCheckpoint>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }
}
