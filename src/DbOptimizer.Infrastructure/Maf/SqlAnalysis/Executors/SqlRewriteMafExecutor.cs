using System.Text.Json;
using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Prompts;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

public sealed class SqlRewriteMafExecutor(
    ISqlRewriteAdvisor sqlRewriteAdvisor,
    IServiceProvider serviceProvider,
    IOptions<MafFeatureFlags> featureFlags,
    ILogger<SqlRewriteMafExecutor> logger)
    : Executor<IndexRecommendationCompletedMessage, SqlRewriteCompletedMessage>("SqlRewriteMafExecutor")
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly MafFeatureFlags _featureFlags = featureFlags.Value;

    public override ValueTask<SqlRewriteCompletedMessage> HandleAsync(
        IndexRecommendationCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return HandleCoreAsync(message, cancellationToken);
    }

    private async ValueTask<SqlRewriteCompletedMessage> HandleCoreAsync(
        IndexRecommendationCompletedMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SqlRewriteSuggestionContract> suggestions;
        if (!message.Command.EnableSqlRewrite)
        {
            logger.LogInformation(
                "SQL rewrite disabled. SessionId={SessionId}",
                message.SessionId);
            suggestions = Array.Empty<SqlRewriteSuggestionContract>();
        }
        else
        {
            suggestions = await GenerateSuggestionsAsync(message, cancellationToken);
            logger.LogInformation(
                "SQL rewrite completed. SessionId={SessionId}, SuggestionCount={SuggestionCount}",
                message.SessionId,
                suggestions.Count);
        }

        return new SqlRewriteCompletedMessage(
            message.SessionId,
            message.Command,
            message.ParsedSql,
            message.ExecutionPlan,
            message.IndexRecommendations,
            suggestions);
    }

    private async Task<IReadOnlyList<SqlRewriteSuggestionContract>> GenerateSuggestionsAsync(
        IndexRecommendationCompletedMessage message,
        CancellationToken cancellationToken)
    {
        var parsedSqlResult = BuildParsedSqlResult(message);
        var executionPlanResult = BuildExecutionPlanResult(message);

        if (!_featureFlags.EnableSqlRewriteLlm)
        {
            return await GenerateRuleBasedSuggestionsAsync(parsedSqlResult, executionPlanResult, cancellationToken);
        }

        var chatClientService = serviceProvider.GetRequiredService<IChatClientService>();
        var llmPromptManager = serviceProvider.GetRequiredService<ILlmPromptManager>();
        var llmExecutionLogger = serviceProvider.GetRequiredService<ILlmExecutionLogger>();
        var startedAt = DateTimeOffset.UtcNow;
        var systemPrompt = string.Empty;
        var userPrompt = string.Empty;
        var conversationId = $"sql-rewrite-{message.SessionId:N}";

        try
        {
            var activePrompt = await llmPromptManager.GetActivePromptAsync("SqlRewrite", cancellationToken);
            systemPrompt = activePrompt.PromptTemplate;
            userPrompt = BuildUserPrompt(message);

            var response = await chatClientService.GenerateStructuredAsync<SqlRewriteLlmResponse>(
                systemPrompt,
                userPrompt,
                new LlmRequestOptions
                {
                    ConversationId = conversationId,
                    Timeout = TimeSpan.FromSeconds(90),
                    MaxRetries = 2
                },
                cancellationToken);

            var completedAt = DateTimeOffset.UtcNow;
            var suggestions = MapSuggestions(response.Value, message.Command.SqlText);

            await llmExecutionLogger.LogExecutionAsync(
                new LlmExecutionRecord(
                    SessionId: message.SessionId,
                    AgentName: "SqlRewrite",
                    ExecutorName: nameof(SqlRewriteMafExecutor),
                    Provider: "LLM",
                    Model: response.ModelId,
                    SystemPrompt: systemPrompt,
                    UserPrompt: userPrompt,
                    StartedAt: startedAt,
                    CompletedAt: completedAt,
                    Status: "Completed",
                    Response: response.RawText,
                    AgentSessionId: conversationId,
                    Usage: response.Usage,
                    Confidence: CalculateAggregateConfidence(suggestions),
                    Reasoning: BuildReasoningSummary(suggestions, response.Value.Error),
                    Evidence: JsonSerializer.Serialize(suggestions.Select(item => new
                    {
                        item.Category,
                        item.EvidenceRefs
                    }), SerializerOptions),
                    Metadata: JsonSerializer.Serialize(new
                    {
                        promptVersion = activePrompt.VersionNumber,
                        message.Command.DatabaseEngine,
                        indexRecommendationCount = message.IndexRecommendations.Count
                    }, SerializerOptions)),
                cancellationToken);

            return suggestions;
        }
        catch (Exception ex)
        {
            await TryLogFailedExecutionAsync(
                message.SessionId,
                systemPrompt,
                userPrompt,
                startedAt,
                ex,
                cancellationToken);

            if (!_featureFlags.EnableFallback)
            {
                throw;
            }

            logger.LogWarning(
                ex,
                "LLM SQL rewrite failed and will fall back to rule-based generation. SessionId={SessionId}",
                message.SessionId);

            return await GenerateRuleBasedSuggestionsAsync(parsedSqlResult, executionPlanResult, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<SqlRewriteSuggestionContract>> GenerateRuleBasedSuggestionsAsync(
        ParsedSqlResult parsedSqlResult,
        ExecutionPlanResult executionPlanResult,
        CancellationToken cancellationToken)
    {
        var suggestions = await sqlRewriteAdvisor.GenerateAsync(parsedSqlResult, executionPlanResult, cancellationToken);

        return suggestions.Select(item => new SqlRewriteSuggestionContract(
            Category: item.Category,
            OriginalFragment: item.OriginalFragment,
            SuggestedFragment: item.SuggestedFragment,
            Reasoning: item.Reasoning,
            EstimatedBenefit: item.EstimatedBenefit,
            EvidenceRefs: item.EvidenceRefs,
            Confidence: item.Confidence)).ToList();
    }

    private static ParsedSqlResult BuildParsedSqlResult(IndexRecommendationCompletedMessage message)
    {
        return new ParsedSqlResult
        {
            QueryType = message.ParsedSql.QueryType,
            Dialect = message.ParsedSql.Dialect,
            IsPartial = message.ParsedSql.IsPartial,
            Confidence = message.ParsedSql.Confidence,
            RawSql = message.Command.SqlText,
            Tables = message.ParsedSql.Tables
                .Select(tableName => new ParsedTableReference
                {
                    TableName = tableName,
                    Confidence = message.ParsedSql.Confidence
                })
                .ToList(),
            Columns = message.ParsedSql.Columns
                .Select(columnName => new ParsedColumnReference
                {
                    ColumnName = columnName,
                    Confidence = message.ParsedSql.Confidence
                })
                .ToList(),
            Warnings = message.ParsedSql.Warnings.ToList()
        };
    }

    private static ExecutionPlanResult BuildExecutionPlanResult(IndexRecommendationCompletedMessage message)
    {
        return new ExecutionPlanResult
        {
            DatabaseEngine = message.ExecutionPlan.DatabaseEngine,
            RawPlan = message.ExecutionPlan.RawPlan,
            UsedFallback = message.ExecutionPlan.UsedFallback,
            Issues = message.ExecutionPlan.Issues.Select(issue => new ExecutionPlanIssue
            {
                Type = issue.Type,
                Description = issue.Description,
                TableName = issue.TableName,
                ImpactScore = issue.ImpactScore,
                Evidence = issue.Evidence
            }).ToList(),
            Warnings = message.ExecutionPlan.Warnings.ToList()
        };
    }

    private static string BuildUserPrompt(IndexRecommendationCompletedMessage message)
    {
        return JsonSerializer.Serialize(new
        {
            sessionId = message.SessionId,
            databaseEngine = message.Command.DatabaseEngine,
            sqlText = message.Command.SqlText,
            parsedSql = message.ParsedSql,
            executionPlan = message.ExecutionPlan,
            indexRecommendations = message.IndexRecommendations
        }, SerializerOptions);
    }

    private static IReadOnlyList<SqlRewriteSuggestionContract> MapSuggestions(
        SqlRewriteLlmResponse response,
        string originalSql)
    {
        return response.Suggestions
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Category) &&
                !string.IsNullOrWhiteSpace(item.SuggestedFragment))
            .Select(item => new SqlRewriteSuggestionContract(
                Category: item.Category.Trim(),
                OriginalFragment: string.IsNullOrWhiteSpace(item.OriginalFragment)
                    ? originalSql
                    : item.OriginalFragment,
                SuggestedFragment: item.SuggestedFragment.Trim(),
                Reasoning: string.IsNullOrWhiteSpace(item.Reasoning)
                    ? "LLM proposed a semantically equivalent SQL rewrite."
                    : item.Reasoning,
                EstimatedBenefit: Math.Clamp(item.EstimatedBenefit, 0, 100),
                EvidenceRefs: item.EvidenceRefs
                    .Where(evidence => !string.IsNullOrWhiteSpace(evidence))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Confidence: Math.Clamp(item.Confidence, 0, 1)))
            .Where(item => item.Confidence > 0.7)
            .ToList();
    }

    private async Task TryLogFailedExecutionAsync(
        Guid sessionId,
        string systemPrompt,
        string userPrompt,
        DateTimeOffset startedAt,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var llmExecutionLogger = serviceProvider.GetRequiredService<ILlmExecutionLogger>();
            await llmExecutionLogger.LogExecutionAsync(
                new LlmExecutionRecord(
                    SessionId: sessionId,
                    AgentName: "SqlRewrite",
                    ExecutorName: nameof(SqlRewriteMafExecutor),
                    Provider: "LLM",
                    Model: "unknown",
                    SystemPrompt: systemPrompt,
                    UserPrompt: userPrompt,
                    StartedAt: startedAt,
                    CompletedAt: DateTimeOffset.UtcNow,
                    Status: "Failed",
                    ErrorMessage: exception.Message),
                cancellationToken);
        }
        catch (Exception logException)
        {
            logger.LogWarning(
                logException,
                "Failed to persist LLM failure record for SQL rewrite. SessionId={SessionId}",
                sessionId);
        }
    }

    private static decimal? CalculateAggregateConfidence(IReadOnlyCollection<SqlRewriteSuggestionContract> suggestions)
    {
        return suggestions.Count == 0
            ? null
            : decimal.Round((decimal)suggestions.Average(item => item.Confidence), 4, MidpointRounding.AwayFromZero);
    }

    private static string BuildReasoningSummary(
        IReadOnlyCollection<SqlRewriteSuggestionContract> suggestions,
        string? error)
    {
        if (suggestions.Count == 0)
        {
            return string.IsNullOrWhiteSpace(error)
                ? "The LLM did not produce any high-confidence SQL rewrite suggestion."
                : error;
        }

        return string.Join(" ", suggestions.Select(item => item.Reasoning));
    }
}
