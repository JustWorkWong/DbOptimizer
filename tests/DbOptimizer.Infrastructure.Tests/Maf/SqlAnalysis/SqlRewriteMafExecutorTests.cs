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

public sealed class SqlRewriteMafExecutorTests
{
    [Fact]
    public async Task HandleAsync_WhenLlmEnabled_UsesStructuredResponseAndLogsExecution()
    {
        var sqlRewriteAdvisor = new Mock<ISqlRewriteAdvisor>(MockBehavior.Strict);
        var chatClientService = new Mock<IChatClientService>();
        var promptManager = new Mock<ILlmPromptManager>();
        var executionLogger = new Mock<ILlmExecutionLogger>();

        promptManager
            .Setup(service => service.GetActivePromptAsync("SqlRewrite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromptVersionDto(
                Guid.NewGuid(),
                "SqlRewrite",
                1,
                "system-prompt",
                null,
                true,
                DateTimeOffset.UtcNow,
                "tester"));

        chatClientService
            .Setup(service => service.GenerateStructuredAsync<SqlRewriteLlmResponse>(
                "system-prompt",
                It.Is<string>(value => value.Contains("\"sqlText\":\"SELECT * FROM users WHERE email = ?\"", StringComparison.Ordinal)),
                It.Is<LlmRequestOptions>(options =>
                    options.ConversationId != null &&
                    options.Timeout == TimeSpan.FromSeconds(90) &&
                    options.MaxRetries == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmStructuredResponse<SqlRewriteLlmResponse>(
                new SqlRewriteLlmResponse
                {
                    Suggestions =
                    [
                        new SqlRewriteLlmSuggestion
                        {
                            Category = "ProjectionPushdown",
                            OriginalFragment = "SELECT * FROM users WHERE email = ?",
                            SuggestedFragment = "SELECT id, email FROM users WHERE email = ?",
                            Reasoning = "Avoid wide row reads when only a subset of columns is required.",
                            EstimatedBenefit = 64,
                            EvidenceRefs = ["executionPlan.issue:FullTableScan"],
                            Confidence = 0.91
                        },
                        new SqlRewriteLlmSuggestion
                        {
                            Category = "LowConfidence",
                            SuggestedFragment = "SELECT email FROM users",
                            Reasoning = "Low confidence.",
                            EstimatedBenefit = 5,
                            EvidenceRefs = [],
                            Confidence = 0.2
                        }
                    ]
                },
                "{\"suggestions\":[{\"category\":\"ProjectionPushdown\"}]}",
                "qwen-max",
                new LlmTokenUsage(120, 70, 190)));

        executionLogger
            .Setup(service => service.LogExecutionAsync(
                It.Is<LlmExecutionRecord>(record =>
                    record.AgentName == "SqlRewrite" &&
                    record.ExecutorName == nameof(SqlRewriteMafExecutor) &&
                    record.Status == "Completed" &&
                    record.Usage != null &&
                    record.Usage.TotalTokens == 190),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var services = new ServiceCollection();
        services.AddSingleton(chatClientService.Object);
        services.AddSingleton(promptManager.Object);
        services.AddSingleton(executionLogger.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var executor = new SqlRewriteMafExecutor(
            sqlRewriteAdvisor.Object,
            serviceProvider,
            Options.Create(new DbOptimizer.Infrastructure.Maf.Runtime.MafFeatureFlags
            {
                EnableSqlRewriteLlm = true,
                EnableFallback = false
            }),
            NullLogger<SqlRewriteMafExecutor>.Instance);

        var result = await executor.HandleAsync(
            CreateMessage(),
            Mock.Of<IWorkflowContext>(),
            CancellationToken.None);

        Assert.Single(result.SqlRewriteSuggestions);
        Assert.Equal("ProjectionPushdown", result.SqlRewriteSuggestions[0].Category);
        Assert.Equal(0.91, result.SqlRewriteSuggestions[0].Confidence);

        executionLogger.VerifyAll();
        sqlRewriteAdvisor.VerifyNoOtherCalls();
    }

    private static IndexRecommendationCompletedMessage CreateMessage()
    {
        var sessionId = Guid.NewGuid();

        return new IndexRecommendationCompletedMessage(
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
                []),
            [
                new IndexRecommendationContract(
                    "users",
                    ["email"],
                    "BTREE",
                    "CREATE INDEX idx_users_email ON users(email)",
                    80,
                    "Add an index for email.",
                    ["executionPlan.issue:FullTableScan"],
                    0.88)
            ]);
    }
}
