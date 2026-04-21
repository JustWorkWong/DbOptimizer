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

/* =========================
 * 索引推荐 Executor
 * 职责：
 * 1) 根据 EnableIndexRecommendation 和 LLM Feature Flag 生成索引建议
 * 2) 优先走 LLM 路径，失败时按需回退到规则引擎
 * 3) 输出 IndexRecommendationCompletedMessage
 * ========================= */
public sealed class IndexAdvisorMafExecutor(
    IIndexRecommendationGenerator indexRecommendationGenerator,
    ITableIndexMetadataProvider tableIndexMetadataProvider,
    ITableIndexMetadataAnalyzer tableIndexMetadataAnalyzer,
    IServiceProvider serviceProvider,
    IOptions<MafFeatureFlags> featureFlags,
    ILogger<IndexAdvisorMafExecutor> logger)
    : Executor<ExecutionPlanCompletedMessage, IndexRecommendationCompletedMessage>("IndexAdvisorMafExecutor")
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly MafFeatureFlags _featureFlags = featureFlags.Value;

    public override ValueTask<IndexRecommendationCompletedMessage> HandleAsync(
        ExecutionPlanCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return HandleCoreAsync(message, cancellationToken);
    }

    private async ValueTask<IndexRecommendationCompletedMessage> HandleCoreAsync(
        ExecutionPlanCompletedMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = message.Command;
        IReadOnlyList<IndexRecommendationContract> recommendations;

        if (!command.EnableIndexRecommendation)
        {
            logger.LogInformation(
                "Index recommendation disabled. SessionId={SessionId}",
                message.SessionId);
            recommendations = Array.Empty<IndexRecommendationContract>();
        }
        else
        {
            recommendations = await GenerateRecommendationsAsync(message, cancellationToken);

            logger.LogInformation(
                "Index recommendation completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}",
                message.SessionId,
                recommendations.Count);
        }

        return new IndexRecommendationCompletedMessage(
            message.SessionId,
            command,
            message.ParsedSql,
            message.ExecutionPlan,
            recommendations);
    }

    private async Task<IReadOnlyList<IndexRecommendationContract>> GenerateRecommendationsAsync(
        ExecutionPlanCompletedMessage message,
        CancellationToken cancellationToken)
    {
        var databaseEngine = ParseDatabaseEngine(message.Command.DatabaseEngine);
        var parsedSqlResult = BuildParsedSqlResult(message);
        var executionPlanResult = BuildExecutionPlanResult(message);
        var tableIndexes = await TryLoadTableIndexMetadataAsync(message, databaseEngine, cancellationToken);

        if (!_featureFlags.EnableIndexAdvisorLlm)
        {
            return GenerateRuleBasedRecommendations(databaseEngine, parsedSqlResult, executionPlanResult, tableIndexes);
        }

        var chatClientService = serviceProvider.GetRequiredService<IChatClientService>();
        var llmPromptManager = serviceProvider.GetRequiredService<ILlmPromptManager>();
        var llmExecutionLogger = serviceProvider.GetRequiredService<ILlmExecutionLogger>();
        var startedAt = DateTimeOffset.UtcNow;
        var systemPrompt = string.Empty;
        var userPrompt = string.Empty;
        var conversationId = $"index-advisor-{message.SessionId:N}";

        try
        {
            var activePrompt = await llmPromptManager.GetActivePromptAsync("IndexAdvisor", cancellationToken);
            systemPrompt = activePrompt.PromptTemplate;
            userPrompt = BuildUserPrompt(message, tableIndexes);

            var response = await chatClientService.GenerateStructuredAsync<IndexAdvisorLlmResponse>(
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
            var recommendations = MapRecommendations(response.Value, databaseEngine, message.Command.DatabaseEngine);

            await llmExecutionLogger.LogExecutionAsync(
                new LlmExecutionRecord(
                    SessionId: message.SessionId,
                    AgentName: "IndexAdvisor",
                    ExecutorName: nameof(IndexAdvisorMafExecutor),
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
                    Confidence: CalculateAggregateConfidence(recommendations),
                    Reasoning: BuildReasoningSummary(recommendations, response.Value.Error),
                    Evidence: JsonSerializer.Serialize(recommendations.Select(item => new
                    {
                        item.TableName,
                        item.Columns,
                        item.EvidenceRefs
                    }), SerializerOptions),
                    Metadata: JsonSerializer.Serialize(new
                    {
                        promptVersion = activePrompt.VersionNumber,
                        message.Command.DatabaseEngine,
                        tableCount = tableIndexes.Count
                    }, SerializerOptions)),
                cancellationToken);

            return recommendations;
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
                "LLM index recommendation failed and will fall back to rule-based generation. SessionId={SessionId}",
                message.SessionId);

            return GenerateRuleBasedRecommendations(databaseEngine, parsedSqlResult, executionPlanResult, tableIndexes);
        }
    }

    private async Task<Dictionary<string, TableIndexMetadata>> TryLoadTableIndexMetadataAsync(
        ExecutionPlanCompletedMessage message,
        DatabaseOptimizationEngine databaseEngine,
        CancellationToken cancellationToken)
    {
        try
        {
            var tableNames = message.ParsedSql.Tables
                .Concat(message.ExecutionPlan.Issues
                    .Select(issue => issue.TableName)
                    .OfType<string>())
                .Where(tableName => !string.IsNullOrWhiteSpace(tableName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tableIndexes = new Dictionary<string, TableIndexMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var tableName in tableNames)
            {
                var invocationResult = await tableIndexMetadataProvider.GetIndexesAsync(
                    databaseEngine,
                    tableName,
                    cancellationToken);
                tableIndexes[tableName] = tableIndexMetadataAnalyzer.Analyze(tableName, invocationResult);
            }

            return tableIndexes;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to load table index metadata. SessionId={SessionId}, DatabaseEngine={DatabaseEngine}. Continuing with empty metadata.",
                message.SessionId,
                databaseEngine);
            return new Dictionary<string, TableIndexMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private IReadOnlyList<IndexRecommendationContract> GenerateRuleBasedRecommendations(
        DatabaseOptimizationEngine databaseEngine,
        ParsedSqlResult parsedSqlResult,
        ExecutionPlanResult executionPlanResult,
        IReadOnlyDictionary<string, TableIndexMetadata> tableIndexes)
    {
        var indexRecommendations = indexRecommendationGenerator.Generate(
            databaseEngine,
            parsedSqlResult,
            executionPlanResult,
            tableIndexes);

        return indexRecommendations.Select(item => new IndexRecommendationContract(
            TableName: item.TableName,
            Columns: item.Columns,
            IndexType: item.IndexType,
            CreateDdl: item.CreateDdl,
            EstimatedBenefit: item.EstimatedBenefit,
            Reasoning: item.Reasoning,
            EvidenceRefs: item.EvidenceRefs,
            Confidence: item.Confidence)).ToList();
    }

    private static ParsedSqlResult BuildParsedSqlResult(ExecutionPlanCompletedMessage message)
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

    private static ExecutionPlanResult BuildExecutionPlanResult(ExecutionPlanCompletedMessage message)
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

    private static string BuildUserPrompt(
        ExecutionPlanCompletedMessage message,
        IReadOnlyDictionary<string, TableIndexMetadata> tableIndexes)
    {
        return JsonSerializer.Serialize(new
        {
            sessionId = message.SessionId,
            databaseEngine = message.Command.DatabaseEngine,
            sqlText = message.Command.SqlText,
            parsedSql = message.ParsedSql,
            executionPlan = message.ExecutionPlan,
            existingIndexes = tableIndexes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new
                {
                    tableName = item.Key,
                    usedFallback = item.Value.UsedFallback,
                    warnings = item.Value.Warnings,
                    existingIndexes = item.Value.ExistingIndexes.Select(index => new
                    {
                        index.IndexName,
                        index.Columns,
                        index.IsUnique
                    }).ToArray()
                })
                .ToArray()
        }, SerializerOptions);
    }

    private static IReadOnlyList<IndexRecommendationContract> MapRecommendations(
        IndexAdvisorLlmResponse response,
        DatabaseOptimizationEngine databaseEngine,
        string databaseEngineName)
    {
        return response.Recommendations
            .Where(item => !string.IsNullOrWhiteSpace(item.TableName))
            .Select(item =>
            {
                var columns = item.Columns
                    .Where(column => !string.IsNullOrWhiteSpace(column))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var createDdl = string.IsNullOrWhiteSpace(item.CreateDdl)
                    ? BuildCreateDdl(databaseEngine, item.TableName, columns)
                    : item.CreateDdl;

                return new IndexRecommendationContract(
                    TableName: item.TableName,
                    Columns: columns,
                    IndexType: string.IsNullOrWhiteSpace(item.IndexType)
                        ? "BTREE"
                        : item.IndexType.Trim().ToUpperInvariant(),
                    CreateDdl: createDdl,
                    EstimatedBenefit: Math.Clamp(item.EstimatedBenefit, 0, 100),
                    Reasoning: string.IsNullOrWhiteSpace(item.Reasoning)
                        ? $"LLM recommended an index for {item.TableName} on {databaseEngineName}."
                        : item.Reasoning,
                    EvidenceRefs: item.EvidenceRefs
                        .Where(evidence => !string.IsNullOrWhiteSpace(evidence))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Confidence: Math.Clamp(item.Confidence, 0, 1));
            })
            .Where(item => item.Columns.Count > 0 && item.Confidence > 0.7)
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
                    AgentName: "IndexAdvisor",
                    ExecutorName: nameof(IndexAdvisorMafExecutor),
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
                "Failed to persist LLM failure record for index advisor. SessionId={SessionId}",
                sessionId);
        }
    }

    private static decimal? CalculateAggregateConfidence(IReadOnlyCollection<IndexRecommendationContract> recommendations)
    {
        return recommendations.Count == 0
            ? null
            : decimal.Round((decimal)recommendations.Average(item => item.Confidence), 4, MidpointRounding.AwayFromZero);
    }

    private static string BuildReasoningSummary(
        IReadOnlyCollection<IndexRecommendationContract> recommendations,
        string? error)
    {
        if (recommendations.Count == 0)
        {
            return string.IsNullOrWhiteSpace(error)
                ? "The LLM did not produce any high-confidence index recommendation."
                : error;
        }

        return string.Join(" ", recommendations.Select(item => item.Reasoning));
    }

    private static string BuildCreateDdl(
        DatabaseOptimizationEngine databaseEngine,
        string tableName,
        IReadOnlyList<string> columns)
    {
        var indexName = $"idx_{tableName}_{string.Join("_", columns)}";

        return databaseEngine switch
        {
            DatabaseOptimizationEngine.MySql =>
                $"CREATE INDEX `{indexName}` ON `{tableName}` ({string.Join(", ", columns.Select(column => $"`{column}`"))});",
            _ =>
                $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" ({string.Join(", ", columns.Select(column => $"\"{column}\""))});"
        };
    }

    private static DatabaseOptimizationEngine ParseDatabaseEngine(string engineName)
    {
        return engineName.ToLowerInvariant() switch
        {
            "mysql" => DatabaseOptimizationEngine.MySql,
            "postgresql" or "postgres" => DatabaseOptimizationEngine.PostgreSql,
            _ => DatabaseOptimizationEngine.Unknown
        };
    }
}
