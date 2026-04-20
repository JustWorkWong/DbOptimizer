using System.Text.Json;
using DbOptimizer.API.Api;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Application;
using DbOptimizer.Infrastructure.Workflows.Review;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task SubmitAsync_Reject_ReturnsRejectedStatus()
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
    }

    [Fact]
    public async Task SubmitAsync_Adjust_ReturnsAdjustedStatus()
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
                new WorkflowReviewTaskGateway(
                    DbContextFactory,
                    NullLogger<WorkflowReviewTaskGateway>.Instance),
                new StubWorkflowReviewResponseFactory(),
                new StubMafWorkflowRuntime(DbContextFactory));
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
                WorkflowType = "sql_analysis",
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
                Recommendations = JsonSerializer.Serialize(CreateEnvelope(report), SerializerOptions),
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

        private static WorkflowResultEnvelope CreateEnvelope(OptimizationReport report)
        {
            return new WorkflowResultEnvelope
            {
                ResultType = "sql_optimization",
                DisplayName = "SQL Optimization Result",
                Summary = report.Summary,
                Data = JsonSerializer.SerializeToElement(report, SerializerOptions),
                Metadata = JsonSerializer.SerializeToElement(
                    new Dictionary<string, object>
                    {
                        ["databaseId"] = "test-db"
                    },
                    SerializerOptions)
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

    private sealed class StubWorkflowReviewResponseFactory : IWorkflowReviewResponseFactory
    {
        private static readonly WorkflowResultEnvelope PlaceholderEnvelope = new()
        {
            ResultType = "review_request",
            DisplayName = "Review Request",
            Summary = string.Empty,
            Data = JsonSerializer.SerializeToElement(new { }),
            Metadata = JsonSerializer.SerializeToElement(new { })
        };

        public ExternalResponse CreateSqlResponse(
            Guid sessionId,
            Guid taskId,
            string requestId,
            string action,
            string? comment,
            IReadOnlyDictionary<string, JsonElement> adjustments)
        {
            return ExternalRequest.Create(
                    MafReviewPorts.SqlReview,
                    new SqlReviewRequestMessage(sessionId, taskId, PlaceholderEnvelope),
                    requestId)
                .CreateResponse(new SqlReviewResponseMessage(
                    sessionId,
                    taskId,
                    action,
                    comment,
                    adjustments,
                    DateTimeOffset.UtcNow));
        }

        public ExternalResponse CreateDbConfigResponse(
            Guid sessionId,
            Guid taskId,
            string requestId,
            string action,
            string? comment,
            IReadOnlyDictionary<string, JsonElement> adjustments)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubMafWorkflowRuntime(TestDbContextFactory dbContextFactory) : IMafWorkflowRuntime
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

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse> ResumeSqlWorkflowAsync(
            Guid sessionId,
            ExternalResponse reviewResponse,
            CancellationToken cancellationToken = default)
        {
            return UpdateSessionAndReturnAsync(sessionId, cancellationToken);
        }

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse> ResumeConfigWorkflowAsync(
            Guid sessionId,
            ExternalResponse reviewResponse,
            CancellationToken cancellationToken = default)
        {
            return UpdateSessionAndReturnAsync(sessionId, cancellationToken);
        }

        public Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowCancelResponse> CancelAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private async Task<DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse> UpdateSessionAndReturnAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.WorkflowSessions.SingleAsync(item => item.SessionId == sessionId, cancellationToken);
            session.Status = "Completed";
            session.CompletedAt = DateTimeOffset.UtcNow;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new DbOptimizer.Infrastructure.Maf.Runtime.WorkflowResumeResponse(
                SessionId: sessionId,
                Status: "Completed");
        }
    }
}
