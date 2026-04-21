using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.Reflection;
using DbOptimizer.Infrastructure.Workflows;

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
    private readonly Mock<IWorkflowEventPublisher> _eventPublisherMock;
    private readonly IWorkflowExecutionConcurrencyGate _concurrencyGate;
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
        _eventPublisherMock = new Mock<IWorkflowEventPublisher>();
        _concurrencyGate = new WorkflowExecutionConcurrencyGate(
            new WorkflowExecutionOptions
            {
                MaxConcurrentRuns = 10,
                MaxConcurrentSqlRuns = 10,
                MaxConcurrentConfigRuns = 10
            },
            NullLogger<WorkflowExecutionConcurrencyGate>.Instance);

        var options = new MafWorkflowRuntimeOptions();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var loggerMock = new Mock<ILogger<MafWorkflowRuntime>>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        _runtime = new MafWorkflowRuntime(
            _workflowFactoryMock.Object,
            _runStateStoreMock.Object,
            _dbContextFactoryMock.Object,
            CheckpointManager.CreateJson(new MafJsonCheckpointStore(
                _runStateStoreMock.Object,
                Mock.Of<ILogger<MafJsonCheckpointStore>>())),
            options,
            loggerFactoryMock.Object,
            _eventPublisherMock.Object,
            _concurrencyGate);
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
            .Returns(CreateSqlWorkflow());

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
            .Returns(CreateSqlWorkflow());

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
            Times.AtLeastOnce);
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
            .Returns(CreateSqlWorkflow());

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
            .Returns(CreateSqlWorkflow());

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
            .Returns(CreateSqlWorkflow());

        // Act
        await _runtime.StartSqlAnalysisAsync(command);

        // Assert
        _workflowFactoryMock.Verify(x => x.BuildSqlAnalysisWorkflow(), Times.Once);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_WhenConcurrencyLimitReached_ThrowsWorkflowExecutionThrottledException()
    {
        var limitedGate = new WorkflowExecutionConcurrencyGate(
            new WorkflowExecutionOptions
            {
                MaxConcurrentRuns = 1,
                MaxConcurrentSqlRuns = 1,
                MaxConcurrentConfigRuns = 1
            },
            NullLogger<WorkflowExecutionConcurrencyGate>.Instance);

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        var loggerMock = new Mock<ILogger<MafWorkflowRuntime>>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        var runtime = new MafWorkflowRuntime(
            _workflowFactoryMock.Object,
            _runStateStoreMock.Object,
            _dbContextFactoryMock.Object,
            CheckpointManager.CreateJson(new MafJsonCheckpointStore(
                _runStateStoreMock.Object,
                Mock.Of<ILogger<MafJsonCheckpointStore>>())),
            new MafWorkflowRuntimeOptions(),
            loggerFactoryMock.Object,
            _eventPublisherMock.Object,
            limitedGate);

        using var lease = limitedGate.Acquire("sql_analysis");

        var sessionId = Guid.NewGuid();
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: "SELECT * FROM users",
            DatabaseType: "mysql",
            SchemaName: null);

        var exception = await Assert.ThrowsAsync<WorkflowExecutionThrottledException>(
            () => runtime.StartSqlAnalysisAsync(command));

        Assert.Equal("sql_analysis", exception.WorkflowType);
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
            .Returns(CreateConfigWorkflow());

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
            .Returns(CreateConfigWorkflow());

        // Act
        await _runtime.StartDbConfigOptimizationAsync(command);

        // Assert
        _runStateStoreMock.Verify(
            x => x.SaveAsync(
                sessionId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
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
            .Returns(CreateConfigWorkflow());

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

    [Fact]
    public async Task ResumeAsync_WithValidCheckpoint_ResumesWorkflow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var workflow = CreateSuspendedWorkflow();
        var runState = await SeedPendingCheckpointAsync(sessionId, workflow, "Approve SQL rewrite?");

        // 创建 suspended session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "suspended",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runState.RunId,
                EngineCheckpointRef = runState.CheckpointRef,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock workflow factory
        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns(workflow);

        // Act
        var response = await _runtime.ResumeAsync(sessionId);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("WaitingForReview", response.Status);

        // 验证 session 状态已更新
        await using var verifyContext = new DbOptimizerDbContext(_dbOptions);
        var updatedSession = await verifyContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(updatedSession);
        Assert.Equal("WaitingForReview", updatedSession.Status);
    }

    [Fact]
    public async Task ResumeSqlWorkflowAsync_WhenWorkflowEmitsOutputAndRunEndsIdle_UpdatesSessionToCompleted()
    {
        var sessionId = Guid.NewGuid();
        var workflow = CreateBidirectionalSqlReviewWorkflow();
        var runState = await SeedPendingCheckpointAsync(
            sessionId,
            workflow,
            new DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationDraftReadyMessage(
                sessionId,
                CreateWorkflowEnvelope("Draft result")));

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = WorkflowSessionStatus.WaitingForReview,
                State = "{}",
                EngineType = "maf",
                EngineRunId = runState.RunId,
                EngineCheckpointRef = runState.CheckpointRef,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns(workflow);

        var response = await _runtime.ResumeSqlWorkflowAsync(
            sessionId,
            ExternalRequest.Create(
                    MafReviewPorts.SqlReview,
                    new DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewRequestMessage(
                        sessionId,
                        Guid.NewGuid(),
                        CreateWorkflowEnvelope("Draft result")),
                    Guid.NewGuid().ToString("N"))
                .CreateResponse(new DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewResponseMessage(
                    sessionId,
                    Guid.NewGuid(),
                    "approve",
                    "approved",
                    new Dictionary<string, JsonElement>(),
                    DateTimeOffset.UtcNow)));

        Assert.Equal("completed", response.Status);

        await using var verifyContext = new DbOptimizerDbContext(_dbOptions);
        var updatedSession = await verifyContext.WorkflowSessions.FindAsync(sessionId);
        Assert.NotNull(updatedSession);
        Assert.Equal(WorkflowSessionStatus.Completed, updatedSession!.Status);
        Assert.NotNull(updatedSession.CompletedAt);
    }

    [Fact]
    public async Task ResumeAsync_WithNonExistentSession_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.ResumeAsync(sessionId));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task ResumeAsync_WithNonSuspendedSession_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // 创建 running session（非 suspended）
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "running",
                State = "{}",
                EngineType = "maf",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.ResumeAsync(sessionId));

        Assert.Contains("not waiting for review", exception.Message);
    }

    [Fact]
    public async Task ResumeAsync_WithMissingCheckpoint_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // 创建 suspended session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "suspended",
                State = "{}",
                EngineType = "maf",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock checkpoint 不存在
        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MafRunState?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.ResumeAsync(sessionId));

        Assert.Contains("Checkpoint", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public async Task ResumeAsync_WithDbConfigWorkflow_BuildsCorrectWorkflow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var workflow = CreateSuspendedWorkflow();
        var runState = await SeedPendingCheckpointAsync(sessionId, workflow, "Approve config tuning?");

        // 创建 suspended db_config session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "db_config_optimization",
                Status = "suspended",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runState.RunId,
                EngineCheckpointRef = runState.CheckpointRef,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock workflow factory
        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Returns(workflow);

        // Act
        await _runtime.ResumeAsync(sessionId);

        // Assert
        _workflowFactoryMock.Verify(x => x.BuildDbConfigWorkflow(), Times.Once);
    }

    [Fact]
    public async Task ResumeAsync_WithInvalidWorkflowType_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // 创建 suspended session with invalid workflow type
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "invalid_workflow_type",
                Status = "suspended",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock run state
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"review-gate-{sessionId}",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.ResumeAsync(sessionId));

        Assert.Contains("Unknown workflow type", exception.Message);
    }

    [Fact]
    public async Task CancelAsync_SuccessfullyCancelsWorkflow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // 创建 running session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "running",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock run state
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"checkpoint-{sessionId}",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Mock workflow factory
        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        var response = await _runtime.CancelAsync(sessionId);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("cancelled", response.Status);

        // 验证 session 状态已更新
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = await dbContext.WorkflowSessions.FindAsync(sessionId);
            Assert.NotNull(session);
            Assert.Equal("Cancelled", session.Status);
            Assert.NotNull(session.CompletedAt);
        }

        // 验证 checkpoint 已删除
        _runStateStoreMock.Verify(x => x.DeleteAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WithRunStateNotFound_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Mock run state not found
        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MafRunState?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.CancelAsync(sessionId));

        Assert.Contains("Run state not found", exception.Message);
    }

    [Fact]
    public async Task CancelAsync_WithSessionNotFound_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // Mock run state exists
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"checkpoint-{sessionId}",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Session does not exist in database

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.CancelAsync(sessionId));

        Assert.Contains("Workflow session not found", exception.Message);
    }

    [Fact]
    public async Task CancelAsync_WithDbConfigWorkflow_BuildsCorrectWorkflow()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // 创建 running db_config session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "db_config_optimization",
                Status = "running",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock run state
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"checkpoint-{sessionId}",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Mock workflow factory
        _workflowFactoryMock.Setup(x => x.BuildDbConfigWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.CancelAsync(sessionId);

        // Assert
        _workflowFactoryMock.Verify(x => x.BuildDbConfigWorkflow(), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WithInvalidWorkflowType_ThrowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // 创建 running session with invalid workflow type
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "invalid_workflow_type",
                Status = "running",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock run state
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"checkpoint-{sessionId}",
            EngineState: "{}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.CancelAsync(sessionId));

        Assert.Contains("Unknown workflow type", exception.Message);
    }

    [Fact]
    public async Task CancelAsync_ClearsCheckpointData()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = $"maf_run_{Guid.NewGuid():N}";

        // 创建 running session
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var session = new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "running",
                State = "{}",
                EngineType = "maf",
                EngineRunId = runId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Mock run state
        var runState = new MafRunState(
            SessionId: sessionId,
            RunId: runId,
            CheckpointRef: $"checkpoint-{sessionId}",
            EngineState: "{\"some\":\"state\"}",
            CreatedAt: DateTime.UtcNow);

        _runStateStoreMock.Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runState);

        // Mock workflow factory
        _workflowFactoryMock.Setup(x => x.BuildSqlAnalysisWorkflow())
            .Returns((Workflow)null!);

        // Act
        await _runtime.CancelAsync(sessionId);

        // Assert - 验证 DeleteAsync 被调用
        _runStateStoreMock.Verify(
            x => x.DeleteAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Checkpoint data should be deleted when workflow is cancelled");
    }

    public void Dispose()
    {
        // DbContext instances are created per-call via factory, no cleanup needed
    }

    private static Workflow CreateSqlWorkflow()
    {
        var executor = new SqlNoOpExecutor();
        return new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
    }

    private static Workflow CreateConfigWorkflow()
    {
        var executor = new ConfigNoOpExecutor();
        return new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();
    }

    private async Task<MafRunState> SeedPendingCheckpointAsync<TInput>(Guid sessionId, Workflow workflow, TInput requestPayload)
        where TInput : notnull
    {
        MafRunState? state = null;

        _runStateStoreMock
            .Setup(x => x.GetAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => state);

        _runStateStoreMock
            .Setup(x => x.SaveAsync(
                sessionId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, string, CancellationToken>((sid, runId, checkpointRef, engineState, _) =>
            {
                state = new MafRunState(
                    sid,
                    runId,
                    checkpointRef,
                    engineState,
                    DateTime.UtcNow);
            })
            .Returns(Task.CompletedTask);

        var checkpointManager = CheckpointManager.CreateJson(new MafJsonCheckpointStore(
            _runStateStoreMock.Object,
            Mock.Of<ILogger<MafJsonCheckpointStore>>()));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            requestPayload,
            checkpointManager,
            sessionId: sessionId.ToString(),
            cts.Token);

        await foreach (var evt in run.WatchStreamAsync(cts.Token))
        {
            if (evt is RequestInfoEvent)
            {
                break;
            }
        }

        await WaitForStatusAsync(run, RunStatus.PendingRequests, cts.Token);

        if (state is null)
        {
            throw new InvalidOperationException("Failed to seed pending checkpoint for resume test.");
        }

        return state;
    }

    private static Workflow CreateSuspendedWorkflow()
    {
        var reviewPort = RequestPort.Create<string, bool>("review-port");
        var finalizeExecutor = new ReviewDecisionExecutor();

        return new WorkflowBuilder(reviewPort)
            .AddEdge(reviewPort, finalizeExecutor)
            .WithOutputFrom(finalizeExecutor)
            .Build();
    }

    private static Workflow CreateBidirectionalSqlReviewWorkflow()
    {
        var executor = new BidirectionalSqlReviewExecutor();

        return new WorkflowBuilder(executor)
            .AddEdge(executor, MafReviewPorts.SqlReview)
            .AddEdge(MafReviewPorts.SqlReview, executor)
            .WithOutputFrom(executor)
            .Build();
    }

    private static WorkflowResultEnvelope CreateWorkflowEnvelope(string summary)
    {
        return new WorkflowResultEnvelope
        {
            ResultType = "sql_optimization",
            DisplayName = "SQL Optimization Result",
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new { summary }),
            Metadata = JsonSerializer.SerializeToElement(new { requireHumanReview = true })
        };
    }

    private sealed class SqlNoOpExecutor : Executor<DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlAnalysisWorkflowCommand, string>
    {
        public SqlNoOpExecutor()
            : base("sql-noop")
        {
        }

        public override ValueTask<string> HandleAsync(
            DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlAnalysisWorkflowCommand message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult($"sql:{message.SessionId}");
        }
    }

    private sealed class ConfigNoOpExecutor : Executor<DbOptimizer.Infrastructure.Maf.DbConfig.DbConfigWorkflowCommand, string>
    {
        public ConfigNoOpExecutor()
            : base("config-noop")
        {
        }

        public override ValueTask<string> HandleAsync(
            DbOptimizer.Infrastructure.Maf.DbConfig.DbConfigWorkflowCommand message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult($"config:{message.SessionId}");
        }
    }

    private sealed class ReviewDecisionExecutor : Executor<bool, string>
    {
        public ReviewDecisionExecutor()
            : base("review-decision")
        {
        }

        public override ValueTask<string> HandleAsync(
            bool message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message ? "approved" : "rejected");
        }
    }

    [System.Obsolete]
    [SendsMessage(typeof(DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewRequestMessage))]
    private sealed class BidirectionalSqlReviewExecutor :
        ReflectingExecutor<BidirectionalSqlReviewExecutor>,
        IMessageHandler<DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationDraftReadyMessage>,
        IMessageHandler<DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewResponseMessage, DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationCompletedMessage>
    {
        public BidirectionalSqlReviewExecutor()
            : base("sql-review")
        {
        }

        public async ValueTask HandleAsync(
            DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationDraftReadyMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await context.SendMessageAsync(
                new DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewRequestMessage(
                    message.SessionId,
                    Guid.NewGuid(),
                    message.DraftResult),
                cancellationToken);
        }

        public ValueTask<DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationCompletedMessage> HandleAsync(
            DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewResponseMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(
                new DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlOptimizationCompletedMessage(
                    message.SessionId,
                    CreateWorkflowEnvelope("Approved result")));
        }
    }

    private static async Task WaitForStatusAsync(StreamingRun run, RunStatus expected, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await run.GetStatusAsync(cancellationToken) == expected)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}
