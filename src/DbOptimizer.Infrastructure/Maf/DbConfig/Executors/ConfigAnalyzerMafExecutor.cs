using System.Text.Json;
using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Prompts;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

public sealed class ConfigAnalyzerMafExecutor(
    IConfigRuleEngine configRuleEngine,
    IServiceProvider serviceProvider,
    IOptions<MafFeatureFlags> featureFlags,
    ILogger<ConfigAnalyzerMafExecutor> logger)
    : Executor<ConfigSnapshotCollectedMessage, ConfigRecommendationsGeneratedMessage>("ConfigAnalyzerMafExecutor")
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly MafFeatureFlags _featureFlags = featureFlags.Value;

    public override ValueTask<ConfigRecommendationsGeneratedMessage> HandleAsync(
        ConfigSnapshotCollectedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return HandleCoreAsync(message, cancellationToken);
    }

    private async ValueTask<ConfigRecommendationsGeneratedMessage> HandleCoreAsync(
        ConfigSnapshotCollectedMessage message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting configuration analysis. SessionId={SessionId}, DatabaseType={DatabaseType}",
            message.SessionId,
            message.Snapshot.DatabaseType);

        var snapshot = BuildSnapshot(message);
        var recommendations = await GenerateRecommendationsAsync(message, snapshot, cancellationToken);

        logger.LogInformation(
            "Configuration analysis completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}",
            message.SessionId,
            recommendations.Count);

        return new ConfigRecommendationsGeneratedMessage(
            message.SessionId,
            message.Command,
            message.Snapshot,
            recommendations);
    }

    private async Task<IReadOnlyList<ConfigRecommendationContract>> GenerateRecommendationsAsync(
        ConfigSnapshotCollectedMessage message,
        DbConfigSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!_featureFlags.EnableConfigAnalyzerLlm)
        {
            return GenerateRuleBasedRecommendations(snapshot);
        }

        var chatClientService = serviceProvider.GetRequiredService<IChatClientService>();
        var llmPromptManager = serviceProvider.GetRequiredService<ILlmPromptManager>();
        var llmExecutionLogger = serviceProvider.GetRequiredService<ILlmExecutionLogger>();
        var startedAt = DateTimeOffset.UtcNow;
        var systemPrompt = string.Empty;
        var userPrompt = string.Empty;
        var conversationId = $"config-analyzer-{message.SessionId:N}";

        try
        {
            var activePrompt = await llmPromptManager.GetActivePromptAsync("ConfigAnalyzer", cancellationToken);
            systemPrompt = activePrompt.PromptTemplate;
            userPrompt = BuildUserPrompt(message);

            var response = await chatClientService.GenerateStructuredAsync<ConfigAnalyzerLlmResponse>(
                systemPrompt,
                userPrompt,
                new LlmRequestOptions
                {
                    ConversationId = conversationId,
                    Timeout = TimeSpan.FromSeconds(120),
                    MaxRetries = 2
                },
                cancellationToken);

            var completedAt = DateTimeOffset.UtcNow;
            var recommendations = MapRecommendations(response.Value, snapshot);

            await llmExecutionLogger.LogExecutionAsync(
                new LlmExecutionRecord(
                    SessionId: message.SessionId,
                    AgentName: "ConfigAnalyzer",
                    ExecutorName: nameof(ConfigAnalyzerMafExecutor),
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
                        item.ParameterName,
                        item.EvidenceRefs
                    }), SerializerOptions),
                    Metadata: JsonSerializer.Serialize(new
                    {
                        promptVersion = activePrompt.VersionNumber,
                        snapshot.DatabaseType,
                        parameterCount = snapshot.Parameters.Count,
                        snapshot.UsedFallback
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
                "LLM config analysis failed and will fall back to rule-based generation. SessionId={SessionId}",
                message.SessionId);

            return GenerateRuleBasedRecommendations(snapshot);
        }
    }

    private IReadOnlyList<ConfigRecommendationContract> GenerateRuleBasedRecommendations(DbConfigSnapshot snapshot)
    {
        var recommendations = configRuleEngine.AnalyzeConfig(snapshot);

        return recommendations.Select(item => new ConfigRecommendationContract(
            ParameterName: item.ParameterName,
            CurrentValue: item.CurrentValue,
            RecommendedValue: item.RecommendedValue,
            Reasoning: item.Reasoning,
            Confidence: item.Confidence,
            Impact: item.Impact,
            RequiresRestart: item.RequiresRestart,
            EvidenceRefs: item.EvidenceRefs,
            RuleName: item.RuleName)).ToList();
    }

    private static DbConfigSnapshot BuildSnapshot(ConfigSnapshotCollectedMessage message)
    {
        return new DbConfigSnapshot
        {
            DatabaseType = message.Snapshot.DatabaseType,
            DatabaseId = message.Snapshot.DatabaseId,
            Parameters = message.Snapshot.Parameters.Select(parameter => new ConfigParameter
            {
                Name = parameter.Name,
                Value = parameter.Value,
                DefaultValue = parameter.DefaultValue,
                Description = parameter.Description,
                IsDynamic = parameter.IsDynamic,
                Type = parameter.Type,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue
            }).ToList(),
            Metrics = new SystemMetrics
            {
                CpuCores = message.Snapshot.Metrics.CpuCores,
                TotalMemoryBytes = message.Snapshot.Metrics.TotalMemoryBytes,
                AvailableMemoryBytes = message.Snapshot.Metrics.AvailableMemoryBytes,
                TotalDiskBytes = message.Snapshot.Metrics.TotalDiskBytes,
                AvailableDiskBytes = message.Snapshot.Metrics.AvailableDiskBytes,
                DatabaseVersion = message.Snapshot.Metrics.DatabaseVersion,
                UptimeSeconds = message.Snapshot.Metrics.UptimeSeconds,
                ActiveConnections = message.Snapshot.Metrics.ActiveConnections,
                MaxConnections = message.Snapshot.Metrics.MaxConnections
            },
            CollectedAt = message.Snapshot.CollectedAt,
            UsedFallback = message.Snapshot.UsedFallback,
            FallbackReason = message.Snapshot.FallbackReason
        };
    }

    private static string BuildUserPrompt(ConfigSnapshotCollectedMessage message)
    {
        return JsonSerializer.Serialize(new
        {
            sessionId = message.SessionId,
            databaseType = message.Snapshot.DatabaseType,
            databaseId = message.Snapshot.DatabaseId,
            collectedAt = message.Snapshot.CollectedAt,
            usedFallback = message.Snapshot.UsedFallback,
            fallbackReason = message.Snapshot.FallbackReason,
            parameters = message.Snapshot.Parameters,
            metrics = message.Snapshot.Metrics
        }, SerializerOptions);
    }

    private static IReadOnlyList<ConfigRecommendationContract> MapRecommendations(
        ConfigAnalyzerLlmResponse response,
        DbConfigSnapshot snapshot)
    {
        return response.Recommendations
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ParameterName) &&
                !string.IsNullOrWhiteSpace(item.RecommendedValue))
            .Select(item =>
            {
                var currentValue = string.IsNullOrWhiteSpace(item.CurrentValue)
                    ? snapshot.Parameters.FirstOrDefault(parameter =>
                        string.Equals(parameter.Name, item.ParameterName, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty
                    : item.CurrentValue;

                return new ConfigRecommendationContract(
                    ParameterName: item.ParameterName.Trim(),
                    CurrentValue: currentValue,
                    RecommendedValue: item.RecommendedValue.Trim(),
                    Reasoning: string.IsNullOrWhiteSpace(item.Reasoning)
                        ? $"LLM recommended adjusting {item.ParameterName}."
                        : item.Reasoning,
                    Confidence: Math.Clamp(item.Confidence, 0, 1),
                    Impact: NormalizeImpact(item.Impact),
                    RequiresRestart: item.RequiresRestart,
                    EvidenceRefs: item.EvidenceRefs
                        .Where(evidence => !string.IsNullOrWhiteSpace(evidence))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    RuleName: string.IsNullOrWhiteSpace(item.RuleName)
                        ? "ConfigAnalyzerLlm"
                        : item.RuleName.Trim());
            })
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
                    AgentName: "ConfigAnalyzer",
                    ExecutorName: nameof(ConfigAnalyzerMafExecutor),
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
                "Failed to persist LLM failure record for config analyzer. SessionId={SessionId}",
                sessionId);
        }
    }

    private static string NormalizeImpact(string impact)
    {
        return impact.Trim().ToLowerInvariant() switch
        {
            "high" => "High",
            "low" => "Low",
            _ => "Medium"
        };
    }

    private static decimal? CalculateAggregateConfidence(IReadOnlyCollection<ConfigRecommendationContract> recommendations)
    {
        return recommendations.Count == 0
            ? null
            : decimal.Round((decimal)recommendations.Average(item => item.Confidence), 4, MidpointRounding.AwayFromZero);
    }

    private static string BuildReasoningSummary(
        IReadOnlyCollection<ConfigRecommendationContract> recommendations,
        string? error)
    {
        if (recommendations.Count == 0)
        {
            return string.IsNullOrWhiteSpace(error)
                ? "The LLM did not produce any high-confidence configuration recommendation."
                : error;
        }

        return string.Join(" ", recommendations.Select(item => item.Reasoning));
    }
}
