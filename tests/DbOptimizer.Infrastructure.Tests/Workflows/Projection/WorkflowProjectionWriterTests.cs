using System.Text.Json;
using System.Text.Json.Serialization;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DbOptimizer.Infrastructure.Tests.Workflows.Projection;

public sealed class WorkflowProjectionWriterTests : IAsyncLifetime
{
    private const string DatabaseName = "WorkflowProjectionWriterTests";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly DbOptimizerDbContext _dbContext;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory;
    private readonly Mock<IWorkflowResultSerializer> _resultSerializerMock;
    private readonly Mock<ILogger<WorkflowProjectionWriter>> _loggerMock;
    private readonly WorkflowProjectionWriter _writer;

    public WorkflowProjectionWriterTests()
    {
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(DatabaseName)
            .Options;

        _dbContext = new DbOptimizerDbContext(options);

        // 创建一个真实的 factory，每次返回新的 context 实例但共享同一个 InMemory 数据库
        _dbContextFactory = new TestDbContextFactory(options);

        _resultSerializerMock = new Mock<IWorkflowResultSerializer>();
        _loggerMock = new Mock<ILogger<WorkflowProjectionWriter>>();

        _writer = new WorkflowProjectionWriter(
            _dbContextFactory,
            _resultSerializerMock.Object,
            _loggerMock.Object);
    }

    public async Task InitializeAsync()
    {
        // 清理所有数据
        _dbContext.WorkflowSessions.RemoveRange(_dbContext.WorkflowSessions);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    // 测试用的 DbContextFactory 实现
    private class TestDbContextFactory : IDbContextFactory<DbOptimizerDbContext>
    {
        private readonly DbContextOptions<DbOptimizerDbContext> _options;

        public TestDbContextFactory(DbContextOptions<DbOptimizerDbContext> options)
        {
            _options = options;
        }

        public DbOptimizerDbContext CreateDbContext()
        {
            return new DbOptimizerDbContext(_options);
        }

        public Task<DbOptimizerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DbOptimizerDbContext(_options));
        }
    }

    [Fact]
    public async Task ApplyEventAsync_WorkflowStarted_ShouldUpdateStatusToRunning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Pending");

        var workflowEvent = CreateWorkflowEvent(sessionId, WorkflowEventType.WorkflowStarted, new { });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert - 使用新的 context 实例读取
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Running", session.Status);
        Assert.True((DateTimeOffset.UtcNow - session.UpdatedAt).TotalSeconds < 5);
    }

    [Fact]
    public async Task ApplyEventAsync_ExecutorStarted_ShouldUpdateStatusToRunning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Pending");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.ExecutorStarted,
            new { executorName = "SqlParserMafExecutor" });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Running", session.Status);
    }

    [Fact]
    public async Task ApplyEventAsync_ExecutorCompleted_ShouldUpdateStatusToRunning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.ExecutorCompleted,
            new { executorName = "SqlParserMafExecutor" });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Running", session.Status);
    }

    [Fact]
    public async Task ApplyEventAsync_WorkflowWaitingReview_ShouldUpdateStatusToWaitingReview()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.WorkflowWaitingReview,
            new { reviewId = Guid.NewGuid() });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("WaitingForReview", session.Status);
    }

    [Fact]
    public async Task ApplyEventAsync_WorkflowCompleted_ShouldUpdateStatusAndCompletedAt()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.WorkflowCompleted,
            new { message = "Completed successfully" });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Completed", session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.True((DateTimeOffset.UtcNow - session.CompletedAt.Value).TotalSeconds < 5);
    }

    [Fact]
    public async Task ApplyEventAsync_WorkflowFailed_ShouldUpdateStatusAndCompletedAt()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.WorkflowFailed,
            new { error = "Database connection failed" });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Failed", session.Status);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task ApplyEventAsync_WorkflowCancelled_ShouldUpdateStatusAndCompletedAt()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var workflowEvent = CreateWorkflowEvent(
            sessionId,
            WorkflowEventType.WorkflowCancelled,
            new { reason = "User cancelled" });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Cancelled", session.Status);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task ApplyEventAsync_SessionNotFound_ShouldLogWarning()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var workflowEvent = CreateWorkflowEvent(sessionId, WorkflowEventType.WorkflowStarted, new { });

        // Act
        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Workflow session not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyEventAsync_MultipleEvents_ShouldApplyInOrder()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Pending");

        var events = new[]
        {
            CreateWorkflowEvent(sessionId, WorkflowEventType.WorkflowStarted, new { }),
            CreateWorkflowEvent(sessionId, WorkflowEventType.ExecutorStarted, new { executorName = "SqlParserMafExecutor" }),
            CreateWorkflowEvent(sessionId, WorkflowEventType.ExecutorCompleted, new { executorName = "SqlParserMafExecutor" }),
            CreateWorkflowEvent(sessionId, WorkflowEventType.WorkflowCompleted, new { })
        };

        // Act
        foreach (var evt in events)
        {
            await _writer.ApplyEventAsync(sessionId, evt);
        }

        // Assert
        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("Completed", session.Status);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task ApplyEventAsync_PersistsTimelineIntoSessionState()
    {
        var sessionId = Guid.NewGuid();
        await CreateTestSessionAsync(sessionId, "Running");

        var startedAt = DateTimeOffset.UtcNow;
        var workflowEvent = new WorkflowEventRecord(
            Sequence: 7,
            EventType: WorkflowEventType.ExecutorCompleted,
            SessionId: sessionId,
            WorkflowType: "SqlAnalysis",
            Timestamp: startedAt,
            Payload: JsonSerializer.SerializeToElement(new { executorName = "SqlParserMafExecutor" }));

        await _writer.ApplyEventAsync(sessionId, workflowEvent);

        await using var assertContext = await _dbContextFactory.CreateDbContextAsync();
        var session = await assertContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);

        var checkpoint = JsonSerializer.Deserialize<WorkflowCheckpoint>(session.State, SerializerOptions);
        Assert.NotNull(checkpoint);

        var persistedEvents = WorkflowTimeline.GetEvents(checkpoint);
        Assert.Single(persistedEvents);
        Assert.Equal(7, persistedEvents[0].Sequence);
        Assert.Equal(WorkflowEventType.ExecutorCompleted, persistedEvents[0].EventType);
        Assert.Equal("SqlParserMafExecutor", persistedEvents[0].Payload.GetProperty("executorName").GetString());
    }

    private async Task CreateTestSessionAsync(Guid sessionId, string status)
    {
        var checkpointStatus = Enum.TryParse<WorkflowCheckpointStatus>(status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : WorkflowCheckpointStatus.Running;
        var now = DateTimeOffset.UtcNow;
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "SqlAnalysis",
            Status = status,
            State = JsonSerializer.Serialize(
                new WorkflowCheckpoint
                {
                    SessionId = sessionId,
                    WorkflowType = "SqlAnalysis",
                    Status = checkpointStatus,
                    CurrentExecutor = string.Empty,
                    CheckpointVersion = 1,
                    Context = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
                    CompletedExecutors = Array.Empty<string>(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastCheckpointAt = now
                },
                SerializerOptions),
            EngineType = "maf",
            SourceType = "manual",
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.WorkflowSessions.Add(session);
        await _dbContext.SaveChangesAsync();
    }

    private static WorkflowEventRecord CreateWorkflowEvent(
        Guid sessionId,
        WorkflowEventType eventType,
        object payload)
    {
        return new WorkflowEventRecord(
            Sequence: 1,
            EventType: eventType,
            SessionId: sessionId,
            WorkflowType: "SqlAnalysis",
            Timestamp: DateTimeOffset.UtcNow,
            Payload: JsonSerializer.SerializeToElement(payload));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
