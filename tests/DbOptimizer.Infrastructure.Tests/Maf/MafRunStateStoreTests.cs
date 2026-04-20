using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using System.Text.Json;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;

namespace DbOptimizer.Infrastructure.Tests.Maf;

/// <summary>
/// MafRunStateStore 单元测试
/// 验证 PostgreSQL + Redis 双层存储正确性
/// </summary>
public sealed class MafRunStateStoreTests : IDisposable
{
    private readonly DbContextOptions<DbOptimizerDbContext> _dbOptions;
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _redisDatabaseMock;
    private readonly Mock<IBatch> _redisBatchMock;
    private readonly Mock<IDbContextFactory<DbOptimizerDbContext>> _dbContextFactoryMock;
    private readonly MafRunStateStore _store;

    public MafRunStateStoreTests()
    {
        // 使用 In-Memory 数据库
        _dbOptions = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Mock Redis
        _redisDatabaseMock = new Mock<IDatabase>();
        _redisBatchMock = new Mock<IBatch>();
        _redisMock = new Mock<IConnectionMultiplexer>();
        _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_redisDatabaseMock.Object);
        _redisDatabaseMock.Setup(x => x.CreateBatch(It.IsAny<object?>()))
            .Returns(_redisBatchMock.Object);

        // Mock DbContextFactory - 每次调用都创建新的 DbContext
        _dbContextFactoryMock = new Mock<IDbContextFactory<DbOptimizerDbContext>>();
        _dbContextFactoryMock.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new DbOptimizerDbContext(_dbOptions));

        var loggerMock = new Mock<ILogger<MafRunStateStore>>();

        _store = new MafRunStateStore(
            _dbContextFactoryMock.Object,
            _redisMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task SaveAsync_PersistsToDatabase()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        var runId = "run_123";
        var checkpointRef = "checkpoint_ref_123";
        var engineState = "{\"step\":1}";

        // Act
        await _store.SaveAsync(sessionId, runId, checkpointRef, engineState);

        // Assert
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var savedSession = await dbContext.WorkflowSessions.FindAsync(sessionId);
            Assert.NotNull(savedSession);
            Assert.Equal(runId, savedSession.EngineRunId);
            Assert.Equal(checkpointRef, savedSession.EngineCheckpointRef);
            Assert.Equal(engineState, savedSession.EngineState);
        }
    }

    [Fact]
    public async Task SaveAsync_SyncsToRedis()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        var runId = "run_123";
        var checkpointRef = "checkpoint_ref_123";
        var engineState = "{\"step\":1}";
        RedisValue cachedValue = RedisValue.Null;

        _redisBatchMock.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>(
                (_, value, _, _, _, _) => cachedValue = value)
            .ReturnsAsync(true);

        // Act
        await _store.SaveAsync(sessionId, runId, checkpointRef, engineState);

        // Assert
        _redisBatchMock.Verify(x => x.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(sessionId.ToString("N"))),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromHours(24)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
        _redisBatchMock.Verify(x => x.Execute(), Times.Once);
        Assert.True(cachedValue.HasValue);

        var cachedState = JsonSerializer.Deserialize<MafRunState>(cachedValue.ToString());
        Assert.NotNull(cachedState);
        Assert.Equal(sessionId, cachedState.SessionId);
        Assert.Equal(runId, cachedState.RunId);
        Assert.Equal(checkpointRef, cachedState.CheckpointRef);
        Assert.Equal(engineState, cachedState.EngineState);
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenSessionNotFound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var runId = "run_123";
        var checkpointRef = "checkpoint_ref_123";
        var engineState = "{\"step\":1}";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.SaveAsync(sessionId, runId, checkpointRef, engineState));
    }

    [Fact]
    public async Task GetAsync_RetrievesFromRedisFirst()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var expectedState = new MafRunState(
            sessionId,
            "run_123",
            "checkpoint_ref_123",
            "{\"step\":1}",
            DateTime.UtcNow,
            DateTime.UtcNow);

        var serialized = JsonSerializer.Serialize(expectedState);
        _redisDatabaseMock.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(serialized));

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("run_123", result.RunId);
        Assert.Equal("checkpoint_ref_123", result.CheckpointRef);
    }

    [Fact]
    public async Task GetAsync_FallsBackToPostgreSQLOnRedisMiss()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            EngineRunId = "run_123",
            EngineCheckpointRef = "checkpoint_ref_123",
            EngineState = "{\"step\":1}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Redis miss
        _redisDatabaseMock.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("run_123", result.RunId);
        Assert.Equal("checkpoint_ref_123", result.CheckpointRef);
        _redisBatchMock.Verify(x => x.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(sessionId.ToString("N"))),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(ttl => ttl == TimeSpan.FromHours(24)),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
        _redisBatchMock.Verify(x => x.Execute(), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNotFound()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _redisDatabaseMock.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenEngineRunIdIsNull()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            EngineRunId = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        _redisDatabaseMock.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromRedis()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _redisDatabaseMock.Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _store.DeleteAsync(sessionId);

        // Assert
        _redisDatabaseMock.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(sessionId.ToString("N"))),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ClearsCheckpointFieldsInPostgreSQL()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            EngineRunId = "run_123",
            EngineCheckpointRef = "checkpoint_ref_123",
            EngineState = "{\"step\":1}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        _redisDatabaseMock.Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _store.DeleteAsync(sessionId);

        // Assert
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var updatedSession = await dbContext.WorkflowSessions.FindAsync(sessionId);
            Assert.NotNull(updatedSession);
            Assert.Null(updatedSession.EngineRunId);
            Assert.Null(updatedSession.EngineCheckpointRef);
            Assert.Null(updatedSession.EngineState);
        }
    }

    [Fact]
    public async Task DeleteAsync_HandlesNonExistentSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        _redisDatabaseMock.Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act (should not throw)
        await _store.DeleteAsync(sessionId);

        // Assert
        _redisDatabaseMock.Verify(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ContinuesWhenRedisFails()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        var runId = "run_123";
        var checkpointRef = "checkpoint_ref_123";
        var engineState = "{\"step\":1}";

        // Redis 写入失败
        _redisBatchMock.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act (should not throw)
        await _store.SaveAsync(sessionId, runId, checkpointRef, engineState);

        // Assert - PostgreSQL 应该成功保存
        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            var savedSession = await dbContext.WorkflowSessions.FindAsync(sessionId);
            Assert.NotNull(savedSession);
            Assert.Equal(runId, savedSession.EngineRunId);
        }
    }

    [Fact]
    public async Task GetAsync_FallsBackWhenRedisThrows()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var session = new WorkflowSessionEntity
        {
            SessionId = sessionId,
            WorkflowType = "sql_analysis",
            Status = "running",
            EngineRunId = "run_123",
            EngineCheckpointRef = "checkpoint_ref_123",
            EngineState = "{\"step\":1}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using (var dbContext = new DbOptimizerDbContext(_dbOptions))
        {
            dbContext.WorkflowSessions.Add(session);
            await dbContext.SaveChangesAsync();
        }

        // Redis 读取失败
        _redisDatabaseMock.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Redis connection failed"));

        // Act
        var result = await _store.GetAsync(sessionId);

        // Assert - 应该从 PostgreSQL 读取成功
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("run_123", result.RunId);
    }

    public void Dispose()
    {
        // DbContext instances are created per-call via factory, no cleanup needed
    }
}
