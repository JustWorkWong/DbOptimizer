var builder = WebApplication.CreateBuilder(args);

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId;
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var sessionId = context.Request.Headers.TryGetValue("X-Session-Id", out var sessionValues)
        ? sessionValues.ToString()
        : "-";
    var executionId = context.Request.Headers.TryGetValue("X-Execution-Id", out var executionValues)
        ? executionValues.ToString()
        : "-";

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["requestId"] = context.TraceIdentifier,
        ["sessionId"] = string.IsNullOrWhiteSpace(sessionId) ? "-" : sessionId,
        ["executionId"] = string.IsNullOrWhiteSpace(executionId) ? "-" : executionId
    }))
    {
        await next();
    }
});

app.MapGet("/health", (HttpContext context) =>
{
    var sessionId = context.Request.Headers.TryGetValue("X-Session-Id", out var sessionValues)
        ? sessionValues.ToString()
        : "-";
    var executionId = context.Request.Headers.TryGetValue("X-Execution-Id", out var executionValues)
        ? executionValues.ToString()
        : "-";

    return Results.Ok(new
    {
        status = "ok",
        requestId = context.TraceIdentifier,
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? "-" : sessionId,
        executionId = string.IsNullOrWhiteSpace(executionId) ? "-" : executionId
    });
});

app.Run();
