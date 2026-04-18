using FluentValidation;

namespace DbOptimizer.Infrastructure.Workflows.Application.Validators;

/// <summary>
/// SQL 分析工作流请求验证器
/// </summary>
public sealed class CreateSqlAnalysisWorkflowRequestValidator : AbstractValidator<CreateSqlAnalysisWorkflowRequest>
{
    public CreateSqlAnalysisWorkflowRequestValidator()
    {
        RuleFor(x => x.SqlText)
            .NotEmpty()
            .WithMessage("SqlText is required.")
            .MaximumLength(100_000)
            .WithMessage("SqlText exceeds maximum length of 100,000 characters.");

        RuleFor(x => x.DatabaseId)
            .NotEmpty()
            .WithMessage("DatabaseId is required.");

        RuleFor(x => x.DatabaseEngine)
            .Must(engine => engine == null || engine == "mysql" || engine == "postgresql")
            .WithMessage("DatabaseEngine must be 'mysql' or 'postgresql' when provided.");

        RuleFor(x => x.SourceType)
            .NotEmpty()
            .WithMessage("SourceType is required.")
            .Must(type => type == "manual" || type == "slow_query" || type == "scheduled")
            .WithMessage("SourceType must be one of: manual, slow_query, scheduled.");
    }
}
