using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;
using DbOptimizer.Infrastructure.Prompts;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Maf.SqlAnalysis;

public sealed class IndexAdvisorMafExecutorTests
{
    [Fact]
    public async Task HandleAsync_WhenLlmEnabled_UsesStructuredResponseAndLogsExecution()
    {
        var generator = new Mock<IIndexRecommendationGenerator>(MockBehavior.Strict);
        var metadataProvider = new Mock<ITableIndexMetadataProvider>();
        var metadataAnalyzer = new Mock<ITableIndexMetadataAnalyzer>();
        var chatClientService = new Mock<IChatClientService>();
        var promptManager = new Mock<ILlmPromptManager>();
        var executionLogger = new Mock<ILlmExecutionLogger>();

        metadataProvider
            .Setup(service => service.GetIndexesAsync(
                DatabaseOptimizationEngine.MySql,
                "users",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexMetadataInvocationResult
            {
                RawText = "[]"
            });

        metadataAnalyzer
            .Setup(service => service.Analyze("users", It.IsAny<IndexMetadataInvocationResult>()))
            .Returns(new TableIndexMetadata
            {
                TableName = "users"
            });

        promptManager
            .Setup(service => service.GetActivePromptAsync("IndexAdvisor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionDto(
                Guid.NewGuid(),
                "IndexAdvisor",
                1,
                "system-prompt",
                null,
                true,
                DateTimeOffset.UtcNow,
                "tester"));

        chatClientService
            .Setup(service => service.GenerateStructuredAsync<IndexAdvisorLlmResponse>(
                "system-prompt",
                It.Is<string>(value => value.Contains("\"sqlText\":\"SELECT * FROM users WHERE email = ?\"", StringComparison.Ordinal)),
                It.Is<LlmRequestOptions>(options =>
                    options.ConversationId != null &&
                    options.Timeout == TimeSpan.FromSeconds(90) &&
                    options.MaxRetries == 2 &&
                    options.UseStreaming == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmStructuredResponse<IndexAdvisorLlmResponse>(
                new IndexAdvisorLlmResponse
                {
                    Recommendations =
                    [
                        new IndexAdvisorLlmRecommendation
                        {
                            TableName = "users",
                            Columns = ["email"],
                            IndexType = "BTREE",
                            CreateDdl = "CREATE INDEX idx_users_email ON users(email)",
                            EstimatedBenefit = 88,
                            Reasoning = "Full table scan on users and email is used in the filter predicate.",
                            EvidenceRefs = ["executionPlan.issue:FullTableScan"],
                            Confidence = 0.88
                        },
                        new IndexAdvisorLlmRecommendation
                        {
                            TableName = "users",
                            Columns = ["status"],
                            IndexType = "BTREE",
                            CreateDdl = "CREATE INDEX idx_users_status ON users(status)",
                            EstimatedBenefit = 25,
                            Reasoning = "Low-confidence suggestion.",
                            EvidenceRefs = ["executionPlan.issue:IndexNotUsed"],
                            Confidence = 0.42
                        }
                    ]
                },
                "{\"recommendations\":[{\"tableName\":\"users\",\"columns\":[\"email\"]}]}",
                "qwen-max",
                new LlmTokenUsage(100, 50, 150)));

        executionLogger
            .Setup(service => service.LogExecutionAsync(
                It.Is<LlmExecutionRecord>(record =>
                    record.AgentName == "IndexAdvisor" &&
                    record.ExecutorName == nameof(IndexAdvisorMafExecutor) &&
                    record.Status == "Completed" &&
                    record.Usage != null &&
                    record.Usage.TotalTokens == 150),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var services = new ServiceCollection();
        services.AddSingleton(chatClientService.Object);
        services.AddSingleton(promptManager.Object);
        services.AddSingleton(executionLogger.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var executor = new IndexAdvisorMafExecutor(
            generator.Object,
            metadataProvider.Object,
            metadataAnalyzer.Object,
            serviceProvider,
            Options.Create(new DbOptimizer.Infrastructure.Maf.Runtime.MafFeatureFlags
            {
                EnableIndexAdvisorLlm = true,
                EnableFallback = false
            }),
            NullLogger<IndexAdvisorMafExecutor>.Instance);

        var result = await executor.HandleAsync(
            CreateMessage(),
            Mock.Of<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Single(result.IndexRecommendations);
        Assert.Equal("users", result.IndexRecommendations[0].TableName);
        Assert.Equal(["email"], result.IndexRecommendations[0].Columns);
        Assert.Equal(0.88, result.IndexRecommendations[0].Confidence);

        executionLogger.VerifyAll();
        generator.VerifyNoOtherCalls();
    }

    private static ExecutionPlanCompletedMessage CreateMessage()
    {
        var sessionId = Guid.NewGuid();

        return new ExecutionPlanCompletedMessage(
            sessionId,
            new SqlAnalysisWorkflowCommand(
                sessionId,
                "SELECT * FROM users WHERE email = ?",
                "db-1",
                "mysql",
                "manual",
                null,
                true,
                true,
                false),
            new ParsedSqlContract(
                "SELECT",
                "mysql",
                false,
                0.95,
                ["users"],
                ["email"],
                []),
            new ExecutionPlanContract(
                "mysql",
                "{}",
                false,
                [
                    new ExecutionPlanIssueContract(
                        "FullTableScan",
                        "Full table scan on users.",
                        "users",
                        0.92,
                        "type=ALL")
                ],
                []));
    }
}
