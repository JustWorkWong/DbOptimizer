using DbOptimizer.Infrastructure.SlowQuery;

namespace DbOptimizer.API.Api;

internal static class DashboardApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/stats", HandleGetDashboardStatsAsync);
        endpoints.MapGet("/api/dashboard/slow-query-trends", HandleGetSlowQueryTrendsAsync);
        endpoints.MapGet("/api/dashboard/slow-query-alerts", HandleGetSlowQueryAlertsAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetDashboardStatsAsync(
        IHistoryQueryService historyQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await historyQueryService.GetDashboardStatsAsync(cancellationToken);
        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleGetSlowQueryTrendsAsync(
        string? databaseId,
        int? days,
        ISlowQueryDashboardQueryService slowQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(databaseId))
        {
            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status400BadRequest,
                "INVALID_PARAMETER",
                "databaseId is required.",
                null);
        }

        var response = await slowQueryService.GetTrendAsync(
            databaseId,
            days ?? 7,
            cancellationToken);

        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleGetSlowQueryAlertsAsync(
        string? databaseId,
        string? status,
        ISlowQueryDashboardQueryService slowQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await slowQueryService.GetAlertsAsync(
            databaseId,
            status,
            cancellationToken);

        return ApiEnvelopeFactory.Success(httpContext, response);
    }
}
