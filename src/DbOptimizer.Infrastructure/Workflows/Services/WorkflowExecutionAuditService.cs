using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Workflows;

internal interface IWorkflowExecutionAuditService
{
    Task<Guid?> StartExecutionAsync(
        WorkflowContext context,
        string executorName,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task CompleteExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        WorkflowExecutorResult result,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task FailExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        string errorMessage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        object? output = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default);

    Task CancelExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}

internal sealed class WorkflowExecutionAuditService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<WorkflowExecutionAuditService> logger) : IWorkflowExecutionAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid?> StartExecutionAsync(
        WorkflowContext context,
        string executorName,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = new AgentExecutionEntity
            {
                ExecutionId = Guid.NewGuid(),
                SessionId = context.SessionId,
                AgentName = executorName,
                ExecutorName = executorName,
                StartedAt = startedAt,
                Status = "Running",
                InputData = Serialize(new
                {
                    workflowType = context.WorkflowType,
                    executorName,
                    checkpointVersion = context.CheckpointVersion,
                    keys = context.Data.Keys.OrderBy(key => key).ToArray(),
                    sqlText = TryGetValue<string>(context, WorkflowContextKeys.SqlText),
                    databaseId = TryGetValue<string>(context, WorkflowContextKeys.DatabaseId),
                    databaseType = TryGetValue<string>(context, WorkflowContextKeys.DatabaseType)
                })
            };

            dbContext.AgentExecutions.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            return entity.ExecutionId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution start. SessionId={SessionId}, ExecutorName={ExecutorName}",
                context.SessionId,
                executorName);
            return null;
        }
    }

    public async Task CompleteExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        WorkflowExecutorResult result,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = result.NextStatus == Checkpointing.WorkflowCheckpointStatus.WaitingForReview
                ? "WaitingReview"
                : "Completed";
            entity.OutputData = Serialize(new
            {
                nextStatus = result.NextStatus.ToString(),
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                output = result.Output
            });
            entity.TokenUsage = Serialize(BuildTokenUsage(result.Output));
            entity.ErrorMessage = null;

            var toolCalls = BuildToolCalls(context, executionId.Value, executorName, startedAt, completedAt, result.Output);
            var decisionRecords = BuildDecisionRecords(executionId.Value, executorName, result.Output);
            var errorLogs = BuildRecoveredErrorLogs(context, executionId.Value, executorName, result.Output);

            if (toolCalls.Count > 0)
            {
                dbContext.ToolCalls.AddRange(toolCalls);
            }

            if (decisionRecords.Count > 0)
            {
                dbContext.DecisionRecords.AddRange(decisionRecords);
            }

            if (errorLogs.Count > 0)
            {
                dbContext.ErrorLogs.AddRange(errorLogs);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution completion. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

    public async Task FailExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        string errorMessage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        object? output = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = "Failed";
            entity.OutputData = Serialize(new
            {
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                output
            });
            entity.TokenUsage = Serialize(BuildTokenUsage(output));
            entity.ErrorMessage = errorMessage;

            dbContext.ErrorLogs.Add(new ErrorLogEntity
            {
                LogId = Guid.NewGuid(),
                SessionId = context.SessionId,
                ExecutionId = executionId.Value,
                ErrorType = exception?.GetType().Name ?? "ExecutorFailure",
                ErrorMessage = errorMessage,
                StackTrace = exception?.ToString(),
                Context = Serialize(new
                {
                    workflowType = context.WorkflowType,
                    executorName,
                    checkpointVersion = context.CheckpointVersion
                }),
                CreatedAt = completedAt
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution failure. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

    public async Task CancelExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = "Cancelled";
            entity.ErrorMessage = "Workflow cancelled.";
            entity.OutputData = Serialize(new
            {
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds
            });
            entity.TokenUsage = Serialize(BuildTokenUsage(null));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution cancellation. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

    private static List<ToolCallEntity> BuildToolCalls(
        WorkflowContext context,
        Guid executionId,
        string executorName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        object? output)
    {
        var toolCalls = new List<ToolCallEntity>();

        switch (output)
        {
            case ParsedSqlResult parsedSql:
                toolCalls.Add(CreateToolCall(
                    executionId,
                    parsedSql.ParseStrategy,
                    new
                    {
                        dialect = parsedSql.Dialect,
                        sqlText = parsedSql.RawSql
                    },
                    new
                    {
                        queryType = parsedSql.QueryType,
                        tableCount = parsedSql.Tables.Count,
                        warningCount = parsedSql.Warnings.Count
                    },
                    startedAt,
                    completedAt));
                break;
            case ExecutionPlanResult executionPlan:
                toolCalls.Add(CreateToolCall(
                    executionId,
                    executionPlan.ToolName,
                    new
                    {
                        databaseEngine = executionPlan.DatabaseEngine,
                        sqlText = TryGetValue<string>(context, WorkflowContextKeys.SqlText)
                    },
                    new
                    {
                        usedFallback = executionPlan.UsedFallback,
                        issueCount = executionPlan.Issues.Count,
                        warningCount = executionPlan.Warnings.Count,
                        attemptCount = executionPlan.AttemptCount,
                        diagnosticTag = executionPlan.DiagnosticTag,
                        elapsedMs = executionPlan.ElapsedMs
                    },
                    startedAt,
                    completedAt));
                break;
        }

        if (string.Equals(executorName, "IndexAdvisorExecutor", StringComparison.Ordinal) &&
            context.TryGet<Dictionary<string, TableIndexMetadata>>(WorkflowContextKeys.TableIndexMetadata, out var tableIndexes) &&
            tableIndexes is not null)
        {
            foreach (var (tableName, metadata) in tableIndexes.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                toolCalls.Add(CreateToolCall(
                    executionId,
                    "show_indexes",
                    new { tableName },
                    new
                    {
                        existingIndexCount = metadata.ExistingIndexes.Count,
                        usedFallback = metadata.UsedFallback,
                        warningCount = metadata.Warnings.Count
                    },
                    startedAt,
                    completedAt));
            }
        }

        return toolCalls;
    }

    private static List<DecisionRecordEntity> BuildDecisionRecords(Guid executionId, string executorName, object? output)
    {
        var decisionRecords = new List<DecisionRecordEntity>();

        switch (output)
        {
            case ParsedSqlResult parsedSql:
                decisionRecords.Add(CreateDecisionRecord(
                    executionId,
                    "SqlParsing",
                    $"Parsed {parsedSql.QueryType} SQL involving {parsedSql.Tables.Count} table(s).",
                    parsedSql.Confidence,
                    new
                    {
                        tables = parsedSql.Tables.Select(item => item.TableName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        warnings = parsedSql.Warnings,
                        unsupportedFeatures = parsedSql.UnsupportedFeatures
                    }));
                break;
            case ExecutionPlanResult executionPlan:
                decisionRecords.Add(CreateDecisionRecord(
                    executionId,
                    "ExecutionPlanAnalysis",
                    $"Analyzed execution plan with {executionPlan.Issues.Count} issue(s); fallback={executionPlan.UsedFallback}.",
                    executionPlan.UsedFallback ? 0.72 : 0.86,
                    new
                    {
                        issues = executionPlan.Issues,
                        warnings = executionPlan.Warnings,
                        metrics = executionPlan.Metrics,
                        diagnosticTag = executionPlan.DiagnosticTag
                    }));
                break;
            case List<IndexRecommendation> recommendations:
                if (recommendations.Count == 0)
                {
                    decisionRecords.Add(CreateDecisionRecord(
                        executionId,
                        "IndexRecommendation",
                        "No additional index recommendation was generated for this SQL statement.",
                        0.6,
                        new { recommendationCount = 0 }));
                }
                else
                {
                    foreach (var recommendation in recommendations)
                    {
                        decisionRecords.Add(CreateDecisionRecord(
                            executionId,
                            "IndexRecommendation",
                            recommendation.Reasoning,
                            recommendation.Confidence,
                            new
                            {
                                recommendation.TableName,
                                recommendation.Columns,
                                recommendation.IndexType,
                                recommendation.EstimatedBenefit,
                                recommendation.CreateDdl,
                                recommendation.EvidenceRefs
                            }));
                    }
                }

                break;
            case OptimizationReport report:
                decisionRecords.Add(CreateDecisionRecord(
                    executionId,
                    "OptimizationSummary",
                    report.Summary,
                    report.OverallConfidence,
                    new
                    {
                        report.Warnings,
                        report.Metadata,
                        evidenceCount = report.EvidenceChain.Count,
                        recommendationCount = report.IndexRecommendations.Count
                    }));
                break;
            default:
                if (string.Equals(executorName, "HumanReviewExecutor", StringComparison.Ordinal))
                {
                    decisionRecords.Add(CreateDecisionRecord(
                        executionId,
                        "HumanReviewPending",
                        "Workflow result was persisted and is now waiting for manual review.",
                        1,
                        new { status = "PendingReview" }));
                }

                break;
        }

        return decisionRecords;
    }

    private static List<ErrorLogEntity> BuildRecoveredErrorLogs(
        WorkflowContext context,
        Guid executionId,
        string executorName,
        object? output)
    {
        var errorLogs = new List<ErrorLogEntity>();

        if (output is ExecutionPlanResult executionPlan &&
            (executionPlan.UsedFallback || !string.IsNullOrWhiteSpace(executionPlan.DiagnosticTag)))
        {
            errorLogs.Add(CreateErrorLog(
                context.SessionId,
                executionId,
                "RecoverableToolFailure",
                $"Execution plan tool degraded to direct database access. DiagnosticTag={executionPlan.DiagnosticTag ?? "none"}",
                new
                {
                    executorName,
                    executionPlan.ToolName,
                    executionPlan.AttemptCount,
                    executionPlan.UsedFallback,
                    executionPlan.DiagnosticTag,
                    executionPlan.ElapsedMs
                }));
        }

        if (string.Equals(executorName, "IndexAdvisorExecutor", StringComparison.Ordinal) &&
            context.TryGet<Dictionary<string, TableIndexMetadata>>(WorkflowContextKeys.TableIndexMetadata, out var tableIndexes) &&
            tableIndexes is not null)
        {
            foreach (var (tableName, metadata) in tableIndexes.Where(item => item.Value.UsedFallback))
            {
                errorLogs.Add(CreateErrorLog(
                    context.SessionId,
                    executionId,
                    "RecoverableToolFailure",
                    $"Index metadata lookup degraded to direct database access for table '{tableName}'.",
                    new
                    {
                        executorName,
                        tableName,
                        usedFallback = metadata.UsedFallback,
                        warningCount = metadata.Warnings.Count
                    }));
            }
        }

        return errorLogs;
    }

    private static ToolCallEntity CreateToolCall(
        Guid executionId,
        string toolName,
        object arguments,
        object result,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        return new ToolCallEntity
        {
            CallId = Guid.NewGuid(),
            ExecutionId = executionId,
            ToolName = toolName,
            Arguments = Serialize(arguments) ?? "{}",
            Result = Serialize(result),
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = "Completed"
        };
    }

    private static DecisionRecordEntity CreateDecisionRecord(
        Guid executionId,
        string decisionType,
        string reasoning,
        double confidence,
        object evidence)
    {
        return new DecisionRecordEntity
        {
            DecisionId = Guid.NewGuid(),
            ExecutionId = executionId,
            DecisionType = decisionType,
            Reasoning = reasoning,
            Confidence = NormalizeConfidence(confidence),
            Evidence = Serialize(evidence) ?? "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ErrorLogEntity CreateErrorLog(
        Guid sessionId,
        Guid executionId,
        string errorType,
        string errorMessage,
        object context)
    {
        return new ErrorLogEntity
        {
            LogId = Guid.NewGuid(),
            SessionId = sessionId,
            ExecutionId = executionId,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            Context = Serialize(context),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static decimal NormalizeConfidence(double value)
    {
        var percent = value <= 1 ? value * 100 : value;
        return decimal.Round((decimal)Math.Clamp(percent, 0, 100), 2, MidpointRounding.AwayFromZero);
    }

    private static object BuildTokenUsage(object? output)
    {
        if (TryExtractTokenUsage(output, out var usage))
        {
            return usage;
        }

        return new
        {
            prompt = 0,
            completion = 0,
            total = 0,
            cost = 0m,
            available = false,
            source = "not_available"
        };
    }

    private static bool TryExtractTokenUsage(object? output, out object usage)
    {
        usage = null!;
        if (output is null)
        {
            return false;
        }

        if (output is OptimizationReport report &&
            TryExtractTokenUsageFromMetadata(report.Metadata, out usage))
        {
            return true;
        }

        if (output is ConfigOptimizationReport configReport &&
            TryExtractTokenUsageFromMetadata(configReport.Metadata, out usage))
        {
            return true;
        }

        if (output is IReadOnlyDictionary<string, object> dictionary &&
            TryExtractTokenUsageFromMetadata(dictionary, out usage))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractTokenUsageFromMetadata(
        IReadOnlyDictionary<string, object> metadata,
        out object usage)
    {
        usage = null!;
        if (!metadata.TryGetValue("tokenUsage", out var rawTokenUsage) || rawTokenUsage is null)
        {
            return false;
        }

        if (rawTokenUsage is JsonElement jsonElement &&
            TryParseTokenUsageFromJsonElement(jsonElement, out usage))
        {
            return true;
        }

        if (rawTokenUsage is IReadOnlyDictionary<string, object> nestedDictionary &&
            TryParseTokenUsageFromDictionary(nestedDictionary, out usage))
        {
            return true;
        }

        if (rawTokenUsage is Dictionary<string, object> plainDictionary &&
            TryParseTokenUsageFromDictionary(plainDictionary, out usage))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseTokenUsageFromDictionary(
        IReadOnlyDictionary<string, object> dictionary,
        out object usage)
    {
        usage = null!;
        if (!TryGetInt(dictionary, "prompt", out var prompt))
        {
            return false;
        }

        TryGetInt(dictionary, "completion", out var completion);
        TryGetInt(dictionary, "total", out var total);
        TryGetDecimal(dictionary, "cost", out var cost);

        usage = new
        {
            prompt,
            completion,
            total = total == 0 ? prompt + completion : total,
            cost,
            available = true,
            source = "metadata"
        };
        return true;
    }

    private static bool TryParseTokenUsageFromJsonElement(JsonElement element, out object usage)
    {
        usage = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetInt(element, "prompt", out var prompt))
        {
            return false;
        }

        TryGetInt(element, "completion", out var completion);
        TryGetInt(element, "total", out var total);
        TryGetDecimal(element, "cost", out var cost);

        usage = new
        {
            prompt,
            completion,
            total = total == 0 ? prompt + completion : total,
            cost,
            available = true,
            source = "metadata"
        };
        return true;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, object> dictionary, string key, out int value)
    {
        value = 0;
        if (!dictionary.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        return rawValue switch
        {
            int intValue => (value = intValue) >= 0,
            long longValue => (value = (int)longValue) >= 0,
            JsonElement jsonElement when TryGetInt(jsonElement, out var parsed) => (value = parsed) >= 0,
            _ when int.TryParse(rawValue.ToString(), out var parsed) => (value = parsed) >= 0,
            _ => false
        };
    }

    private static bool TryGetDecimal(IReadOnlyDictionary<string, object> dictionary, string key, out decimal value)
    {
        value = 0;
        if (!dictionary.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            return false;
        }

        return rawValue switch
        {
            decimal decimalValue => (value = decimalValue) >= 0,
            double doubleValue => (value = (decimal)doubleValue) >= 0,
            JsonElement jsonElement when TryGetDecimal(jsonElement, out var parsed) => (value = parsed) >= 0,
            _ when decimal.TryParse(rawValue.ToString(), out var parsed) => (value = parsed) >= 0,
            _ => false
        };
    }

    private static bool TryGetInt(JsonElement element, string key, out int value)
    {
        value = 0;
        return element.TryGetProperty(key, out var property) && TryGetInt(property, out value);
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(element.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetDecimal(JsonElement element, string key, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(key, out var property) && TryGetDecimal(property, out value);
    }

    private static bool TryGetDecimal(JsonElement element, out decimal value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), out value),
            _ => false
        };
    }

    private static T? TryGetValue<T>(WorkflowContext context, string key)
    {
        return context.TryGet<T>(key, out var value) ? value : default;
    }

    private static string? Serialize(object? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);
    }
}
