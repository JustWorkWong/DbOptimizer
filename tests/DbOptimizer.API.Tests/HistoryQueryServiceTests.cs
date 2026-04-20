using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DbOptimizer.API.Api;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class HistoryQueryServiceTests
{
    [Fact]
    public async Task GetHistoryReplayAsync_MergesPersistedAndLiveEvents()
    {
        await using var harness = await HistoryQueryHarness.CreateAsync();
        var response = await harness.Service.GetHistoryReplayAsync(harness.SessionId);

        Assert.NotNull(response);
        Assert.Equal(harness.SessionId, response!.SessionId);
        Assert.Collection(
            response.Events,
            first => Assert.Equal(1, first.Sequence),
            second => Assert.Equal(2, second.Sequence),
            third => Assert.Equal(3, third.Sequence));
    }

    private sealed class HistoryQueryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private HistoryQueryHarness(
            SqliteConnection connection,
            TestDbContextFactory dbContextFactory,
            Guid sessionId,
            HistoryQueryService service)
        {
            _connection = connection;
            DbContextFactory = dbContextFactory;
            SessionId = sessionId;
            Service = service;
        }

        public TestDbContextFactory DbContextFactory { get; }

        public Guid SessionId { get; }

        public HistoryQueryService Service { get; }

        public static async Task<HistoryQueryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var dbContext = new DbOptimizerDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var sessionId = Guid.NewGuid();
            var checkpoint = CreateCheckpoint(sessionId);

            dbContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = "Running",
                State = JsonSerializer.Serialize(checkpoint, SerializerOptions),
                EngineType = "maf",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

            await dbContext.SaveChangesAsync();

            var factory = new TestDbContextFactory(options);
            var service = new HistoryQueryService(
                factory,
                Mock.Of<IWorkflowResultSerializer>(),
                new StubWorkflowEventQueryService(
                [
                    CreateEvent(sessionId, 2, WorkflowEventType.ExecutorCompleted, new { executorName = "Analyzer" }),
                    CreateEvent(sessionId, 3, WorkflowEventType.WorkflowCompleted, new { message = "done" })
                ]));

            return new HistoryQueryHarness(connection, factory, sessionId, service);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        private static WorkflowCheckpoint CreateCheckpoint(Guid sessionId)
        {
            var persistedEvents = new[]
            {
                CreateEvent(sessionId, 1, WorkflowEventType.ExecutorStarted, new { executorName = "Analyzer" }),
                CreateEvent(sessionId, 2, WorkflowEventType.ExecutorCompleted, new { executorName = "Analyzer", duplicate = true })
            };

            return new WorkflowCheckpoint
            {
                SessionId = sessionId,
                WorkflowType = "sql_analysis",
                Status = WorkflowCheckpointStatus.Running,
                CurrentExecutor = string.Empty,
                CheckpointVersion = 1,
                Context = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    [WorkflowContextKeys.WorkflowTimeline] = JsonSerializer.SerializeToElement(persistedEvents, TimelineSerializerOptions),
                    [WorkflowContextKeys.WorkflowTimelineNextSequence] = JsonSerializer.SerializeToElement(2L, TimelineSerializerOptions)
                },
                CompletedExecutors = Array.Empty<string>(),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                LastCheckpointAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
        }

        private static WorkflowEventRecord CreateEvent(
            Guid sessionId,
            long sequence,
            WorkflowEventType eventType,
            object payload)
        {
            return new WorkflowEventRecord(
                sequence,
                eventType,
                sessionId,
                "sql_analysis",
                DateTimeOffset.UtcNow.AddSeconds(sequence),
                JsonSerializer.SerializeToElement(payload, SerializerOptions));
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<DbOptimizerDbContext> options) : IDbContextFactory<DbOptimizerDbContext>
    {
        public DbOptimizerDbContext CreateDbContext()
        {
            return new DbOptimizerDbContext(options);
        }

        public Task<DbOptimizerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DbOptimizerDbContext(options));
        }
    }

    private sealed class StubWorkflowEventQueryService(IReadOnlyList<WorkflowEventRecord> events) : IWorkflowEventQueryService
    {
        public IReadOnlyList<WorkflowEventRecord> GetEvents(Guid sessionId, long afterSequence = 0, int limit = 200)
        {
            return events
                .Where(item => item.SessionId == sessionId && item.Sequence > afterSequence)
                .OrderBy(item => item.Sequence)
                .Take(limit)
                .ToArray();
        }

        public WorkflowEventSubscription Subscribe(Guid sessionId, long afterSequence = 0)
        {
            return new WorkflowEventSubscription([], Channel.CreateUnbounded<WorkflowEventRecord>().Reader, static () => { });
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly JsonSerializerOptions TimelineSerializerOptions = new(JsonSerializerDefaults.Web);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
