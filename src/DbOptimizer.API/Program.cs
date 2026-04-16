using DbOptimizer.API.DatabaseMigrations;
using DbOptimizer.API.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId;
});

/* =========================
 * EF Core 迁移启动注册
 * - 启动时执行 Database.MigrateAsync()
 * - 迁移成功后再标记健康就绪
 * ========================= */
builder.Services.AddDbContext<DbOptimizerDbContext>(options =>
    options.UseNpgsql(ResolvePostgreSqlConnectionString(builder.Configuration)));
builder.Services.AddSingleton<MigrationReadinessState>();
builder.Services.AddHostedService<EfMigrationHostedService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var sessionId = GetHeaderValue(context, "X-Session-Id");
    var executionId = GetHeaderValue(context, "X-Execution-Id");

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["requestId"] = context.TraceIdentifier,
        ["sessionId"] = sessionId,
        ["executionId"] = executionId
    }))
    {
        await next();
    }
});

app.MapGet("/health", (MigrationReadinessState readinessState) =>
{
    var payload = new
    {
        status = readinessState.IsReady ? "ok" : "not_ready"
    };

    return readinessState.IsReady
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

return;

static string GetHeaderValue(HttpContext context, string headerName)
{
    if (!context.Request.Headers.TryGetValue(headerName, out var values))
    {
        return "-";
    }

    var value = values.ToString();
    return string.IsNullOrWhiteSpace(value) ? "-" : value;
}

static string ResolvePostgreSqlConnectionString(IConfiguration configuration)
{
    var fromAspire = configuration.GetConnectionString("dboptimizer-postgres");
    if (!string.IsNullOrWhiteSpace(fromAspire))
    {
        return fromAspire;
    }

    var fallback = configuration.GetConnectionString("PostgreSql");
    if (!string.IsNullOrWhiteSpace(fallback))
    {
        return fallback;
    }

    throw new InvalidOperationException("Missing PostgreSQL connection string: ConnectionStrings:dboptimizer-postgres or ConnectionStrings:PostgreSql");
}
