using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;

/* =========================
 * SQL Coordinator Executor
 * 职责：
 * 1) 汇总所有分析结果
 * 2) 生成 WorkflowResultEnvelope (sql-optimization-report)
 * 3) 输出 SqlOptimizationDraftReadyMessage
 * ========================= */
public sealed class SqlCoordinatorMafExecutor(ILogger<SqlCoordinatorMafExecutor> logger)
    : Executor<SqlRewriteCompletedMessage, SqlOptimizationDraftReadyMessage>("SqlCoordinatorMafExecutor")
{
    public override ValueTask<SqlOptimizationDraftReadyMessage> HandleAsync(
        SqlRewriteCompletedMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var command = message.Command;
        var draftResult = BuildDraftResult(command, message);

        logger.LogInformation(
            "SQL optimization draft ready. SessionId={SessionId}, IndexRecommendationCount={IndexCount}, SqlRewriteCount={RewriteCount}",
            message.SessionId,
            message.IndexRecommendations.Count,
            message.SqlRewriteSuggestions.Count);

        return ValueTask.FromResult(new SqlOptimizationDraftReadyMessage(message.SessionId, draftResult));
    }

    private static DbOptimizer.Core.Models.WorkflowResultEnvelope BuildDraftResult(
        SqlAnalysisWorkflowCommand command,
        SqlRewriteCompletedMessage message)
    {
        var summary = BuildSummary(message);
        var overallConfidence = CalculateOverallConfidence(message);

        var data = new
        {
            sessionId = command.SessionId,
            sqlText = command.SqlText,
            databaseId = command.DatabaseId,
            databaseEngine = command.DatabaseEngine,
            parsedSql = message.ParsedSql,
            executionPlan = message.ExecutionPlan,
            indexRecommendations = message.IndexRecommendations,
            sqlRewriteSuggestions = message.SqlRewriteSuggestions,
            overallConfidence
        };

        var metadata = new
        {
            sourceType = command.SourceType,
            sourceRefId = command.SourceRefId,
            enableIndexRecommendation = command.EnableIndexRecommendation,
            enableSqlRewrite = command.EnableSqlRewrite,
            requireHumanReview = command.RequireHumanReview,
            generatedAt = DateTimeOffset.UtcNow
        };

        return new DbOptimizer.Core.Models.WorkflowResultEnvelope
        {
            ResultType = "sql-optimization-report",
            DisplayName = "SQL Optimization Report",
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(data),
            Metadata = JsonSerializer.SerializeToElement(metadata)
        };
    }

    private static string BuildSummary(SqlRewriteCompletedMessage message)
    {
        var parts = new List<string>();

        if (message.ExecutionPlan.Issues.Count > 0)
        {
            parts.Add($"{message.ExecutionPlan.Issues.Count} execution plan issues detected");
        }

        if (message.IndexRecommendations.Count > 0)
        {
            parts.Add($"{message.IndexRecommendations.Count} index recommendations");
        }

        if (message.SqlRewriteSuggestions.Count > 0)
        {
            parts.Add($"{message.SqlRewriteSuggestions.Count} SQL rewrite suggestions");
        }

        if (parts.Count == 0)
        {
            return "No optimization opportunities found";
        }

        return string.Join(", ", parts);
    }

    private static double CalculateOverallConfidence(SqlRewriteCompletedMessage message)
    {
        var confidenceValues = new List<double> { message.ParsedSql.Confidence };

        confidenceValues.AddRange(message.IndexRecommendations.Select(r => r.Confidence));
        confidenceValues.AddRange(message.SqlRewriteSuggestions.Select(s => s.Confidence));

        return confidenceValues.Count > 0 ? confidenceValues.Average() : 0.0;
    }
}
