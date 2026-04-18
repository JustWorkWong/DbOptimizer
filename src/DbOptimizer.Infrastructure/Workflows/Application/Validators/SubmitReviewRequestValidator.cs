using FluentValidation;

namespace DbOptimizer.Infrastructure.Workflows.Application.Validators;

/// <summary>
/// 审核提交请求
/// </summary>
public sealed class SubmitReviewRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string? Modifications { get; set; }
}

/// <summary>
/// 审核提交请求验证器
/// </summary>
public sealed class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .WithMessage("Action is required.")
            .Must(action => action == "approve" || action == "reject")
            .WithMessage("Action must be 'approve' or 'reject'.");

        RuleFor(x => x.Comment)
            .NotEmpty()
            .When(x => x.Action == "reject")
            .WithMessage("Comment is required when rejecting.");

        RuleFor(x => x.Modifications)
            .Must(BeValidJson)
            .When(x => !string.IsNullOrEmpty(x.Modifications))
            .WithMessage("Modifications must be valid JSON when provided.");
    }

    private static bool BeValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
