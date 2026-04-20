using DbOptimizer.API.Api;
using FluentValidation;

namespace DbOptimizer.API.Validators;

internal sealed class ApiSubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    private const int MaxCommentLength = 2000;
    private const int MaxAdjustmentsCount = 100;

    public ApiSubmitReviewRequestValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .WithMessage("Action is required.")
            .Must(action => action == "approve" || action == "reject" || action == "adjust")
            .WithMessage("Action must be 'approve', 'reject', or 'adjust'.");

        RuleFor(x => x.Comment)
            .MaximumLength(MaxCommentLength)
            .When(x => !string.IsNullOrEmpty(x.Comment))
            .WithMessage($"Comment must be at most {MaxCommentLength} characters.");

        RuleFor(x => x.Comment)
            .NotEmpty()
            .When(x => string.Equals(x.Action, "reject", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Comment is required when rejecting.");

        RuleFor(x => x.Adjustments)
            .Must(adjustments => adjustments is null || adjustments.Count <= MaxAdjustmentsCount)
            .WithMessage($"Adjustments must contain at most {MaxAdjustmentsCount} entries.");
    }
}
