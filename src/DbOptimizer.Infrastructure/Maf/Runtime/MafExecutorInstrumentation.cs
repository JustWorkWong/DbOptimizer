using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public interface IMafExecutorInstrumentation
{
    Task<Guid?> OnStartedAsync(
        string workflowType,
        string executorName,
        object input,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task OnCompletedAsync(
        string workflowType,
        string executorName,
        object input,
        object output,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool waitingForReview = false,
        Guid? executionId = null,
        CancellationToken cancellationToken = default);

    Task OnFailedAsync(
        string workflowType,
        string executorName,
        object input,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        Guid? executionId = null,
        CancellationToken cancellationToken = default);
}

public sealed class MafExecutorInstrumentation(
    ILogger<MafExecutorInstrumentation> logger,
    IWorkflowEventPublisher eventPublisher,
    IWorkflowExecutionAuditService auditService) : IMafExecutorInstrumentation
{
    public async Task<Guid?> OnStartedAsync(
        string workflowType,
        string executorName,
        object input,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ExtractSessionId(input);
        var context = BuildContext(sessionId, workflowType, executorName, input);
        var inputSummary = SummarizeInput(input);
        var stage = GetStageLabel(executorName);

        logger.LogInformation(
            "Workflow executor started. WorkflowType={WorkflowType}, SessionId={SessionId}, ExecutorName={ExecutorName}, Stage={Stage}, Input={Input}",
            workflowType,
            sessionId,
            executorName,
            stage,
            inputSummary);

        var executionId = await auditService.StartExecutionAsync(
            context,
            executorName,
            startedAt,
            cancellationToken);

        await eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.ExecutorStarted,
                sessionId,
                workflowType,
                startedAt,
                new
                {
                    executorName,
                    startedAt,
                    stage,
                    message = GetStartedMessage(executorName),
                    details = inputSummary
                }),
            cancellationToken);

        return executionId;
    }

    public async Task OnCompletedAsync(
        string workflowType,
        string executorName,
        object input,
        object output,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool waitingForReview = false,
        Guid? executionId = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ExtractSessionId(input);
        var context = BuildContext(sessionId, workflowType, executorName, input, output);
        var outputSummary = SummarizeOutput(output);
        var durationMs = (long)(completedAt - startedAt).TotalMilliseconds;
        var nextStatus = waitingForReview
            ? Checkpointing.WorkflowCheckpointStatus.WaitingForReview
            : Checkpointing.WorkflowCheckpointStatus.Completed;
        var stage = GetStageLabel(executorName);

        logger.LogInformation(
            "Workflow executor completed. WorkflowType={WorkflowType}, SessionId={SessionId}, ExecutorName={ExecutorName}, Stage={Stage}, DurationMs={DurationMs}, WaitingForReview={WaitingForReview}, Output={Output}",
            workflowType,
            sessionId,
            executorName,
            stage,
            durationMs,
            waitingForReview,
            outputSummary);

        await auditService.CompleteExecutionAsync(
            context,
            executionId,
            executorName,
            new WorkflowExecutorResult(true, nextStatus, output, null),
            startedAt,
            completedAt,
            cancellationToken);

        await eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.ExecutorCompleted,
                sessionId,
                workflowType,
                completedAt,
                new
                {
                    executorName,
                    completedAt,
                    durationMs,
                    nextStatus = nextStatus.ToString(),
                    stage,
                    message = waitingForReview
                        ? $"{stage} completed and is now waiting for manual review."
                        : $"{stage} completed.",
                    tokenUsage = ExtractTokenUsage(output),
                    details = outputSummary
                }),
            cancellationToken);
    }

    public async Task OnFailedAsync(
        string workflowType,
        string executorName,
        object input,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        Guid? executionId = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ExtractSessionId(input);
        var context = BuildContext(sessionId, workflowType, executorName, input);
        var durationMs = (long)(completedAt - startedAt).TotalMilliseconds;
        var stage = GetStageLabel(executorName);

        logger.LogError(
            exception,
            "Workflow executor failed. WorkflowType={WorkflowType}, SessionId={SessionId}, ExecutorName={ExecutorName}, Stage={Stage}, DurationMs={DurationMs}",
            workflowType,
            sessionId,
            executorName,
            stage,
            durationMs);

        await auditService.FailExecutionAsync(
            context,
            executionId,
            executorName,
            exception.Message,
            startedAt,
            completedAt,
            output: null,
            exception,
            cancellationToken);

        await eventPublisher.PublishAsync(
            new WorkflowEventMessage(
                WorkflowEventType.ExecutorFailed,
                sessionId,
                workflowType,
                completedAt,
                new
                {
                    executorName,
                    timestamp = completedAt,
                    durationMs,
                    errorMessage = exception.Message,
                    stage,
                    message = $"{stage} failed.",
                    details = new
                    {
                        exceptionType = exception.GetType().Name,
                        exception.Message
                    }
                }),
            cancellationToken);
    }

    private static Guid ExtractSessionId(object input)
    {
        var property = input.GetType().GetProperty("SessionId");
        if (property?.GetValue(input) is Guid sessionId)
        {
            return sessionId;
        }

        throw new InvalidOperationException($"Unable to resolve SessionId from {input.GetType().Name}.");
    }

    private static WorkflowContext BuildContext(
        Guid sessionId,
        string workflowType,
        string executorName,
        object input,
        object? output = null)
    {
        var context = new WorkflowContext(sessionId, workflowType);
        context.SetCurrentExecutor(executorName);
        ApplyInputContext(context, input);
        ApplyOutputContext(context, output);
        return context;
    }

    private static void ApplyInputContext(WorkflowContext context, object input)
    {
        switch (input)
        {
            case DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlAnalysisWorkflowCommand command:
                context.Set(WorkflowContextKeys.SqlText, command.SqlText);
                context.Set(WorkflowContextKeys.DatabaseId, command.DatabaseId);
                context.Set(WorkflowContextKeys.DatabaseType, command.DatabaseEngine);
                break;
            case SqlParsingCompletedMessage message:
                ApplyInputContext(context, message.Command);
                break;
            case ExecutionPlanCompletedMessage message:
                ApplyInputContext(context, message.Command);
                break;
            case IndexRecommendationCompletedMessage message:
                ApplyInputContext(context, message.Command);
                break;
            case SqlRewriteCompletedMessage message:
                ApplyInputContext(context, message.Command);
                break;
            case DbOptimizer.Infrastructure.Maf.DbConfig.DbConfigWorkflowCommand command:
                context.Set(WorkflowContextKeys.DatabaseId, command.DatabaseId);
                context.Set(WorkflowContextKeys.DatabaseType, command.DatabaseType);
                break;
            case ConfigSnapshotCollectedMessage message:
                ApplyInputContext(context, message.Command);
                break;
            case ConfigRecommendationsGeneratedMessage message:
                ApplyInputContext(context, message.Command);
                break;
        }
    }

    private static void ApplyOutputContext(WorkflowContext context, object? output)
    {
        switch (output)
        {
            case SqlParsingCompletedMessage message:
                context.Set(WorkflowContextKeys.ParsedSql, message.ParsedSql);
                break;
            case ExecutionPlanCompletedMessage message:
                context.Set(WorkflowContextKeys.ExecutionPlan, message.ExecutionPlan);
                break;
            case IndexRecommendationCompletedMessage message:
                context.Set(WorkflowContextKeys.IndexRecommendations, message.IndexRecommendations);
                break;
            case SqlOptimizationDraftReadyMessage message:
                context.Set(WorkflowContextKeys.FinalResult, message.DraftResult);
                break;
            case SqlOptimizationCompletedMessage message:
                context.Set(WorkflowContextKeys.FinalResult, message.FinalResult);
                break;
            case ConfigSnapshotCollectedMessage message:
                context.Set(WorkflowContextKeys.ConfigSnapshot, message.Snapshot);
                break;
            case ConfigRecommendationsGeneratedMessage message:
                context.Set(WorkflowContextKeys.ConfigRecommendations, message.Recommendations);
                break;
            case DbConfigOptimizationDraftReadyMessage message:
                context.Set(WorkflowContextKeys.ConfigOptimizationReport, message.DraftResult);
                break;
            case DbConfigOptimizationCompletedMessage message:
                context.Set(WorkflowContextKeys.ConfigOptimizationReport, message.FinalResult);
                break;
        }
    }

    private static object SummarizeInput(object input)
    {
        return input switch
        {
            DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlAnalysisWorkflowCommand command => new
            {
                command.DatabaseId,
                databaseType = command.DatabaseEngine,
                sqlLength = command.SqlText.Length,
                sqlPreview = BuildPreview(command.SqlText)
            },
            SqlParsingCompletedMessage message => new
            {
                message.Command.DatabaseId,
                databaseType = message.Command.DatabaseEngine,
                queryType = message.ParsedSql.QueryType,
                tableCount = message.ParsedSql.Tables.Count
            },
            ExecutionPlanCompletedMessage message => new
            {
                message.Command.DatabaseId,
                databaseType = message.Command.DatabaseEngine,
                queryType = message.ParsedSql.QueryType,
                issueCount = message.ExecutionPlan.Issues.Count
            },
            IndexRecommendationCompletedMessage message => new
            {
                message.Command.DatabaseId,
                databaseType = message.Command.DatabaseEngine,
                recommendationCount = message.IndexRecommendations.Count
            },
            SqlRewriteCompletedMessage message => new
            {
                message.Command.DatabaseId,
                databaseType = message.Command.DatabaseEngine,
                rewriteSuggestionCount = message.SqlRewriteSuggestions.Count
            },
            DbOptimizer.Infrastructure.Maf.DbConfig.DbConfigWorkflowCommand command => new
            {
                command.DatabaseId,
                command.DatabaseType,
                command.RequireHumanReview
            },
            ConfigSnapshotCollectedMessage message => new
            {
                message.Command.DatabaseId,
                message.Command.DatabaseType,
                parameterCount = message.Snapshot.Parameters.Count
            },
            ConfigRecommendationsGeneratedMessage message => new
            {
                message.Command.DatabaseId,
                message.Command.DatabaseType,
                recommendationCount = message.Recommendations.Count
            },
            _ => new { inputType = input.GetType().Name }
        };
    }

    private static object SummarizeOutput(object output)
    {
        return output switch
        {
            SqlParsingCompletedMessage message => new
            {
                queryType = message.ParsedSql.QueryType,
                tableCount = message.ParsedSql.Tables.Count,
                message.ParsedSql.Confidence,
                warningCount = message.ParsedSql.Warnings.Count
            },
            ExecutionPlanCompletedMessage message => new
            {
                message.ExecutionPlan.DatabaseEngine,
                message.ExecutionPlan.UsedFallback,
                issueCount = message.ExecutionPlan.Issues.Count,
                warningCount = message.ExecutionPlan.Warnings.Count
            },
            IndexRecommendationCompletedMessage message => new
            {
                recommendationCount = message.IndexRecommendations.Count
            },
            SqlRewriteCompletedMessage message => new
            {
                rewriteSuggestionCount = message.SqlRewriteSuggestions.Count,
                recommendationCount = message.IndexRecommendations.Count
            },
            SqlOptimizationDraftReadyMessage message => SummarizeEnvelope(message.DraftResult),
            SqlOptimizationCompletedMessage message => SummarizeEnvelope(message.FinalResult),
            ConfigSnapshotCollectedMessage message => new
            {
                message.Snapshot.DatabaseId,
                message.Snapshot.DatabaseType,
                parameterCount = message.Snapshot.Parameters.Count,
                message.Snapshot.UsedFallback,
                message.Snapshot.FallbackReason
            },
            ConfigRecommendationsGeneratedMessage message => new
            {
                recommendationCount = message.Recommendations.Count,
                highImpactCount = message.Recommendations.Count(item => string.Equals(item.Impact, "High", StringComparison.OrdinalIgnoreCase))
            },
            DbConfigOptimizationDraftReadyMessage message => SummarizeEnvelope(message.DraftResult),
            DbConfigOptimizationCompletedMessage message => SummarizeEnvelope(message.FinalResult),
            _ => new { outputType = output.GetType().Name }
        };
    }

    private static object SummarizeEnvelope(WorkflowResultEnvelope envelope)
    {
        return new
        {
            envelope.ResultType,
            envelope.DisplayName,
            envelope.Summary
        };
    }

    private static object? ExtractTokenUsage(object output)
    {
        if (output is not WorkflowResultEnvelope envelope ||
            !envelope.Metadata.TryGetProperty("tokenUsage", out var tokenUsage))
        {
            return null;
        }

        return JsonSerializer.Deserialize<object>(tokenUsage.GetRawText());
    }

    private static string BuildPreview(string value)
    {
        const int maxLength = 160;
        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : $"{singleLine[..maxLength]}...";
    }

    private static string GetStageLabel(string executorName)
    {
        return executorName switch
        {
            "SqlInputValidationExecutor" => "Input validation",
            "SqlParserMafExecutor" => "SQL parsing",
            "ExecutionPlanMafExecutor" => "Execution plan analysis",
            "IndexAdvisorMafExecutor" => "Index recommendation",
            "SqlRewriteMafExecutor" => "SQL rewrite",
            "SqlCoordinatorMafExecutor" => "Result coordination",
            "SqlHumanReviewGateExecutor" => "Human review",
            "DbConfigInputValidationExecutor" => "Input validation",
            "ConfigCollectorMafExecutor" => "Config collection",
            "ConfigAnalyzerMafExecutor" => "Config analysis",
            "ConfigCoordinatorMafExecutor" => "Config coordination",
            "ConfigHumanReviewGateExecutor" => "Config review",
            _ => executorName
        };
    }

    private static string GetStartedMessage(string executorName)
    {
        return executorName switch
        {
            "SqlInputValidationExecutor" => "Validating SQL input and database selection.",
            "SqlParserMafExecutor" => "Parsing SQL structure.",
            "ExecutionPlanMafExecutor" => "Collecting execution plan from the target database.",
            "IndexAdvisorMafExecutor" => "Generating index recommendations.",
            "SqlRewriteMafExecutor" => "Generating SQL rewrite suggestions.",
            "SqlCoordinatorMafExecutor" => "Building the SQL optimization report.",
            "SqlHumanReviewGateExecutor" => "Evaluating whether manual review is required.",
            "DbConfigInputValidationExecutor" => "Validating config optimization input.",
            "ConfigCollectorMafExecutor" => "Collecting database configuration and system metrics.",
            "ConfigAnalyzerMafExecutor" => "Running configuration analysis rules.",
            "ConfigCoordinatorMafExecutor" => "Building the configuration optimization report.",
            "ConfigHumanReviewGateExecutor" => "Evaluating whether config review is required.",
            _ => $"Running {executorName}."
        };
    }
}

internal sealed class ObservedExecutor<TInput, TOutput>(
    string workflowType,
    Executor<TInput, TOutput> innerExecutor,
    IMafExecutorInstrumentation instrumentation)
    : Executor<TInput, TOutput>(innerExecutor.Id)
{
    public override async ValueTask<TOutput> HandleAsync(
        TInput message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executionId = await instrumentation.OnStartedAsync(
            workflowType,
            innerExecutor.Id,
            message!,
            startedAt,
            cancellationToken);

        try
        {
            var output = await innerExecutor.HandleAsync(message, context, cancellationToken);
            await instrumentation.OnCompletedAsync(
                workflowType,
                innerExecutor.Id,
                message!,
                output!,
                startedAt,
                DateTimeOffset.UtcNow,
                executionId: executionId,
                cancellationToken: cancellationToken);
            return output;
        }
        catch (Exception ex) when (IsSuspended(ex))
        {
            await instrumentation.OnCompletedAsync(
                workflowType,
                innerExecutor.Id,
                message!,
                new { suspended = true, reason = ex.Message },
                startedAt,
                DateTimeOffset.UtcNow,
                waitingForReview: true,
                executionId: executionId,
                cancellationToken: cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await instrumentation.OnFailedAsync(
                workflowType,
                innerExecutor.Id,
                message!,
                ex,
                startedAt,
                DateTimeOffset.UtcNow,
                executionId,
                cancellationToken);
            throw;
        }
    }

    private static bool IsSuspended(Exception exception)
    {
        return string.Equals(exception.GetType().Name, "WorkflowSuspendedException", StringComparison.Ordinal);
    }
}
