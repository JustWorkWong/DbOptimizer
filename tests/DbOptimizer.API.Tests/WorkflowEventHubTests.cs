using DbOptimizer.API.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class WorkflowEventHubTests
{
    [Fact]
    public async Task PublishAsync_MakesEventsAvailableToQueryAndSubscription()
    {
        var hub = new WorkflowEventHub(NullLogger<WorkflowEventHub>.Instance);
        var sessionId = Guid.NewGuid();

        await hub.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowStarted,
                sessionId,
                "SqlAnalysis",
                DateTimeOffset.UtcNow,
                new { isResume = false }));

        var subscription = hub.Subscribe(sessionId);
        try
        {
            Assert.Single(subscription.Backlog);
            Assert.Equal(1, subscription.Backlog[0].Sequence);

            await hub.PublishAsync(
                new WorkflowEventMessage(
                    WorkflowEventType.ExecutorStarted,
                    sessionId,
                    "SqlAnalysis",
                    DateTimeOffset.UtcNow,
                    new { executorName = "SqlParserExecutor" }));

            var published = await subscription.Reader.ReadAsync();

            Assert.Equal(2, published.Sequence);
            Assert.Equal(WorkflowEventType.ExecutorStarted, published.EventType);

            var queried = hub.GetEvents(sessionId, afterSequence: 1);
            Assert.Single(queried);
            Assert.Equal(2, queried[0].Sequence);
        }
        finally
        {
            subscription.Dispose();
        }
    }

    [Fact]
    public async Task PublishAsync_UsesProvidedSequenceWhenPresent()
    {
        var hub = new WorkflowEventHub(NullLogger<WorkflowEventHub>.Instance);
        var sessionId = Guid.NewGuid();

        await hub.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowCompleted,
                sessionId,
                "SqlAnalysis",
                DateTimeOffset.UtcNow,
                new { completedExecutors = 6 },
                Sequence: 42));

        var queried = hub.GetEvents(sessionId);

        Assert.Single(queried);
        Assert.Equal(42, queried[0].Sequence);
    }
}
