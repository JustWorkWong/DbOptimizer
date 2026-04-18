using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Events;
using DbOptimizer.Infrastructure.Workflows.Application;
using Moq;
using Xunit;

namespace DbOptimizer.Infrastructure.Tests.Workflows.Events;

public sealed class MafWorkflowEventAdapterTests
{
    private readonly Mock<IWorkflowProgressCalculator> _progressCalculatorMock;
    private readonly MafWorkflowEventAdapter _adapter;

    public MafWorkflowEventAdapterTests()
    {
        _progressCalculatorMock = new Mock<IWorkflowProgressCalculator>();
        _adapter = new MafWorkflowEventAdapter(_progressCalculatorMock.Object);
    }

    [Fact]
    public void CreateSnapshot_ShouldCreateWorkflowStartedEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var snapshot = new WorkflowStatusResponse(
            SessionId: sessionId,
            WorkflowType: "SqlAnalysis",
            EngineType: "maf",
            Status: "Running",
            CurrentNode: "SqlParserMafExecutor",
            ProgressPercent: 20,
            StartedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            Source: new WorkflowSourceDto("manual", null),
            Review: null,
            Result: null,
            Error: null);

        // Act
        var result = _adapter.CreateSnapshot(snapshot);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(WorkflowEventType.WorkflowStarted, result.EventType);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("SqlAnalysis", result.WorkflowType);
        Assert.True(result.Payload.TryGetProperty("sessionId", out var sessionIdProp));
        Assert.Equal(sessionId, sessionIdProp.GetGuid());
    }

    [Fact]
    public void Map_ShouldMapWorkflowStartedEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(sessionId, WorkflowEventType.WorkflowStarted, new { message = "Workflow started" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.Equal(WorkflowEventType.WorkflowStarted, result[0].EventType);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("workflow.started", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldMapExecutorStartedEvent_WithProgressCalculation()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.ExecutorStarted,
            new { executorName = "SqlParserMafExecutor", stage = "parsing", message = "Parsing SQL" });

        _progressCalculatorMock
            .Setup(x => x.GetProgressPercent("SqlAnalysis", "SqlParserMafExecutor", "Running"))
            .Returns(16);

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("executor.started", eventTypeProp.GetString());

        Assert.True(result[0].Payload.TryGetProperty("payload", out var payloadProp));
        Assert.True(payloadProp.TryGetProperty("executorName", out var executorNameProp));
        Assert.Equal("SqlParserMafExecutor", executorNameProp.GetString());

        Assert.True(payloadProp.TryGetProperty("progressPercent", out var progressProp));
        Assert.Equal(16, progressProp.GetInt32());
    }

    [Fact]
    public void Map_ShouldMapExecutorCompletedEvent_WithProgressCalculation()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.ExecutorCompleted,
            new { executorName = "SqlParserMafExecutor", durationMs = 1500 });

        _progressCalculatorMock
            .Setup(x => x.GetProgressPercent("SqlAnalysis", "SqlParserMafExecutor", "Completed"))
            .Returns(33);

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("executor.completed", eventTypeProp.GetString());

        Assert.True(result[0].Payload.TryGetProperty("payload", out var payloadProp));
        Assert.True(payloadProp.TryGetProperty("progressPercent", out var progressProp));
        Assert.Equal(33, progressProp.GetInt32());
    }

    [Fact]
    public void Map_ShouldMapWorkflowWaitingReviewEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.WorkflowWaitingReview,
            new { reviewId = Guid.NewGuid(), message = "Waiting for human review" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("review.requested", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldMapWorkflowCompletedEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.WorkflowCompleted,
            new { message = "Workflow completed successfully" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("workflow.completed", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldMapWorkflowFailedEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.WorkflowFailed,
            new { error = "Database connection failed" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("workflow.failed", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldMapWorkflowCancelledEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.WorkflowCancelled,
            new { message = "Workflow cancelled by user" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("workflow.cancelled", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldMapCheckpointSavedEvent()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(
            sessionId,
            WorkflowEventType.CheckpointSaved,
            new { checkpointRef = "checkpoint_123" });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Single(result);
        Assert.True(result[0].Payload.TryGetProperty("eventType", out var eventTypeProp));
        Assert.Equal("checkpoint.saved", eventTypeProp.GetString());
    }

    [Fact]
    public void Map_ShouldFilterOutUnknownEventTypes()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var mafEvent = CreateMafEvent(sessionId, (WorkflowEventType)999, new { });

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", new[] { mafEvent });

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Map_ShouldHandleMultipleEvents()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var events = new[]
        {
            CreateMafEvent(sessionId, WorkflowEventType.WorkflowStarted, new { }),
            CreateMafEvent(sessionId, WorkflowEventType.ExecutorStarted, new { executorName = "SqlParserMafExecutor" }),
            CreateMafEvent(sessionId, WorkflowEventType.ExecutorCompleted, new { executorName = "SqlParserMafExecutor" }),
            CreateMafEvent(sessionId, WorkflowEventType.WorkflowCompleted, new { })
        };

        _progressCalculatorMock
            .Setup(x => x.GetProgressPercent(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(50);

        // Act
        var result = _adapter.Map(sessionId, "SqlAnalysis", events);

        // Assert
        Assert.Equal(4, result.Count);
    }

    private static WorkflowEventRecord CreateMafEvent(Guid sessionId, WorkflowEventType eventType, object payload)
    {
        return new WorkflowEventRecord(
            Sequence: 1,
            EventType: eventType,
            SessionId: sessionId,
            WorkflowType: "SqlAnalysis",
            Timestamp: DateTimeOffset.UtcNow,
            Payload: JsonSerializer.SerializeToElement(payload));
    }
}
