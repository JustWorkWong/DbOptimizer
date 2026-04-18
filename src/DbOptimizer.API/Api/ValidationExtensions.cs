using FluentValidation;

namespace DbOptimizer.API.Api;

/// <summary>
/// FluentValidation 扩展方法
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// 验证请求并返回 API 响应
    /// </summary>
    public static async Task<IResult> ValidateAndExecuteAsync<TRequest>(
        this IValidator<TRequest> validator,
        TRequest request,
        HttpContext httpContext,
        Func<Task<IResult>> executeFunc)
    {
        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );

            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "One or more validation errors occurred.",
                errors);
        }

        return await executeFunc();
    }

    /// <summary>
    /// 验证请求（同步版本）
    /// </summary>
    public static IResult ValidateAndExecute<TRequest>(
        this IValidator<TRequest> validator,
        TRequest request,
        HttpContext httpContext,
        Func<IResult> executeFunc)
    {
        var validationResult = validator.Validate(request);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );

            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "One or more validation errors occurred.",
                errors);
        }

        return executeFunc();
    }
}
