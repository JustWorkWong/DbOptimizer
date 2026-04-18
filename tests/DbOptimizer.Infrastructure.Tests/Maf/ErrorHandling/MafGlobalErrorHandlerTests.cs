using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;

namespace DbOptimizer.Infrastructure.Tests.Maf.ErrorHandling;

public sealed class MafGlobalErrorHandlerTests : IAsyncLifetime
{
    private DbOptimizerDbContext? _dbContext;
    private IDbContextFactory<DbOptimizerDbContext> _dbContextFactory = null!;
    private readonly Mock<IMafRunStateStore> _mockRunStateStore;
    private readonly Mock<ILogger<MafGlobalErrorHandler>> _mockLogger;
    private readonly MafGlobalErrorHandler _errorHandler;

    public MafGlobalErrorHandlerTests()
    {
        _mockRunStateStore = new Mock<IMafRunStateStore>();
        _mockLogger = new Mock<ILogger<MafGlobalErrorHandler>>();

        // 延迟初始化 _errorHandler，在 InitializeAsync 中创建
        _errorHandler = null!;
    }

    public async Task InitializeAsync()
    {
        // 使用内存数据库
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DbOptimizerDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        // 创建 DbContextFactory，每次调用返回新的 DbContext 实例
        var mockFactory = new Mock<IDbContextFactory<DbOptimizerDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DbOptimizerDbContext(options));
        _dbContextFactory = mockFactory.Object;

        // 使用反射设置 _errorHandler
        var errorHandlerField = typeof(MafGlobalErrorHandlerTests)
            .GetField("_errorHandler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        errorHandlerField!.SetValue(this, new MafGlobalErrorHandler(
            _dbContextFactory,
            _mockRunStateStore.Object,
            _mockLogger.Object));
    }

    public async Task DisposeAsync()
    {
        if (_dbContext is not null)
        {
            await _dbContext.Database.EnsureDeletedAsync();
            await _dbContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandleWorkflowErrorAsync_UpdatesSessionStatusToFailed()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            State = "{}",
            EngineType = "maf",
            SourceType = "manual",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WorkflowSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var exception = new TimeoutException("Operation timed out");

        // Act
        await _errorHandler.HandleWorkflowErrorAsync(
            sessionId,
            exception,
            currentStep: "test_step",
            CancellationToken.None);

        // Assert - 需要重新查询，因为 MafGlobalErrorHandler 使用了新的 DbContext 实例
        await using var verifyContext = await _dbContextFactory.CreateDbContextAsync();
        var updatedSession = await verifyContext.WorkflowSessions.FindAsync(sessionId);
        updatedSession.Should().NotBeNull();
        updatedSession!.Status.Should().Be("failed");
        updatedSession.ErrorMessage.Should().NotBeNullOrEmpty();
        updatedSession.CompletedAt.Should().NotBeNull();
        updatedSession.EngineState.Should().Contain("TimeoutException");
    }

    [Fact]
    public async Task HandleWorkflowErrorAsync_SavesCheckpointBeforeFailure()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            State = "{}",
            EngineType = "maf",
            SourceType = "manual",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WorkflowSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: "test_run",
            CheckpointRef: "checkpoint_1",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

        _mockRunStateStore
            .Setup(s => s.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        var exception = new Exception("Test error");

        // Act
        await _errorHandler.HandleWorkflowErrorAsync(
            sessionId,
            exception,
            currentStep: "test_step",
            CancellationToken.None);

        // Assert
        _mockRunStateStore.Verify(
            s => s.SaveAsync(
                sessionId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleWorkflowErrorAsync_ClassifiesErrorCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            State = "{}",
            EngineType = "maf",
            SourceType = "manual",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WorkflowSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var exception = new ArgumentException("Invalid argument");

        // Act
        await _errorHandler.HandleWorkflowErrorAsync(
            sessionId,
            exception,
            currentStep: "validation",
            CancellationToken.None);

        // Assert - 需要重新查询，因为 MafGlobalErrorHandler 使用了新的 DbContext 实例
        await using var verifyContext = await _dbContextFactory.CreateDbContextAsync();
        var updatedSession = await verifyContext.WorkflowSessions.FindAsync(sessionId);
        updatedSession.Should().NotBeNull();
        updatedSession!.EngineState.Should().Contain("ValidationError");
        updatedSession.ErrorMessage.Should().Contain("验证失败");
    }

    [Fact]
    public async Task HandleExecutorErrorAsync_LogsErrorCorrectly()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var executorName = "TestExecutor";
        var exception = new TimeoutException("Executor timeout");

        // Act
        await _errorHandler.HandleExecutorErrorAsync(
            sessionId,
            executorName,
            exception,
            CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Executor execution failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
