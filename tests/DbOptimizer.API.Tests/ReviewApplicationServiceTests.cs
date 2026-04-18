using System.Text.Json;
using DbOptimizer.API.Api;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using DbOptimizer.Infrastructure.Workflows.Review;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class ReviewApplicationServiceTests
{
    [Fact]
    public async Task SubmitAsync_Approve_UpdatesSessionAndReviewTask()
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

        await using var db = await harness.DbContextFactory.CreateDbContextAsync();
        var reviewTask = await db.ReviewTasks.SingleAsync(item => item.TaskId == harness.ReviewTaskId);
        var session = await db.WorkflowSessions.SingleAsync(item => item.SessionId == harness.SessionId);

        Assert.Equal("Approved", reviewTask.Status);
        Assert.Equal("Completed", session.Status);
    }

    [Fact]
    public async Task SubmitAsync_Reject_ThrowsNotSupportedException()
    {
        await using var harness = await ReviewServiceHarness.CreateAsync();
        var service = harness.CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await service.SubmitAsync(
                harness.ReviewTaskId,
                new SubmitReviewRequest
                {
                    Action = "reject",
                    Comment = "need different index"
                });
        });
    }

    [Fact]
    public async Task SubmitAsync_Adjust_ThrowsNotSupportedException()
    {
        await using var harness = await ReviewServiceHarness.CreateAsync();
        var service = harness.CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await service.SubmitAsync(
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
        });
    }

    private sealed class ReviewServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ReviewServiceHarness(
            SqliteConnection connection,
            TestDbContextFactory dbContextFactory,
            Guid sessionId,
            Guid reviewTaskId)
        {
            _connection = connection;
            DbContextFactory = dbContextFactory;
            SessionId = sessionId;
            ReviewTaskId = reviewTaskId;
        }

        public TestDbContextFactory DbContextFactory { get; }

        public Guid SessionId { get; }

        public Guid ReviewTaskId { get; }

        public ReviewApplicationService CreateService()
        {
            return new ReviewApplicationService(
                DbContextFactory,
                new WorkflowResultSerializer(),
                new StubWorkflowReviewTaskGateway(),
                new StubWorkflowReviewResponseFactory(),
                new StubMafWorkflowRuntime());
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
                Status = "WaitingForReview",
                State = JsonSerializer.Serialize(new { databaseId = "test-db", report }, SerializerOptions),
                EngineType = "maf",
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
                sessionId,
                reviewTaskId);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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

    private sealed class StubWorkflowReviewTaskGateway : IWorkflowReviewTaskGateway
    {
        public Task<Guid> CreateAsync(
            Guid sessionId,
            string taskType,
            string requestId,
            string engineRunId,
            string checkpointRef,
            WorkflowResultEnvelope payload,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ReviewTaskCorrelation?> GetCorrelationAsync(
            Guid taskId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpdateStatusAsync(
            Guid taskId,
            string status,
            string? comment,
            string? adjustmentsJson,
            DateTimeOffset reviewedAt,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubWorkflowReviewResponseFactory : IWorkflowReviewResponseFactory
    {
        public ReviewDecisionResponseMessage CreateSqlResponse(
            Guid sessionId,
            Guid taskId,
            string requestId,
            string runId,
            string checkpointRef,
            string action,
            string? comment,
            IReadOnlyDictionary<string, JsonElement> adjustments)
        {
            throw new NotSupportedException();
        }

        public object CreateDbConfigResponse(
            Guid sessionId,
            Guid taskId,
            string requestId,
            string runId,
            string checkpointRef,
            string action,
            string? comment,
            IReadOnlyDictionary<string, JsonElement> adjustments)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubMafWorkflowRuntime : IMafWorkflowRuntime
    {
        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowStartResponse> StartSqlAnalysisAsync(
            DbOptimizer.Infrastructure.Maf.Runtime.SqlAnalysisWorkflowCommand command,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowStartResponse> StartDbConfigOptimizationAsync(
            DbOptimizer.Infrastructure.Maf.Runtime.DbConfigWorkflowCommand command,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse> ResumeAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowCancelResponse> CancelAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
