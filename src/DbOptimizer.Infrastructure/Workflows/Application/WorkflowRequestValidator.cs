namespace DbOptimizer.Infrastructure.Workflows.Application;

/// <summary>
/// Workflow 请求验证器
/// </summary>
public static class WorkflowRequestValidator
{
    public static ValidationResult ValidateCreateSqlAnalysisRequest(CreateSqlAnalysisWorkflowRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.SqlText))
        {
            errors.Add("SqlText is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DatabaseId))
        {
            errors.Add("DatabaseId is required.");
        }

        if (request.SqlText?.Length > 100_000)
        {
            errors.Add("SqlText exceeds maximum length of 100,000 characters.");
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    public static ValidationResult ValidateCreateDbConfigOptimizationRequest(CreateDbConfigOptimizationWorkflowRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.DatabaseId))
        {
            errors.Add("DatabaseId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DatabaseType))
        {
            errors.Add("DatabaseType is required.");
        }

        var validTypes = new[] { "mysql", "postgresql" };
        if (!string.IsNullOrWhiteSpace(request.DatabaseType) &&
            !validTypes.Contains(request.DatabaseType.ToLowerInvariant()))
        {
            errors.Add($"DatabaseType must be one of: {string.Join(", ", validTypes)}.");
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}

/// <summary>
/// 验证结果
/// </summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success() => new(true, Array.Empty<string>());
    public static ValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}

/// <summary>
/// SQL 分析 Workflow 请求
/// </summary>
public sealed class CreateSqlAnalysisWorkflowRequest
{
    public string SqlText { get; init; } = string.Empty;
    public string DatabaseId { get; init; } = string.Empty;
    public string? DatabaseEngine { get; init; }
    public SqlAnalysisWorkflowOptions Options { get; init; } = new();
}

/// <summary>
/// 数据库配置优化 Workflow 请求
/// </summary>
public sealed class CreateDbConfigOptimizationWorkflowRequest
{
    public string DatabaseId { get; init; } = string.Empty;
    public string DatabaseType { get; init; } = string.Empty;
    public DbConfigWorkflowOptions Options { get; init; } = new();
}

/// <summary>
/// SQL 分析 Workflow 选项
/// </summary>
public sealed class SqlAnalysisWorkflowOptions
{
    public bool EnableIndexRecommendation { get; init; } = true;
    public bool EnableSqlRewrite { get; init; } = true;
    public bool RequireHumanReview { get; init; } = true;
}

/// <summary>
/// 数据库配置优化 Workflow 选项
/// </summary>
public sealed class DbConfigWorkflowOptions
{
    public bool AllowFallbackSnapshot { get; init; } = true;
    public bool RequireHumanReview { get; init; } = true;
}
