using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Tests.Maf;

/// <summary>
/// MafWorkflowRuntime 集成测试
/// 验证 workflow 启动、session 管理、state 持久化
/// </summary>
public sealed class MafWorkflowRuntimeTests : IDisposable
{
    private readonly DbContextOptions<DbOptimizerDbContext> _dbOptions;
    private readonly Mock<IDbContextFactory<DbOptimizerDbContext>> _dbContextFactoryMock;
    private readonly Mock<IMafWorkflowFactory> _workflowFactoryMock;
    private readonly Mock<IMafRunStateStore> _runStateStoreMock;
    private readonly MafWorkflowRuntime _runtime;

    public MafWorkflowRuntimeTests()
    {
        // 使用 In-Memory 数据库
        _dbOptions = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Mock DbContextFactory
        _dbContextFactoryMock = new Mock<IDbContextFactory<DbOptimizerDbContext>>();
        _dbContextFactoryMock.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DbOptimizerDbContext(_dbOptions));

        // Mock WorkflowFactory
        _workflowFactoryMock = new Mock<IMafWorkflowFactory>();

        // Mock RunStateStore
        _runStateStoreMock = new Mock<IMafRunStateStore>();

        var options = new MafWorkflowRuntimeOptions();
        var loggerMock = new Mock<ILogger<MafWorkflowRuntime>>();

        _runtime = new MafWorkflowRuntime(
            _workflowFactoryMock.Object,
            _runStateStoreMock.Object,
            _dbContextFactoryMock.Object,
            options,
            loggerMock.Object);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_CreatesWorkflowSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT * FROM users WHERE id = 1",
            DatabaseType: "mysql",
            SchemaName: null);

        // Mock workflow factory 返回 null（测试不依赖实际 workflow 执行）
        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        var response = await _runtime.StartSqlAnalysisAsync(command);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("running", response.Status);
        Assert.NotEmpty(response.RunId);

        // 验证 session 已创建
        await using var dbContext = new DbOptimizerDbContext(_dbOptions);
        var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("sql_analysis", session.WorkflowType);
        Assert.Equal("running", session.Status);
        Assert.Equal("maf", session.EngineType);
        Assert.Equal("manual", session.SourceType);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_SavesMafRunState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT * FROM users",
            DatabaseType: "postgresql",
            SchemaName: "public");

        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        var response = await _runtime.StartSqlAnalysisAsync(command);

        // Assert
        Assert.NotNull(response);

        // 验证 MAF run state 已保存
        _runStateStoreMock.Verify(
            x => x.SaveAsync(
                sessionId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_RecordsEngineTypeAsMaf()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT COUNT(*) FROM orders",
            DatabaseType: "mysql",
            SchemaName: null);

        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.StartSqlAnalysisAsync(command);

        // Assert
        await using var dbContext = new DbOptimizerDbContext(_dbOptions);
        var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("maf", session.EngineType);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_WithNullCommand_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _runtime.StartSqlAnalysisAsync(null!));
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_GeneratesUniqueRunId()
    {
        // Arrange
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var command1 = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId1,
            SqlText: "SELECT * FROM table1",
            DatabaseType: "mysql",
            SchemaName: null);

        var command2 = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId2,
            SqlText: "SELECT * FROM table2",
            DatabaseType: "postgresql",
            SchemaName: null);

        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        var response1 = await _runtime.StartSqlAnalysisAsync(command1);
        var response2 = await _runtime.StartSqlAnalysisAsync(command2);

        // Assert
        Assert.NotEqual(response1.RunId, response2.RunId);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_CallsWorkflowFactory()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT * FROM products",
            DatabaseType: "mysql",
            SchemaName: null);

        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.StartSqlAnalysisAsync(command);

        // Assert
        _workflowFactoryMock.Verify(x => x.BuildSqlAnalysisWorkflow(), Times.Once);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_OnFailure_CleansUpSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT * FROM users",
            DatabaseType: "mysql",
            SchemaName: null);

        // Mock workflow factory 抛出异常
        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Throws(new InvalidOperationException("Workflow build failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.StartSqlAnalysisAsync(command));

        // 验证 session 被标记为 failed
        await using var dbContext = new DbOptimizerDbContext(_dbOptions);
        var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("failed", session.Status);
        Assert.NotNull(session.ErrorMessage);
        Assert.NotNull(session.CompletedAt);
    }

    [Fact]
    public async Task StartDbConfigOptimizationAsync_CreatesWorkflowSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new DbConfigWorkflowCommand(
            SessionId: sessionId,
            DatabaseId: "test-db-001",
            DatabaseType: "mysql",
            AllowFallbackSnapshot: true,
            RequireHumanReview: false);

        // Mock workflow factory 返回 null（测试不依赖实际 workflow 执行）
        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Returns((Workflow)null!);

        // Act
        var response = await _runtime.StartDbConfigOptimizationAsync(command);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("running", response.Status);
        Assert.NotEmpty(response.RunId);

        // 验证 session 已创建
        await using var dbContext = new DbOptimizerDbContext(_dbOptions);
        var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("db_config_optimization", session.WorkflowType);
        Assert.Equal("running", session.Status);
    }

    [Fact]
    public async Task StartDbConfigOptimizationAsync_SavesRunState()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new DbConfigWorkflowCommand(
            SessionId: sessionId,
            DatabaseId: "test-db-002",
            DatabaseType: "postgresql",
            AllowFallbackSnapshot: false,
            RequireHumanReview: true);

        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.StartDbConfigOptimizationAsync(command);

        // Assert
        _runStateStoreMock.Verify(
            x => x.SaveAsync(
                sessionId,
                It.IsAny<string>(),
                string.Empty,
                "{}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartDbConfigOptimizationAsync_CallsWorkflowFactory()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new DbConfigWorkflowCommand(
            SessionId: sessionId,
            DatabaseId: "test-db-003",
            DatabaseType: "mysql");

        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.StartDbConfigOptimizationAsync(command);

        // Assert
        _workflowFactoryMock.Verify(x => x.BuildDbConfigWorkflow(), Times.Once);
    }

    [Fact]
    public async Task StartDbConfigOptimizationAsync_OnFailure_CleansUpSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var command = new DbConfigWorkflowCommand(
            SessionId: sessionId,
            DatabaseId: "test-db-004",
            DatabaseType: "mysql");

        // Mock workflow factory 抛出异常
        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Throws(new InvalidOperationException("Workflow build failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.StartDbConfigOptimizationAsync(command));

        // 验证 session 被标记为 failed
        await using var dbContext = new DbOptimizerDbContext(_dbOptions);
        var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal("failed", session.Status);
        Assert.NotNull(session.ErrorMessage);
        Assert.NotNull(session.CompletedAt);
    }

    public void Dispose()
    {
        // DbContext instances are created per-call via factory, no cleanup needed
    }
}
