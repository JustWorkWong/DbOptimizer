using FluentValidation;

namespace DbOptimizer.Infrastructure.Workflows.Application.Validators;

/// <summary>
/// 数据库配置优化工作流请求验证器
/// </summary>
public sealed class CreateDbConfigOptimizationWorkflowRequestValidator : AbstractValidator<CreateDbConfigOptimizationWorkflowRequest>
{
    public CreateDbConfigOptimizationWorkflowRequestValidator()
    {
        RuleFor(x => x.DatabaseId)
            .NotEmpty()
            .WithMessage("DatabaseId is required.");

        RuleFor(x => x.DatabaseType)
            .NotEmpty()
            .WithMessage("DatabaseType is required.")
            .Must(type => type == "mysql" || type == "postgresql")
            .WithMessage("DatabaseType must be one of: mysql, postgresql.");
    }
}
