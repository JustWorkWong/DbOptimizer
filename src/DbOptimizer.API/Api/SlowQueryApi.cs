using DbOptimizer.Infrastructure.SlowQuery;

namespace DbOptimizer.API.Api;

internal static class SlowQueryApiRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSlowQueryApi(this IEndpointRouteBuilder endpoints)
    {
        var slowQueryGroup = endpoints.MapGroup("/api/slow-queries");
        slowQueryGroup.MapGet(string.Empty, HandleGetSlowQueriesAsync);
        slowQueryGroup.MapGet("/{queryId:guid}", HandleGetSlowQueryDetailAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleGetSlowQueriesAsync(
        string? databaseId,
        string? queryHash,
        int? page,
        int? pageSize,
        ISlowQueryDashboardQueryService slowQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await slowQueryService.GetSlowQueriesAsync(
            databaseId,
            queryHash,
            page ?? 1,
            pageSize ?? 20,
            cancellationToken);

        return ApiEnvelopeFactory.Success(httpContext, response);
    }

    private static async Task<IResult> HandleGetSlowQueryDetailAsync(
        Guid queryId,
        ISlowQueryDashboardQueryService slowQueryService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var response = await slowQueryService.GetSlowQueryAsync(queryId, cancellationToken);

        if (response is null)
        {
            return ApiEnvelopeFactory.Failure(
                httpContext,
                StatusCodes.Status404NotFound,
                "SLOW_QUERY_NOT_FOUND",
                "Slow query not found.",
                new { queryId });
        }

        return ApiEnvelopeFactory.Success(httpContext, response);
    }
}
