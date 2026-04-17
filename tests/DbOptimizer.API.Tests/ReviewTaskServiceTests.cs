using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class ReviewTaskServiceTests
{
    [Fact]
    public async Task CreateAsync_PreservesDatabaseMetadataInStoredEnvelope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var dbContext = new DbOptimizerDbContext(options))
        {
            await dbContext.Database.EnsureCreatedAsync();

            var sessionId = Guid.NewGuid();
            dbContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "SqlAnalysis",
                Status = "WaitingForReview",
                State = JsonSerializer.Serialize(
                    new
                    {
                        context = new Dictionary<string, object?>
                        {
                            [WorkflowContextKeys.DatabaseId] = "db-prod-01",
                            [WorkflowContextKeys.DatabaseType] = "postgresql"
                        }
                    }),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

            await dbContext.SaveChangesAsync();

            var service = new ReviewTaskService(
                new TestDbContextFactory(options),
                new WorkflowResultSerializer(),
                NullLogger<ReviewTaskService>.Instance);

            var reviewTaskId = await service.CreateAsync(
                sessionId,
                new OptimizationReport
                {
                    Summary = "summary",
                    OverallConfidence = 0.9
                });

            var reviewTask = await dbContext.ReviewTasks.SingleAsync(item => item.TaskId == reviewTaskId);
            var envelope = JsonSerializer.Deserialize<WorkflowResultEnvelope>(reviewTask.Recommendations, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(envelope);
            Assert.Equal("sql-optimization-report", envelope!.ResultType);
            Assert.Equal("db-prod-01", envelope.Metadata.GetProperty("databaseId").GetString());
            Assert.Equal("postgresql", envelope.Metadata.GetProperty("databaseType").GetString());
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
}
