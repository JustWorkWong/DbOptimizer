using System.Text.Json;
using DbOptimizer.API.Api;
using DbOptimizer.API.Checkpointing;
using DbOptimizer.API.Persistence;
using DbOptimizer.API.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class ReviewApplicationServiceTests
{
    [Fact]
    public async Task SubmitAsync_Approve_CompletesCheckpoint_DeletesCheckpoint_AndPublishesWorkflowCompleted()
    {
        await using var harness = await ReviewServiceHarness.CreateAsync();
        var service = harness.CreateService();

        var result = await service.SubmitAsync(
            harness.ReviewTaskId,
            new SubmitReviewRequest
            {
                Action = "approve",
                Comment = "looks good"
            });

        Assert.Equal("Approved", result.Status);
        Assert.NotNull(harness.CheckpointStorage.SavedCheckpoint);
        Assert.Equal(WorkflowCheckpointStatus.Completed, harness.CheckpointStorage.SavedCheckpoint!.Status);
        Assert.Equal(harness.SessionId, harness.CheckpointStorage.DeletedSessionId);
        Assert.Contains(harness.WorkflowEventPublisher.PublishedEvents, item => item.EventType == WorkflowEventType.WorkflowCompleted);

        await using var db = await harness.DbContextFactory.CreateDbContextAsync();
        var reviewTask = await db.ReviewTasks.SingleAsync(item => item.TaskId == harness.ReviewTaskId);
        var session = await db.WorkflowSessions.SingleAsync(item => item.SessionId == harness.SessionId);

        Assert.Equal("Approved", reviewTask.Status);
        Assert.Equal("Completed", session.Status);
    }

    [Fact]
    public async Task SubmitAsync_Reject_PersistsRejectionReason_AndResumesWorkflow()
    {
        await using var harness = await ReviewServiceHarness.CreateAsync();
        var service = harness.CreateService();

        var result = await service.SubmitAsync(
            harness.ReviewTaskId,
            new SubmitReviewRequest
            {
                Action = "reject",
                Comment = "need different index"
            });

        Assert.Equal("Rejected", result.Status);
        Assert.NotNull(harness.CheckpointStorage.SavedCheckpoint);
        Assert.Equal(WorkflowCheckpointStatus.WaitingForReview, harness.CheckpointStorage.SavedCheckpoint!.Status);
        Assert.Equal("need different index", harness.CheckpointStorage.SavedCheckpoint.Context[WorkflowContextKeys.RejectionReason].GetString());
        Assert.NotNull(harness.WorkflowExecutionScheduler.ResumedCheckpoint);
        Assert.Equal(harness.SessionId, harness.WorkflowExecutionScheduler.ResumedCheckpoint!.SessionId);
    }

    [Fact]
    public async Task SubmitAsync_Adjust_RewritesFinalResult_AndMarksAdjusted()
    {
        await using var harness = await ReviewServiceHarness.CreateAsync();
        var service = harness.CreateService();

        var result = await service.SubmitAsync(
            harness.ReviewTaskId,
            new SubmitReviewRequest
            {
                Action = "adjust",
                Comment = "rename the index",
                Adjustments = new Dictionary<string, JsonElement>
                {
                    ["indexName"] = JsonSerializer.SerializeToElement("idx_users_age_v2")
                }
            });

        Assert.Equal("Adjusted", result.Status);
        Assert.NotNull(harness.CheckpointStorage.SavedCheckpoint);
        Assert.Equal(WorkflowCheckpointStatus.Completed, harness.CheckpointStorage.SavedCheckpoint!.Status);

        var finalResult = harness.CheckpointStorage.SavedCheckpoint.Context[WorkflowContextKeys.FinalResult]
            .Deserialize<OptimizationReport>();

        Assert.NotNull(finalResult);
        Assert.Equal(WorkflowCheckpointStatus.Completed, harness.CheckpointStorage.SavedCheckpoint.Status);
        Assert.Equal("rename the index", harness.CheckpointStorage.SavedCheckpoint.Context["ReviewComment"].GetString());
        Assert.True(harness.CheckpointStorage.SavedCheckpoint.Context.ContainsKey("ReviewAdjustments"));
    }

    private sealed class ReviewServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ReviewServiceHarness(
            SqliteConnection connection,
            TestDbContextFactory dbContextFactory,
            StubCheckpointStorage checkpointStorage,
            StubWorkflowExecutionScheduler workflowExecutionScheduler,
            StubWorkflowEventPublisher workflowEventPublisher,
            Guid sessionId,
            Guid reviewTaskId)
        {
            _connection = connection;
            DbContextFactory = dbContextFactory;
            CheckpointStorage = checkpointStorage;
            WorkflowExecutionScheduler = workflowExecutionScheduler;
            WorkflowEventPublisher = workflowEventPublisher;
            SessionId = sessionId;
            ReviewTaskId = reviewTaskId;
        }

        public TestDbContextFactory DbContextFactory { get; }

        public StubCheckpointStorage CheckpointStorage { get; }

        public StubWorkflowExecutionScheduler WorkflowExecutionScheduler { get; }

        public StubWorkflowEventPublisher WorkflowEventPublisher { get; }

        public Guid SessionId { get; }

        public Guid ReviewTaskId { get; }

        public ReviewApplicationService CreateService()
        {
            return new ReviewApplicationService(
                DbContextFactory,
                CheckpointStorage,
                WorkflowExecutionScheduler,
                WorkflowEventPublisher);
        }

        public static async Task<ReviewServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var dbContext = new DbOptimizerDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var sessionId = Guid.NewGuid();
            var reviewTaskId = Guid.NewGuid();
            var report = CreateReport();

            dbContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "SqlAnalysis",
                Status = WorkflowCheckpointStatus.WaitingForReview.ToString(),
                State = JsonSerializer.Serialize(CreateCheckpoint(sessionId, report), WorkflowCheckpointJson.SerializerOptions),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            dbContext.ReviewTasks.Add(new ReviewTaskEntity
            {
                TaskId = reviewTaskId,
                SessionId = sessionId,
                Status = "Pending",
                Recommendations = JsonSerializer.Serialize(report),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

            await dbContext.SaveChangesAsync();

            var factory = new TestDbContextFactory(options);
            return new ReviewServiceHarness(
                connection,
                factory,
                new StubCheckpointStorage(CreateCheckpoint(sessionId, report)),
                new StubWorkflowExecutionScheduler(),
                new StubWorkflowEventPublisher(),
                sessionId,
                reviewTaskId);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        private static WorkflowCheckpoint CreateCheckpoint(Guid sessionId, OptimizationReport report)
        {
            var context = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkflowContextKeys.FinalResult] = JsonSerializer.SerializeToElement(report),
                [WorkflowContextKeys.ReviewId] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
                [WorkflowContextKeys.ReviewStatus] = JsonSerializer.SerializeToElement("Pending")
            };

            return new WorkflowCheckpoint
            {
                SessionId = sessionId,
                WorkflowType = "SqlAnalysis",
                Status = WorkflowCheckpointStatus.WaitingForReview,
                CurrentExecutor = string.Empty,
                CheckpointVersion = 5,
                Context = context,
                CompletedExecutors =
                [
                    "SqlParserExecutor",
                    "ExecutionPlanExecutor",
                    "IndexAdvisorExecutor",
                    "CoordinatorExecutor",
                    "HumanReviewExecutor"
                ],
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
        }

        private static OptimizationReport CreateReport()
        {
            return new OptimizationReport
            {
                Summary = "Original summary",
                OverallConfidence = 0.92,
                IndexRecommendations =
                [
                    new IndexRecommendation
                    {
                        TableName = "users",
                        Columns = ["age"],
                        IndexType = "BTREE",
                        CreateDdl = "CREATE INDEX idx_users_age ON users(age)",
                        EstimatedBenefit = 97,
                        Reasoning = "reason",
                        Confidence = 0.95,
                        EvidenceRefs = []
                    }
                ],
                EvidenceChain = [],
                Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            };
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

    private sealed class StubCheckpointStorage(WorkflowCheckpoint? checkpoint) : ICheckpointStorage
    {
        public WorkflowCheckpoint? SavedCheckpoint { get; private set; }

        public Guid? DeletedSessionId { get; private set; }

        public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            SavedCheckpoint = checkpoint;
            return Task.CompletedTask;
        }

        public Task<WorkflowCheckpoint?> LoadCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(checkpoint?.SessionId == sessionId ? checkpoint : null);
        }

        public Task DeleteCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            DeletedSessionId = sessionId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubWorkflowExecutionScheduler : IWorkflowExecutionScheduler
    {
        public WorkflowCheckpoint? ResumedCheckpoint { get; private set; }

        public Task<WorkflowStartResponse> ScheduleSqlAnalysisAsync(CreateSqlAnalysisWorkflowRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowStartResponse> ScheduleDbConfigOptimizationAsync(CreateDbConfigOptimizationWorkflowRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowResumeResponse> ResumeAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            ResumedCheckpoint = checkpoint;
            return Task.FromResult(new WorkflowResumeResponse(checkpoint.SessionId, "Running", "RegenerationExecutor"));
        }
    }

    private sealed class StubWorkflowEventPublisher : IWorkflowEventPublisher
    {
        public List<WorkflowEventMessage> PublishedEvents { get; } = [];

        public Task PublishAsync(WorkflowEventMessage workflowEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(workflowEvent);
            return Task.CompletedTask;
        }
    }
}
