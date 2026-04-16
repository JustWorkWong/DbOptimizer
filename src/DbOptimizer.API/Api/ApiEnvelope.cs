using Microsoft.AspNetCore.Http;

namespace DbOptimizer.API.Api;

internal sealed record ApiError(string Code, string Message, object? Details = null);

internal sealed record ApiMeta(string RequestId, DateTimeOffset Timestamp);

internal sealed record ApiEnvelope<T>(bool Success, T? Data, ApiError? Error, ApiMeta Meta);

internal sealed class ApiException(int statusCode, string code, string message, object? details = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public object? Details { get; } = details;
}

internal static class ApiEnvelopeFactory
{
    public static IResult Success<T>(HttpContext httpContext, T data, int statusCode = StatusCodes.Status200OK)
    {
        return Results.Json(
            new ApiEnvelope<T>(
                true,
                data,
                null,
                new ApiMeta(httpContext.TraceIdentifier, DateTimeOffset.UtcNow)),
            statusCode: statusCode);
    }

    public static IResult Failure(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        object? details = null)
    {
        return Results.Json(
            new ApiEnvelope<object?>(
                false,
                null,
                new ApiError(code, message, details),
                new ApiMeta(httpContext.TraceIdentifier, DateTimeOffset.UtcNow)),
            statusCode: statusCode);
    }
}
