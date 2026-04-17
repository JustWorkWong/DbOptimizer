using DbOptimizer.Infrastructure.Workflows;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class WorkflowTimelineTests
{
    [Fact]
    public void Append_AssignsIncreasingSequenceAndPersistsEvents()
    {
        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "SqlAnalysis");

        var started = WorkflowTimeline.Append(
            context,
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowStarted,
                sessionId,
                "SqlAnalysis",
                DateTimeOffset.UtcNow,
                new { isResume = false }));

        var completed = WorkflowTimeline.Append(
            context,
            new WorkflowEventMessage(
                WorkflowEventType.WorkflowCompleted,
                sessionId,
                "SqlAnalysis",
                DateTimeOffset.UtcNow,
                new { completedExecutors = 6 }));

        var events = WorkflowTimeline.GetEvents(context);

        Assert.Equal(1, started.Sequence);
        Assert.Equal(2, completed.Sequence);
        Assert.Collection(
            events,
            first => Assert.Equal(WorkflowEventType.WorkflowStarted, first.EventType),
            second => Assert.Equal(WorkflowEventType.WorkflowCompleted, second.EventType));
    }

    [Fact]
    public void Append_TrimsPersistedEventsToConfiguredLimit()
    {
        var sessionId = Guid.NewGuid();
        var context = new WorkflowContext(sessionId, "SqlAnalysis");

        for (var index = 0; index < 2050; index++)
        {
            WorkflowTimeline.Append(
                context,
                new WorkflowEventMessage(
                    WorkflowEventType.CheckpointSaved,
                    sessionId,
                    "SqlAnalysis",
                    DateTimeOffset.UtcNow.AddSeconds(index),
                    new { checkpointVersion = index + 1 }));
        }

        var events = WorkflowTimeline.GetEvents(context);

        Assert.Equal(2048, events.Count);
        Assert.Equal(3, events.First().Sequence);
        Assert.Equal(2050, events.Last().Sequence);
    }
}
