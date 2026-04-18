using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Tests.Workflows;

/// <summary>
/// WorkflowApplicationService 单元测试
/// 验证 Application Service 正确委托给 MAF Runtime
/// </summary>
public sealed class WorkflowApplicationServiceTests
{
    private readonly Mock<IMafWorkflowRuntime> _mafRuntimeMock;
    private readonly Mock<IDbContextFactory<DbOptimizerDbContext>> _dbContextFactoryMock;
    private readonly Mock<IWorkflowResultSerializer> _serializerMock;
    private readonly Mock<ILogger<WorkflowApplicationService>> _loggerMock;
    private readonly WorkflowApplicationService _service;

    public WorkflowApplicationServiceTests()
    {
        _mafRuntimeMock = new Mock<IMafWorkflowRuntime>();
        _dbContextFactoryMock = new Mock<IDbContextFactory<DbOptimizerDbContext>>();
        _serializerMock = new Mock<IWorkflowResultSerializer>();
        _loggerMock = new Mock<ILogger<WorkflowApplicationService>>();

        _service = new WorkflowApplicationService(
            _mafRuntimeMock.Object,
            _dbContextFactoryMock.Object,
            _serializerMock.Object,
            _loggerMock.Object);
    }

    #region StartSqlAnalysisAsync Tests

    [Fact]
    public async Task StartSqlAnalysisAsync_CallsMafRuntime_WithCorrectCommand()
    {
        // Arrange
        var request = new CreateSqlAnalysisWorkflowRequest
        {
            SqlText = "SELECT * FROM users WHERE id = 1",
            DatabaseEngine = "mysql",
            DatabaseId = "prod-db-01"
        };

        var expectedResponse = new DbOptimizer.Infrastructure.Maf.Runtime.WorkflowStartResponse(
            SessionId: Guid.NewGuid(),
            RunId: "run-123",
            Status: "running");

        _mafRuntimeMock.Setup(x => x.StartSqlAnalysisAsync(
                It.IsAny<SqlAnalysisWorkflowCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.StartSqlAnalysisAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(expectedResponse.SessionId, response.SessionId);
        Assert.Equal("SqlAnalysis", response.WorkflowType);
        Assert.Equal("maf", response.EngineType);
        Assert.Equal("running", response.Status);

        // 验证调用了 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.StartSqlAnalysisAsync(
                It.Is<SqlAnalysisWorkflowCommand>(cmd =>
                    cmd.SqlText == request.SqlText.Trim() &&
                    cmd.DatabaseType == "mysql"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_ThrowsException_WhenSqlTextIsEmpty()
    {
        // Arrange
        var request = new CreateSqlAnalysisWorkflowRequest
        {
            SqlText = "",
            DatabaseEngine = "mysql",
            DatabaseId = "prod-db-01"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.StartSqlAnalysisAsync(request));

        // 验证未调用 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.StartSqlAnalysisAsync(
                It.IsAny<SqlAnalysisWorkflowCommand>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StartSqlAnalysisAsync_ThrowsException_WhenDatabaseIdIsEmpty()
    {
        // Arrange
        var request = new CreateSqlAnalysisWorkflowRequest
        {
            SqlText = "SELECT 1",
            DatabaseEngine = "mysql",
            DatabaseId = ""
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.StartSqlAnalysisAsync(request));
    }

    #endregion

    #region StartDbConfigOptimizationAsync Tests

    [Fact]
    public async Task StartDbConfigOptimizationAsync_CallsMafRuntime_WithCorrectCommand()
    {
        // Arrange
        var request = new CreateDbConfigOptimizationWorkflowRequest
        {
            DatabaseId = "prod-db-01",
            DatabaseType = "postgresql"
        };

        var expectedResponse = new DbOptimizer.Infrastructure.Maf.Runtime.WorkflowStartResponse(
            SessionId: Guid.NewGuid(),
            RunId: "run-456",
            Status: "running");

        _mafRuntimeMock.Setup(x => x.StartDbConfigOptimizationAsync(
                It.IsAny<DbConfigWorkflowCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.StartDbConfigOptimizationAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(expectedResponse.SessionId, response.SessionId);
        Assert.Equal("DbConfigOptimization", response.WorkflowType);
        Assert.Equal("maf", response.EngineType);
        Assert.Equal("running", response.Status);

        // 验证调用了 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.StartDbConfigOptimizationAsync(
                It.Is<DbConfigWorkflowCommand>(cmd =>
                    cmd.DatabaseId == request.DatabaseId.Trim() &&
                    cmd.DatabaseType == "postgresql"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartDbConfigOptimizationAsync_ThrowsException_WhenDatabaseIdIsEmpty()
    {
        // Arrange
        var request = new CreateDbConfigOptimizationWorkflowRequest
        {
            DatabaseId = "",
            DatabaseType = "postgresql"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.StartDbConfigOptimizationAsync(request));

        // 验证未调用 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.StartDbConfigOptimizationAsync(
                It.IsAny<DbConfigWorkflowCommand>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ResumeAsync Tests

    [Fact]
    public async Task ResumeAsync_CallsMafRuntime()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedResponse = new DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse(
            SessionId: sessionId,
            Status: "running");

        _mafRuntimeMock.Setup(x => x.ResumeAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.ResumeAsync(sessionId);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("running", response.Status);

        // 验证调用了 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.ResumeAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region CancelAsync Tests

    [Fact]
    public async Task CancelAsync_CallsMafRuntime()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedResponse = new DbOptimizer.Infrastructure.Maf.Runtime.WorkflowCancelResponse(
            SessionId: sessionId,
            Status: "cancelled");

        _mafRuntimeMock.Setup(x => x.CancelAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _service.CancelAsync(sessionId);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("cancelled", response.Status);

        // 验证调用了 MAF Runtime
        _mafRuntimeMock.Verify(
            x => x.CancelAsync(sessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
