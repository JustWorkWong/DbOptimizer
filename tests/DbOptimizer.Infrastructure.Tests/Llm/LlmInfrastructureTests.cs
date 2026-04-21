using System.Text.Json;
using DbOptimizer.Infrastructure.DependencyInjection;
using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Llm;

public sealed class LlmInfrastructureTests
{
    [Fact]
    public async Task LlmPromptManager_ReturnsActivePrompt()
    {
        var promptService = new Mock<IPromptVersionService>();
        promptService
            .Setup(service => service.GetActiveAsync("IndexAdvisor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionDto(
                Guid.NewGuid(),
                "IndexAdvisor",
                2,
                "system-prompt",
                null,
                true,
                DateTimeOffset.UtcNow,
                "tester"));

        var manager = new LlmPromptManager(promptService.Object);

        var prompt = await manager.GetPromptAsync("IndexAdvisor");

        Assert.Equal("system-prompt", prompt);
    }

    [Fact]
    public async Task LlmExecutionLogger_PersistsExecutionMessagesAndSessionAggregates()
    {
        var databaseName = $"llm-logger-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<DbOptimizerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var dbContextFactory = new TestDbContextFactory(options);
        await SeedWorkflowSessionAsync(dbContextFactory);

        var logger = new LlmExecutionLogger(
            dbContextFactory,
            NullLogger<LlmExecutionLogger>.Instance);

        var executionId = await logger.LogExecutionAsync(new LlmExecutionRecord(
            SessionId: SeedSessionId,
            AgentName: "IndexAdvisor",
            ExecutorName: "IndexAdvisorExecutor",
            Provider: "DashScope",
            Model: "qwen-max",
            SystemPrompt: "system",
            UserPrompt: "user",
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            Status: "Completed",
            Response: "{\"recommendations\":[]}",
            AgentSessionId: "conv-1",
            Usage: new LlmTokenUsage(120, 80, 200),
            Confidence: 0.88m,
            Reasoning: "reasoning",
            Evidence: "[\"plan\"]"));

        await using var verifyContext = await dbContextFactory.CreateDbContextAsync();
        var execution = await verifyContext.AgentExecutions.SingleAsync(item => item.ExecutionId == executionId);
        var messages = await verifyContext.AgentMessages.Where(item => item.ExecutionId == executionId).ToListAsync();
        var decision = await verifyContext.DecisionRecords.SingleAsync(item => item.ExecutionId == executionId);
        var session = await verifyContext.WorkflowSessions.SingleAsync(item => item.SessionId == SeedSessionId);

        Assert.Equal("IndexAdvisor", execution.AgentName);
        Assert.Equal(3, messages.Count);
        Assert.Equal(88m, decision.Confidence);
        Assert.Equal(200, session.TotalTokens);
        Assert.NotNull(session.EstimatedCost);

        var sessionIds = JsonSerializer.Deserialize<List<string>>(session.AgentSessionIds);
        Assert.Equal(["conv-1"], sessionIds);
    }

    [Fact]
    public void AddLlmInfrastructure_RegistersRequiredServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LlmProviderOptions.SectionName}:Provider"] = "DashScope",
                [$"{LlmProviderOptions.SectionName}:Model"] = "qwen-max",
                [$"{LlmProviderOptions.SectionName}:Endpoint"] = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                [$"{LlmProviderOptions.SectionName}:ApiKey"] = "test-key",
                [$"{MafFeatureFlags.SectionName}:EnableIndexAdvisorLlm"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPromptVersionService>(Mock.Of<IPromptVersionService>());
        services.AddSingleton<IDbContextFactory<DbOptimizerDbContext>>(Mock.Of<IDbContextFactory<DbOptimizerDbContext>>());
        services.AddLlmInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IChatClientService>());
        Assert.NotNull(serviceProvider.GetService<ILlmPromptManager>());
        Assert.NotNull(serviceProvider.GetService<ILlmExecutionLogger>());
    }

    private static async Task SeedWorkflowSessionAsync(IDbContextFactory<DbOptimizerDbContext> dbContextFactory)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        dbContext.WorkflowSessions.Add(new WorkflowSessionEntity
        {
            SessionId = SeedSessionId,
            WorkflowType = "SqlAnalysis",
            Status = "Running",
            State = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static readonly Guid SeedSessionId = Guid.Parse("1d2f8c8f-4d67-4dfd-b6fd-4f17e307cb4d");

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
