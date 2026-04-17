using System.Text;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.API.Api;

internal static class WorkflowEventsApiRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWorkflowEventsApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workflows/{sessionId:guid}/events", HandleWorkflowEventsAsync);
        return endpoints;
    }

    private static async Task HandleWorkflowEventsAsync(
        Guid sessionId,
        HttpContext httpContext,
        IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
        IWorkflowQueryService workflowQueryService,
        IWorkflowEventQueryService workflowEventQueryService,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowQueryService.GetAsync(sessionId, cancellationToken);
        if (workflow is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(
                new ApiEnvelope<object?>(
                    false,
                    null,
                    new ApiError("WORKFLOW_NOT_FOUND", "Workflow session not found.", new { sessionId }),
                    new ApiMeta(httpContext.TraceIdentifier, DateTimeOffset.UtcNow)),
                cancellationToken);
            return;
        }

        var response = httpContext.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        var lastEventIdHeader = httpContext.Request.Headers["Last-Event-ID"].ToString();
        _ = long.TryParse(lastEventIdHeader, out var lastEventId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var persistedState = await dbContext.WorkflowSessions
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .Select(item => item.State)
            .SingleAsync(cancellationToken);
        var checkpoint = WorkflowCheckpointJson.Deserialize(persistedState);
        var persistedEvents = WorkflowTimeline.GetEvents(checkpoint)
            .Where(item => item.Sequence > lastEventId)
            .OrderBy(item => item.Sequence)
            .ToArray();

        var highestPersistedSequence = persistedEvents.LastOrDefault()?.Sequence ?? lastEventId;
        foreach (var workflowEvent in persistedEvents)
        {
            await WriteEventAsync(response, workflowEvent, cancellationToken);
        }

        await WriteSnapshotAsync(response, workflow, cancellationToken);

        var subscription = workflowEventQueryService.Subscribe(sessionId, highestPersistedSequence);
        try
        {
            foreach (var backlogEvent in subscription.Backlog)
            {
                await WriteEventAsync(response, backlogEvent, cancellationToken);
            }

            var lastHeartbeat = DateTime.UtcNow;
            var heartbeatInterval = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                var timeUntilHeartbeat = heartbeatInterval - (DateTime.UtcNow - lastHeartbeat);
                var timeout = timeUntilHeartbeat > TimeSpan.Zero ? timeUntilHeartbeat : TimeSpan.FromMilliseconds(1);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                try
                {
                    var hasData = await subscription.Reader.WaitToReadAsync(timeoutCts.Token);
                    if (hasData)
                    {
                        while (subscription.Reader.TryRead(out var workflowEvent))
                        {
                            await WriteEventAsync(response, workflowEvent, cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout reached, send heartbeat
                    await WriteHeartbeatAsync(response, cancellationToken);
                    lastHeartbeat = DateTime.UtcNow;
                }
            }
        }
        finally
        {
            subscription.Dispose();
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        WorkflowEventRecord workflowEvent,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            eventType = workflowEvent.EventType.ToString(),
            sessionId = workflowEvent.SessionId,
            workflowType = workflowEvent.WorkflowType,
            sequence = workflowEvent.Sequence,
            timestamp = workflowEvent.Timestamp,
            payload = workflowEvent.Payload
        };

        var builder = new StringBuilder();
        builder.Append("id: ").Append(workflowEvent.Sequence).AppendLine();
        builder.Append("event: WorkflowEvent").AppendLine();
        builder.Append("data: ").Append(JsonSerializer.Serialize(payload, SerializerOptions)).AppendLine().AppendLine();

        await response.WriteAsync(builder.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteHeartbeatAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow }, SerializerOptions);
        var builder = new StringBuilder();
        builder.Append("event: heartbeat").AppendLine();
        builder.Append("data: ").Append(payload).AppendLine().AppendLine();

        await response.WriteAsync(builder.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSnapshotAsync(
        HttpResponse response,
        WorkflowStatusResponse workflow,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            eventType = "WorkflowSnapshot",
            sessionId = workflow.SessionId,
            timestamp = DateTimeOffset.UtcNow,
            payload = workflow
        }, SerializerOptions);

        var builder = new StringBuilder();
        builder.Append("event: snapshot").AppendLine();
        builder.Append("data: ").Append(payload).AppendLine().AppendLine();

        await response.WriteAsync(builder.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
