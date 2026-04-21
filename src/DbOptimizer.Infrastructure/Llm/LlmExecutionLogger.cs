using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Llm;

public sealed record LlmExecutionRecord(
    Guid SessionId,
    string AgentName,
    string ExecutorName,
    string Provider,
    string Model,
    string SystemPrompt,
    string UserPrompt,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string? Response = null,
    string? ErrorMessage = null,
    string? AgentSessionId = null,
    LlmTokenUsage? Usage = null,
    decimal? EstimatedCost = null,
    decimal? Confidence = null,
    string? Reasoning = null,
    string? Evidence = null,
    string? Metadata = null);

public interface ILlmExecutionLogger
{
    Task<Guid> LogExecutionAsync(
        LlmExecutionRecord record,
        CancellationToken cancellationToken = default);
}

public sealed class LlmExecutionLogger(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<LlmExecutionLogger> logger) : ILlmExecutionLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbContextFactory = dbContextFactory;
    private readonly ILogger<LlmExecutionLogger> _logger = logger;

    public async Task<Guid> LogExecutionAsync(
        LlmExecutionRecord record,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sessionExists = await dbContext.WorkflowSessions
            .AnyAsync(item => item.SessionId == record.SessionId, cancellationToken);

        if (!sessionExists)
        {
            throw new InvalidOperationException($"Workflow session {record.SessionId} was not found.");
        }

        var executionId = Guid.NewGuid();
        var durationMs = Math.Max(0, (long)(record.CompletedAt - record.StartedAt).TotalMilliseconds);
        var estimatedCost = record.EstimatedCost ?? CalculateEstimatedCost(record.Usage);

        var execution = new AgentExecutionEntity
        {
            ExecutionId = executionId,
            SessionId = record.SessionId,
            AgentName = record.AgentName,
            ExecutorName = record.ExecutorName,
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            Status = record.Status,
            InputData = JsonSerializer.Serialize(new
            {
                provider = record.Provider,
                model = record.Model,
                systemPrompt = record.SystemPrompt,
                userPrompt = record.UserPrompt,
                metadata = DeserializeOrFallback(record.Metadata)
            }, SerializerOptions),
            OutputData = JsonSerializer.Serialize(new
            {
                durationMs,
                response = record.Response
            }, SerializerOptions),
            ErrorMessage = record.ErrorMessage,
            TokenUsage = record.Usage is null
                ? null
                : JsonSerializer.Serialize(new
                {
                    inputTokens = record.Usage.InputTokens,
                    outputTokens = record.Usage.OutputTokens,
                    totalTokens = record.Usage.TotalTokens,
                    estimatedCost
                }, SerializerOptions)
        };

        dbContext.AgentExecutions.Add(execution);
        dbContext.AgentMessages.AddRange(CreateMessages(record, executionId));

        if (ShouldCreateDecisionRecord(record))
        {
            dbContext.DecisionRecords.Add(new DecisionRecordEntity
            {
                DecisionId = Guid.NewGuid(),
                ExecutionId = executionId,
                DecisionType = $"{record.AgentName}LlmDecision",
                Reasoning = record.Reasoning ?? record.Response ?? string.Empty,
                Confidence = record.Confidence.HasValue
                    ? WorkflowExecutionAuditHelper.NormalizeConfidence((double)record.Confidence.Value)
                    : 0m,
                Evidence = string.IsNullOrWhiteSpace(record.Evidence) ? "[]" : record.Evidence,
                CreatedAt = record.CompletedAt
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await UpdateSessionAggregatesAsync(
            dbContext,
            record.SessionId,
            record.AgentSessionId,
            record.Usage,
            estimatedCost,
            cancellationToken);

        _logger.LogInformation(
            "LLM execution persisted. SessionId={SessionId}, ExecutionId={ExecutionId}, ExecutorName={ExecutorName}, Status={Status}",
            record.SessionId,
            executionId,
            record.ExecutorName,
            record.Status);

        return executionId;
    }

    private static IEnumerable<AgentMessageEntity> CreateMessages(LlmExecutionRecord record, Guid executionId)
    {
        var createdAt = record.CompletedAt;

        yield return new AgentMessageEntity
        {
            MessageId = Guid.NewGuid(),
            ExecutionId = executionId,
            Role = "system",
            Content = record.SystemPrompt,
            Metadata = JsonSerializer.Serialize(new { source = "llm" }, SerializerOptions),
            CreatedAt = createdAt
        };

        yield return new AgentMessageEntity
        {
            MessageId = Guid.NewGuid(),
            ExecutionId = executionId,
            Role = "user",
            Content = record.UserPrompt,
            Metadata = JsonSerializer.Serialize(new { source = "llm" }, SerializerOptions),
            CreatedAt = createdAt
        };

        if (!string.IsNullOrWhiteSpace(record.Response))
        {
            yield return new AgentMessageEntity
            {
                MessageId = Guid.NewGuid(),
                ExecutionId = executionId,
                Role = "assistant",
                Content = record.Response,
                Metadata = JsonSerializer.Serialize(new
                {
                    source = "llm",
                    model = record.Model
                }, SerializerOptions),
                CreatedAt = createdAt
            };
        }
    }

    private static bool ShouldCreateDecisionRecord(LlmExecutionRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.Reasoning)
            || !string.IsNullOrWhiteSpace(record.Evidence)
            || record.Confidence.HasValue;
    }

    private static async Task UpdateSessionAggregatesAsync(
        DbOptimizerDbContext dbContext,
        Guid sessionId,
        string? agentSessionId,
        LlmTokenUsage? usage,
        decimal? estimatedCost,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var tokenDelta = usage?.TotalTokens ?? 0;

        if (IsNpgsql(dbContext))
        {
            var normalizedAgentSessionId = string.IsNullOrWhiteSpace(agentSessionId)
                ? null
                : agentSessionId;

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
$"""
UPDATE workflow_sessions
SET agent_session_ids = CASE
        WHEN {normalizedAgentSessionId} IS NULL THEN COALESCE(agent_session_ids, '[]'::jsonb)
        WHEN COALESCE(agent_session_ids, '[]'::jsonb) @> jsonb_build_array({normalizedAgentSessionId}) THEN COALESCE(agent_session_ids, '[]'::jsonb)
        ELSE COALESCE(agent_session_ids, '[]'::jsonb) || jsonb_build_array({normalizedAgentSessionId})
    END,
    total_tokens = total_tokens + {tokenDelta},
    estimated_cost = CASE
        WHEN {estimatedCost} IS NULL THEN estimated_cost
        ELSE COALESCE(estimated_cost, 0) + {estimatedCost}
    END,
    updated_at = {updatedAt}
WHERE session_id = {sessionId};
""",
                cancellationToken);

            return;
        }

        var session = await dbContext.WorkflowSessions
            .SingleAsync(item => item.SessionId == sessionId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(agentSessionId))
        {
            var sessionIds = DeserializeSessionIds(session.AgentSessionIds);
            if (!sessionIds.Contains(agentSessionId, StringComparer.Ordinal))
            {
                sessionIds.Add(agentSessionId);
                session.AgentSessionIds = JsonSerializer.Serialize(sessionIds, SerializerOptions);
            }
        }

        if (usage is not null)
        {
            session.TotalTokens += usage.TotalTokens;
        }

        if (estimatedCost.HasValue)
        {
            session.EstimatedCost = (session.EstimatedCost ?? 0m) + estimatedCost.Value;
        }

        session.UpdatedAt = updatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsNpgsql(DbOptimizerDbContext dbContext)
    {
        return string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);
    }

    private static List<string> DeserializeSessionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object? DeserializeOrFallback(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static decimal? CalculateEstimatedCost(LlmTokenUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        const decimal inputTokenCostPer1M = 3.0m;
        const decimal outputTokenCostPer1M = 15.0m;

        return (usage.InputTokens * inputTokenCostPer1M / 1_000_000m)
            + (usage.OutputTokens * outputTokenCostPer1M / 1_000_000m);
    }
}
