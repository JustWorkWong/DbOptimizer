using System.Text;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using DbOptimizer.Infrastructure.Workflows.Events;
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
        IWorkflowApplicationService workflowApplicationService,
        IWorkflowEventQueryService workflowEventQueryService,
        IMafWorkflowEventAdapter mafWorkflowEventAdapter,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowApplicationService.GetAsync(sessionId, cancellationToken);
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
        var mafEvents = WorkflowTimeline.GetEvents(checkpoint)
            .Where(item => item.Sequence > lastEventId)
            .OrderBy(item => item.Sequence)
            .ToArray();

        var businessEvents = mafWorkflowEventAdapter.Map(sessionId, workflow.WorkflowType, mafEvents);

        var highestPersistedSequence = businessEvents.LastOrDefault()?.Sequence ?? lastEventId;
        foreach (var businessEvent in businessEvents)
        {
            await WriteBusinessEventAsync(response, businessEvent, cancellationToken);
        }

        await WriteSnapshotAsync(response, workflow, mafWorkflowEventAdapter, cancellationToken);

        var subscription = workflowEventQueryService.Subscribe(sessionId, highestPersistedSequence);
        try
        {
            var backlogBusinessEvents = mafWorkflowEventAdapter.Map(sessionId, workflow.WorkflowType, subscription.Backlog);
            foreach (var backlogEvent in backlogBusinessEvents)
            {
                await WriteBusinessEventAsync(response, backlogEvent, cancellationToken);
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
                        var newEvents = new List<WorkflowEventRecord>();
                        while (subscription.Reader.TryRead(out var mafEvent))
                        {
                            newEvents.Add(mafEvent);
                        }

                        var newBusinessEvents = mafWorkflowEventAdapter.Map(sessionId, workflow.WorkflowType, newEvents);
                        foreach (var businessEvent in newBusinessEvents)
                        {
                            await WriteBusinessEventAsync(response, businessEvent, cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
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

    private static async Task WriteBusinessEventAsync(
        HttpResponse response,
        WorkflowEventRecord businessEvent,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append("id: ").Append(businessEvent.Sequence).AppendLine();
        builder.Append("event: workflow-event").AppendLine();
        builder.Append("data: ").Append(businessEvent.Payload.GetRawText()).AppendLine().AppendLine();

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
        IMafWorkflowEventAdapter mafWorkflowEventAdapter,
        CancellationToken cancellationToken)
    {
        var snapshotRecord = mafWorkflowEventAdapter.CreateSnapshot(workflow);
        var builder = new StringBuilder();
        builder.Append("event: snapshot").AppendLine();
        builder.Append("data: ").Append(snapshotRecord.Payload.GetRawText()).AppendLine().AppendLine();

        await response.WriteAsync(builder.ToString(), cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
