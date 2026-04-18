using FluentValidation;

namespace DbOptimizer.Infrastructure.Workflows.Application.Validators;

/// <summary>
/// 历史查询参数
/// </summary>
public sealed class HistoryQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? WorkflowType { get; set; }
    public string? Status { get; set; }
    public string? DatabaseType { get; set; }
}

/// <summary>
/// 历史查询参数验证器
/// </summary>
public sealed class HistoryQueryValidator : AbstractValidator<HistoryQuery>
{
    public HistoryQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.WorkflowType)
            .Must(type => string.IsNullOrEmpty(type) || type == "sql_analysis" || type == "config_optimization")
            .When(x => !string.IsNullOrEmpty(x.WorkflowType))
            .WithMessage("WorkflowType must be 'sql_analysis' or 'config_optimization'.");

        RuleFor(x => x.Status)
            .Must(status => string.IsNullOrEmpty(status) ||
                           status == "pending" ||
                           status == "running" ||
                           status == "waiting_review" ||
                           status == "completed" ||
                           status == "failed" ||
                           status == "cancelled")
            .When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage("Status must be one of: pending, running, waiting_review, completed, failed, cancelled.");

        RuleFor(x => x.DatabaseType)
            .Must(type => string.IsNullOrEmpty(type) || type == "mysql" || type == "postgresql")
            .When(x => !string.IsNullOrEmpty(x.DatabaseType))
            .WithMessage("DatabaseType must be 'mysql' or 'postgresql'.");
    }
}
