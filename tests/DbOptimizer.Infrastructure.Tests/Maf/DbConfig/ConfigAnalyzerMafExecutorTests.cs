using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.DbConfig.Executors;
using DbOptimizer.Infrastructure.Prompts;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Maf.DbConfig;

public sealed class ConfigAnalyzerMafExecutorTests
{
    [Fact]
    public async Task HandleAsync_WhenLlmEnabled_UsesStructuredResponseAndLogsExecution()
    {
        var configRuleEngine = new Mock<IConfigRuleEngine>(MockBehavior.Strict);
        var chatClientService = new Mock<IChatClientService>();
        var promptManager = new Mock<ILlmPromptManager>();
        var executionLogger = new Mock<ILlmExecutionLogger>();

        promptManager
            .Setup(service => service.GetActivePromptAsync("ConfigAnalyzer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionDto(
                Guid.NewGuid(),
                "ConfigAnalyzer",
                1,
                "system-prompt",
                null,
                true,
                DateTimeOffset.UtcNow,
                "tester"));

        chatClientService
            .Setup(service => service.GenerateStructuredAsync<ConfigAnalyzerLlmResponse>(
                "system-prompt",
                It.Is<string>(value => value.Contains("\"databaseType\":\"mysql\"", StringComparison.Ordinal)),
                It.Is<LlmRequestOptions>(options =>
                    options.ConversationId != null &&
                    options.Timeout == TimeSpan.FromSeconds(120) &&
                    options.MaxRetries == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmStructuredResponse<ConfigAnalyzerLlmResponse>(
                new ConfigAnalyzerLlmResponse
                {
                    Recommendations =
                    [
                        new ConfigAnalyzerLlmRecommendation
                        {
                            ParameterName = "innodb_buffer_pool_size",
                            CurrentValue = "134217728",
                            RecommendedValue = "4294967296",
                            Reasoning = "Increase buffer pool to better match available memory.",
                            Confidence = 0.89,
                            Impact = "High",
                            RequiresRestart = true,
                            EvidenceRefs = ["metrics.totalMemoryBytes:8589934592"],
                            RuleName = "ConfigAnalyzerLlm"
                        },
                        new ConfigAnalyzerLlmRecommendation
                        {
                            ParameterName = "sort_buffer_size",
                            CurrentValue = "262144",
                            RecommendedValue = "524288",
                            Reasoning = "Low confidence.",
                            Confidence = 0.35,
                            Impact = "Low",
                            RequiresRestart = false,
                            EvidenceRefs = [],
                            RuleName = "ConfigAnalyzerLlm"
                        }
                    ]
                },
                "{\"recommendations\":[{\"parameterName\":\"innodb_buffer_pool_size\"}]}",
                "qwen-max",
                new LlmTokenUsage(140, 60, 200)));

        executionLogger
            .Setup(service => service.LogExecutionAsync(
                It.Is<LlmExecutionRecord>(record =>
                    record.AgentName == "ConfigAnalyzer" &&
                    record.ExecutorName == nameof(ConfigAnalyzerMafExecutor) &&
                    record.Status == "Completed" &&
                    record.Usage != null &&
                    record.Usage.TotalTokens == 200),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var services = new ServiceCollection();
        services.AddSingleton(chatClientService.Object);
        services.AddSingleton(promptManager.Object);
        services.AddSingleton(executionLogger.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var executor = new ConfigAnalyzerMafExecutor(
            configRuleEngine.Object,
            serviceProvider,
            Options.Create(new DbOptimizer.Infrastructure.Maf.Runtime.MafFeatureFlags
            {
                EnableConfigAnalyzerLlm = true,
                EnableFallback = false
            }),
            NullLogger<ConfigAnalyzerMafExecutor>.Instance);

        var result = await executor.HandleAsync(
            CreateMessage(),
            Mock.Of<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Single(result.Recommendations);
        Assert.Equal("innodb_buffer_pool_size", result.Recommendations[0].ParameterName);
        Assert.Equal("High", result.Recommendations[0].Impact);
        Assert.True(result.Recommendations[0].RequiresRestart);

        executionLogger.VerifyAll();
        configRuleEngine.VerifyNoOtherCalls();
    }

    private static ConfigSnapshotCollectedMessage CreateMessage()
    {
        var sessionId = Guid.NewGuid();

        return new ConfigSnapshotCollectedMessage(
            sessionId,
            new DbConfigWorkflowCommand(
                sessionId,
                "db-1",
                "mysql",
                true,
                false),
            new DbConfigSnapshotContract(
                "mysql",
                "db-1",
                [
                    new ConfigParameterContract(
                        "innodb_buffer_pool_size",
                        "134217728",
                        "134217728",
                        "Buffer pool size",
                        false,
                        "integer",
                        "5242880",
                        null)
                ],
                new SystemMetricsContract(
                    8,
                    8L * 1024 * 1024 * 1024,
                    4L * 1024 * 1024 * 1024,
                    100L * 1024 * 1024 * 1024,
                    60L * 1024 * 1024 * 1024,
                    "8.0.36",
                    3600,
                    42,
                    200),
                DateTimeOffset.UtcNow,
                false,
                null));
    }
}
