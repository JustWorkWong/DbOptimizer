using System.Text.Json;

namespace DbOptimizer.API.Workflows;

internal static class WorkflowEventPayloadFactory
{
    public static object BuildExecutorStartedPayload(
        WorkflowContext context,
        string executorName,
        DateTimeOffset startedAt)
    {
        return new
        {
            executorName,
            startedAt,
            stage = GetStageLabel(executorName),
            message = GetStartedMessage(executorName),
            details = BuildContextDetails(context, executorName)
        };
    }

    public static object BuildExecutorCompletedPayload(
        WorkflowContext context,
        string executorName,
        WorkflowExecutorResult result,
        DateTimeOffset completedAt,
        long durationMs)
    {
        return new
        {
            executorName,
            completedAt,
            durationMs,
            nextStatus = result.NextStatus.ToString(),
            stage = GetStageLabel(executorName),
            message = GetCompletedMessage(executorName, result.NextStatus),
            tokenUsage = TryExtractTokenUsage(result.Output),
            details = BuildResultDetails(context, executorName, result.Output)
        };
    }

    public static object BuildExecutorFailedPayload(
        WorkflowContext context,
        string executorName,
        string errorMessage,
        DateTimeOffset timestamp,
        long? durationMs = null,
        object? output = null)
    {
        return new
        {
            executorName,
            timestamp,
            durationMs,
            errorMessage,
            stage = GetStageLabel(executorName),
            message = $"{GetStageLabel(executorName)} failed.",
            tokenUsage = TryExtractTokenUsage(output),
            details = BuildResultDetails(context, executorName, output)
        };
    }

    private static string GetStageLabel(string executorName)
    {
        return executorName switch
        {
            "SqlParserExecutor" => "SQL parsing",
            "ExecutionPlanExecutor" => "Execution plan analysis",
            "IndexAdvisorExecutor" => "Index recommendation",
            "CoordinatorExecutor" => "Result coordination",
            "HumanReviewExecutor" => "Human review",
            "RegenerationExecutor" => "Result regeneration",
            "ConfigCollectorExecutor" => "Config collection",
            "ConfigAnalyzerExecutor" => "Config analysis",
            "ConfigCoordinatorExecutor" => "Config coordination",
            "ConfigReviewExecutor" => "Config review",
            _ => executorName
        };
    }

    private static string GetStartedMessage(string executorName)
    {
        return executorName switch
        {
            "SqlParserExecutor" => "Parsing SQL structure and extracting tables, filters, and query type.",
            "ExecutionPlanExecutor" => "Collecting execution plan and runtime diagnostics from the target database.",
            "IndexAdvisorExecutor" => "Inspecting index metadata and generating index recommendations.",
            "CoordinatorExecutor" => "Combining plan findings, index advice, and evidence into the final report.",
            "HumanReviewExecutor" => "Creating the manual review task for approval.",
            "RegenerationExecutor" => "Regenerating the final result from review feedback.",
            "ConfigCollectorExecutor" => "Collecting database configuration and system metrics.",
            "ConfigAnalyzerExecutor" => "Analyzing configuration findings against optimization rules.",
            "ConfigCoordinatorExecutor" => "Combining configuration findings into a single report.",
            "ConfigReviewExecutor" => "Creating the configuration review task for approval.",
            _ => $"Running {executorName}."
        };
    }

    private static string GetCompletedMessage(string executorName, Checkpointing.WorkflowCheckpointStatus nextStatus)
    {
        return nextStatus == Checkpointing.WorkflowCheckpointStatus.WaitingForReview
            ? $"{GetStageLabel(executorName)} completed and is now waiting for manual review."
            : $"{GetStageLabel(executorName)} completed.";
    }

    private static object BuildContextDetails(WorkflowContext context, string executorName)
    {
        return executorName switch
        {
            "SqlParserExecutor" => new
            {
                sqlText = TryGetValue<string>(context, WorkflowContextKeys.SqlText),
                databaseType = TryGetValue<string>(context, WorkflowContextKeys.DatabaseType)
            },
            "ExecutionPlanExecutor" => new
            {
                sqlText = TryGetValue<string>(context, WorkflowContextKeys.SqlText),
                databaseId = TryGetValue<string>(context, WorkflowContextKeys.DatabaseId),
                databaseType = TryGetValue<string>(context, WorkflowContextKeys.DatabaseType)
            },
            "IndexAdvisorExecutor" => new
            {
                parsedTables = TryGetValue<ParsedSqlResult>(context, WorkflowContextKeys.ParsedSql)?
                    .Tables
                    .Select(item => item.TableName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            },
            _ => new
            {
                databaseId = TryGetValue<string>(context, WorkflowContextKeys.DatabaseId),
                databaseType = TryGetValue<string>(context, WorkflowContextKeys.DatabaseType)
            }
        };
    }

    private static object? BuildResultDetails(WorkflowContext context, string executorName, object? output)
    {
        return output switch
        {
            ParsedSqlResult parsedSql => new
            {
                queryType = parsedSql.QueryType,
                dialect = parsedSql.Dialect,
                parseStrategy = parsedSql.ParseStrategy,
                confidence = parsedSql.Confidence,
                tables = parsedSql.Tables.Select(item => item.TableName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings = parsedSql.Warnings,
                unsupportedFeatures = parsedSql.UnsupportedFeatures
            },
            ExecutionPlanResult executionPlan => new
            {
                executionPlan.DatabaseEngine,
                executionPlan.ToolName,
                executionPlan.UsedFallback,
                executionPlan.AttemptCount,
                executionPlan.DiagnosticTag,
                executionPlan.ElapsedMs,
                executionPlan.Metrics,
                executionPlan.Warnings,
                issues = executionPlan.Issues.Select(issue => new
                {
                    issue.Type,
                    issue.TableName,
                    issue.Description,
                    issue.ImpactScore,
                    issue.Evidence
                }).ToArray()
            },
            List<IndexRecommendation> recommendations => new
            {
                recommendationCount = recommendations.Count,
                recommendations = recommendations.Select(item => new
                {
                    item.TableName,
                    item.Columns,
                    item.IndexType,
                    item.EstimatedBenefit,
                    item.Confidence,
                    item.Reasoning,
                    item.CreateDdl,
                    item.EvidenceRefs
                }).ToArray(),
                indexMetadata = TryGetValue<Dictionary<string, TableIndexMetadata>>(context, WorkflowContextKeys.TableIndexMetadata)?
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
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
            },
            OptimizationReport report => new
            {
                report.Summary,
                report.OverallConfidence,
                report.Warnings,
                report.Metadata,
                evidenceCount = report.EvidenceChain.Count,
                recommendationCount = report.IndexRecommendations.Count
            },
            DbConfigSnapshot snapshot => new
            {
                snapshot.DatabaseId,
                snapshot.DatabaseType,
                snapshot.CollectedAt,
                snapshot.UsedFallback,
                snapshot.FallbackReason,
                parameterCount = snapshot.Parameters.Count,
                snapshot.Metrics
            },
            ConfigOptimizationReport configReport => new
            {
                configReport.Summary,
                configReport.DatabaseId,
                configReport.DatabaseType,
                configReport.OverallConfidence,
                configReport.HighImpactCount,
                configReport.MediumImpactCount,
                configReport.LowImpactCount,
                configReport.RequiresRestartCount,
                recommendationCount = configReport.Recommendations.Count,
                configReport.Metadata
            },
            IReadOnlyDictionary<string, object> dictionary => dictionary,
            _ when string.Equals(executorName, "HumanReviewExecutor", StringComparison.Ordinal) => new
            {
                reviewId = TryGetValue<Guid>(context, WorkflowContextKeys.ReviewId),
                reviewStatus = TryGetValue<string>(context, WorkflowContextKeys.ReviewStatus)
            },
            _ => output
        };
    }

    private static object? TryExtractTokenUsage(object? output)
    {
        return output switch
        {
            OptimizationReport report when TryExtractTokenUsage(report.Metadata, out var usage) => usage,
            ConfigOptimizationReport report when TryExtractTokenUsage(report.Metadata, out var usage) => usage,
            IReadOnlyDictionary<string, object> dictionary when TryExtractTokenUsage(dictionary, out var usage) => usage,
            Dictionary<string, object> dictionary when TryExtractTokenUsage(dictionary, out var usage) => usage,
            _ => null
        };
    }

    private static bool TryExtractTokenUsage(
        IReadOnlyDictionary<string, object> metadata,
        out object? usage)
    {
        usage = null;

        if (!metadata.TryGetValue("tokenUsage", out var candidate) || candidate is null)
        {
            return false;
        }

        switch (candidate)
        {
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
            {
                if (!TryGetInt(element, "prompt", out var prompt) ||
                    !TryGetInt(element, "completion", out var completion) ||
                    !TryGetInt(element, "total", out var total))
                {
                    return false;
                }

                usage = new
                {
                    prompt,
                    completion,
                    total,
                    cost = TryGetDecimal(element, "cost", out var cost) ? cost : 0m,
                    source = "executor_output"
                };
                return true;
            }
            case IReadOnlyDictionary<string, object> nested:
            {
                return TryExtractTokenUsage(nested, out usage);
            }
            default:
                return false;
        }
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static T? TryGetValue<T>(WorkflowContext context, string key)
    {
        return context.TryGet<T>(key, out var value) ? value : default;
    }
}
