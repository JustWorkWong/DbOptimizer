using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbOptimizer.Infrastructure.Tests.Prompts;

public sealed class LlmPromptInitializationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WhenNoActivePromptExists_SeedsDefaultIndexAdvisorPrompt()
    {
        var databaseName = $"prompt-seed-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var dbContextFactory = new TestDbContextFactory(options);
        var promptVersionService = new PromptVersionService(
            dbContextFactory,
            NullLogger<PromptVersionService>.Instance);
        var hostedService = new LlmPromptInitializationHostedService(
            promptVersionService,
            NullLogger<LlmPromptInitializationHostedService>.Instance);

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        var activePrompt = await promptVersionService.GetActiveAsync("IndexAdvisor");
        var versions = await promptVersionService.ListAsync("IndexAdvisor");

        Assert.NotNull(activePrompt);
        Assert.Contains("Return JSON only.", activePrompt!.PromptTemplate, StringComparison.Ordinal);
        Assert.Single(versions.Items);
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
            return Task.FromResult(CreateDbContext());
        }
    }
}
