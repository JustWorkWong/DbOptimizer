using FluentValidation;

namespace DbOptimizer.Infrastructure.Workflows.Application.Validators;

/// <summary>
/// 分页查询参数
/// </summary>
public sealed class PaginationQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public string? SortOrder { get; set; }
}

/// <summary>
/// 分页查询参数验证器
/// </summary>
public sealed class PaginationQueryValidator : AbstractValidator<PaginationQuery>
{
    private static readonly string[] AllowedSortFields =
    {
        "created_at",
        "updated_at",
        "status",
        "database_type",
        "workflow_type"
    };

    public PaginationQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.SortBy)
            .Must(sortBy => string.IsNullOrEmpty(sortBy) || AllowedSortFields.Contains(sortBy.ToLowerInvariant()))
            .When(x => !string.IsNullOrEmpty(x.SortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", AllowedSortFields)}.");

        RuleFor(x => x.SortOrder)
            .Must(order => string.IsNullOrEmpty(order) || order.ToLowerInvariant() == "asc" || order.ToLowerInvariant() == "desc")
            .When(x => !string.IsNullOrEmpty(x.SortOrder))
            .WithMessage("SortOrder must be 'asc' or 'desc'.");
    }
}
