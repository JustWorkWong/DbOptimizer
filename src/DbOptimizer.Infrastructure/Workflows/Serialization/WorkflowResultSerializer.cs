using System.Text.Json;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.Workflows;

public interface IWorkflowResultSerializer
{
    WorkflowResultEnvelope ToEnvelope(OptimizationReport report, string databaseId, string databaseType);

    WorkflowResultEnvelope ToEnvelope(ConfigOptimizationReport report);

    WorkflowResultEnvelope ToEnvelope(
        string workflowType,
        JsonElement resultElement,
        string? databaseId = null,
        string? databaseType = null);

    WorkflowResultEnvelope ToEnvelope(
        string workflowType,
        string json,
        string? databaseId = null,
        string? databaseType = null);

    int GetRecommendationCount(
        string workflowType,
        JsonElement resultElement,
        string? databaseId = null,
        string? databaseType = null);
}

public sealed class WorkflowResultSerializer : IWorkflowResultSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly WorkflowResultSerializer EmptySerializer = new();

    public WorkflowResultEnvelope ToEnvelope(OptimizationReport report, string databaseId, string databaseType)
    {
        return new WorkflowResultEnvelope
        {
            ResultType = "sql-optimization-report",
            DisplayName = "SQL 调优报告",
            Summary = report.Summary,
            Data = JsonSerializer.SerializeToElement(report, SerializerOptions),
            Metadata = JsonSerializer.SerializeToElement(
                new
                {
                    databaseId,
                    databaseType,
                    report.OverallConfidence,
                    warningCount = report.Warnings.Count
                },
                SerializerOptions)
        };
    }

    public WorkflowResultEnvelope ToEnvelope(ConfigOptimizationReport report)
    {
        return new WorkflowResultEnvelope
        {
            ResultType = "db-config-optimization-report",
            DisplayName = "数据库配置调优报告",
            Summary = report.Summary,
            Data = JsonSerializer.SerializeToElement(report, SerializerOptions),
            Metadata = JsonSerializer.SerializeToElement(
                new
                {
                    report.DatabaseId,
                    report.DatabaseType,
                    report.OverallConfidence,
                    report.HighImpactCount,
                    report.RequiresRestartCount
                },
                SerializerOptions)
        };
    }

    public WorkflowResultEnvelope ToEnvelope(
        string workflowType,
        JsonElement resultElement,
        string? databaseId = null,
        string? databaseType = null)
    {
        if (IsEnvelope(resultElement))
        {
            return resultElement.Deserialize<WorkflowResultEnvelope>(SerializerOptions)
                ?? CreateEmptyEnvelope(workflowType, databaseId, databaseType);
        }

        return workflowType switch
        {
            "DbConfigOptimization" => ToEnvelope(
                resultElement.Deserialize<ConfigOptimizationReport>(SerializerOptions)
                ?? CreateEmptyConfigReport(databaseId, databaseType)),
            _ => ToEnvelope(
                resultElement.Deserialize<OptimizationReport>(SerializerOptions) ?? new OptimizationReport(),
                databaseId ?? string.Empty,
                databaseType ?? string.Empty)
        };
    }

    public WorkflowResultEnvelope ToEnvelope(
        string workflowType,
        string json,
        string? databaseId = null,
        string? databaseType = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateEmptyEnvelope(workflowType, databaseId, databaseType);
        }

        using var document = JsonDocument.Parse(json);
        return ToEnvelope(workflowType, document.RootElement, databaseId, databaseType);
    }

    public int GetRecommendationCount(
        string workflowType,
        JsonElement resultElement,
        string? databaseId = null,
        string? databaseType = null)
    {
        var envelope = ToEnvelope(workflowType, resultElement, databaseId, databaseType);

        return envelope.ResultType switch
        {
            "db-config-optimization-report" => envelope.Data.Deserialize<ConfigOptimizationReport>(SerializerOptions)?.Recommendations.Count ?? 0,
            _ => envelope.Data.Deserialize<OptimizationReport>(SerializerOptions)?.IndexRecommendations.Count ?? 0
        };
    }

    private static bool IsEnvelope(JsonElement resultElement)
    {
        return resultElement.ValueKind == JsonValueKind.Object &&
               resultElement.TryGetProperty("resultType", out _) &&
               resultElement.TryGetProperty("displayName", out _) &&
               resultElement.TryGetProperty("summary", out _) &&
               resultElement.TryGetProperty("data", out _);
    }

    private static WorkflowResultEnvelope CreateEmptyEnvelope(
        string workflowType,
        string? databaseId,
        string? databaseType)
    {
        return workflowType switch
        {
            "DbConfigOptimization" => EmptySerializer.ToEnvelope(CreateEmptyConfigReport(databaseId, databaseType)),
            _ => EmptySerializer.ToEnvelope(new OptimizationReport(), databaseId ?? string.Empty, databaseType ?? string.Empty)
        };
    }

    private static ConfigOptimizationReport CreateEmptyConfigReport(string? databaseId, string? databaseType)
    {
        return new ConfigOptimizationReport
        {
            Summary = string.Empty,
            Recommendations = Array.Empty<ConfigRecommendation>(),
            OverallConfidence = 0,
            GeneratedAt = DateTimeOffset.UtcNow,
            DatabaseId = databaseId ?? string.Empty,
            DatabaseType = databaseType ?? string.Empty
        };
    }
}
