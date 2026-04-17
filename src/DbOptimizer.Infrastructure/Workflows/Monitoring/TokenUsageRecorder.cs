using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbOptimizer.Infrastructure.Workflows.Monitoring;

/// <summary>
/// Token 使用量记录器实现
/// 将 Token 消耗记录到 agent_executions 表
/// </summary>
public sealed class TokenUsageRecorder(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IOptions<TokenUsageRecorderOptions> options,
    ILogger<TokenUsageRecorder> logger) : ITokenUsageRecorder
{
    private const decimal InputTokenCostPer1M = 3.0m;
    private const decimal OutputTokenCostPer1M = 15.0m;
    private readonly TokenUsageRecorderOptions _options = options.Value;

    public async Task RecordAsync(
        Guid sessionId,
        string executorName,
        JsonElement? tokenUsage,
        CancellationToken cancellationToken = default)
    {
        if (tokenUsage is null || tokenUsage.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(linkedCts.Token);

            var execution = await dbContext.AgentExecutions
                .Where(x => x.SessionId == sessionId && x.ExecutorName == executorName)
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync(linkedCts.Token);

            if (execution is null)
            {
                logger.LogWarning(
                    "Agent execution not found for token recording. SessionId={SessionId}, ExecutorName={ExecutorName}",
                    sessionId,
                    executorName);
                return;
            }

            execution.TokenUsage = tokenUsage.Value.GetRawText();
            await dbContext.SaveChangesAsync(linkedCts.Token);

            logger.LogInformation(
                "Token usage recorded. SessionId={SessionId}, ExecutorName={ExecutorName}, TokenUsage={TokenUsage}",
                sessionId,
                executorName,
                tokenUsage.Value.GetRawText());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to record token usage. SessionId={SessionId}, ExecutorName={ExecutorName}",
                sessionId,
                executorName);
        }
    }

    public async Task<TokenUsageSummary> GetSessionSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var executions = await dbContext.AgentExecutions
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.TokenUsage != null)
            .Select(x => x.TokenUsage)
            .ToListAsync(cancellationToken);

        var totalInputTokens = 0;
        var totalOutputTokens = 0;

        foreach (var tokenUsageJson in executions)
        {
            if (string.IsNullOrWhiteSpace(tokenUsageJson))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(tokenUsageJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("inputTokens", out var inputProp) && inputProp.TryGetInt32(out var input))
                {
                    totalInputTokens += input;
                }

                if (root.TryGetProperty("outputTokens", out var outputProp) && outputProp.TryGetInt32(out var output))
                {
                    totalOutputTokens += output;
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse token usage JSON: {Json}", tokenUsageJson);
            }
        }

        var totalTokens = totalInputTokens + totalOutputTokens;
        var estimatedCost = (totalInputTokens * InputTokenCostPer1M / 1_000_000m) +
                           (totalOutputTokens * OutputTokenCostPer1M / 1_000_000m);

        return new TokenUsageSummary(totalInputTokens, totalOutputTokens, totalTokens, estimatedCost);
    }
}
