using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.SlowQuery;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DbOptimizer.Infrastructure.Tests.SlowQuery;

public sealed class SlowQueryDashboardQueryServiceTests
{
    [Fact]
    public void SingletonRegistration_WithDbContextFactory_CanResolveService()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();

        services.AddDbContextFactory<DbOptimizerDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<ISlowQueryDashboardQueryService, SlowQueryDashboardQueryService>();

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var service = serviceProvider.GetRequiredService<ISlowQueryDashboardQueryService>();

        service.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSlowQueryAsync_ReturnsDetailAndLatestAnalysis()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var queryId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await using (var seedContext = new DbOptimizerDbContext(options))
        {
            seedContext.SlowQueries.Add(new SlowQueryEntity
            {
                QueryId = queryId,
                DatabaseId = "db-1",
                DatabaseType = "postgresql",
                QueryHash = "hash-1",
                SqlFingerprint = "select * from users where id = ?",
                OriginalSql = "select * from users where id = 1",
                QueryType = "SELECT",
                Tables = "users,orders",
                AvgExecutionTime = TimeSpan.FromMilliseconds(120),
                MaxExecutionTime = TimeSpan.FromMilliseconds(300),
                ExecutionCount = 5,
                TotalRowsExamined = 1000,
                TotalRowsSent = 10,
                FirstSeenAt = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero),
                LastSeenAt = new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                LatestAnalysisSessionId = sessionId
            });

            seedContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = sessionId,
                WorkflowType = "slow-query-analysis",
                Status = "Completed",
                ResultType = "recommendation",
                SourceType = "slow-query",
                SourceRefId = queryId,
                CreatedAt = new DateTimeOffset(2026, 4, 17, 1, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 4, 17, 1, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(2026, 4, 17, 1, 5, 0, TimeSpan.Zero)
            });

            await seedContext.SaveChangesAsync();
        }

        var dbContextFactory = new TestDbContextFactory(options);
        var service = new SlowQueryDashboardQueryService(dbContextFactory);

        var result = await service.GetSlowQueryAsync(queryId);

        result.Should().NotBeNull();
        result!.QueryId.Should().Be(queryId);
        result.Tables.Should().Equal("users", "orders");
        result.LatestAnalysis.Should().NotBeNull();
        result.LatestAnalysis!.SessionId.Should().Be(sessionId);
        result.LatestAnalysis.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task GetTrendAsync_GroupsPointsByUtcDate()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var utcBoundary = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(-2), TimeSpan.Zero);
        var localOffsetTimestamp = utcBoundary.ToOffset(TimeSpan.FromHours(8));
        var queryId = Guid.NewGuid();

        await using (var seedContext = new DbOptimizerDbContext(options))
        {
            seedContext.SlowQueries.Add(new SlowQueryEntity
            {
                QueryId = queryId,
                DatabaseId = "db-utc",
                DatabaseType = "postgresql",
                QueryHash = "hash-utc",
                SqlFingerprint = "select 1",
                OriginalSql = "select 1",
                QueryType = "SELECT",
                Tables = "dual",
                AvgExecutionTime = TimeSpan.FromMilliseconds(120),
                MaxExecutionTime = TimeSpan.FromMilliseconds(120),
                ExecutionCount = 1,
                TotalRowsExamined = 1,
                TotalRowsSent = 1,
                FirstSeenAt = localOffsetTimestamp,
                LastSeenAt = localOffsetTimestamp,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            seedContext.WorkflowSessions.Add(new WorkflowSessionEntity
            {
                SessionId = Guid.NewGuid(),
                WorkflowType = "slow-query-analysis",
                Status = "Completed",
                SourceType = "slow-query",
                SourceRefId = queryId,
                CreatedAt = localOffsetTimestamp,
                UpdatedAt = localOffsetTimestamp
            });

            await seedContext.SaveChangesAsync();
        }

        var service = new SlowQueryDashboardQueryService(new TestDbContextFactory(options));

        var result = await service.GetTrendAsync("db-utc", 2);

        result.Points.Should().ContainSingle();
        result.Points[0].Date.Should().Be(utcBoundary.UtcDateTime.Date.ToString("yyyy-MM-dd"));
        result.Points[0].AnalysisTriggeredCount.Should().Be(1);
    }

    private sealed class TestDbContextFactory(DbContextOptions<DbOptimizerDbContext> options)
        : IDbContextFactory<DbOptimizerDbContext>
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
