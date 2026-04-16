using DbOptimizer.API.Checkpointing;
using DbOptimizer.API.DatabaseMigrations;
using DbOptimizer.API.Persistence;
using DbOptimizer.API.Workflows;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var postgreSqlConnectionString = ResolvePostgreSqlConnectionString(builder.Configuration);
var redisConnectionString = ResolveRedisConnectionString(builder.Configuration);
var executionPlanOptions = builder.Configuration.GetSection(ExecutionPlanOptions.SectionName).Get<ExecutionPlanOptions>()
    ?? CreateDefaultExecutionPlanOptions();

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
    options.UseNpgsql(postgreSqlConnectionString));
builder.Services.AddDbContextFactory<DbOptimizerDbContext>(options =>
    options.UseNpgsql(postgreSqlConnectionString));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configurationOptions = ConfigurationOptions.Parse(redisConnectionString);
    configurationOptions.AbortOnConnectFail = false;
    configurationOptions.ConnectRetry = 2;

    return ConnectionMultiplexer.Connect(configurationOptions);
});
builder.Services.AddSingleton<ICheckpointStorage, PostgresRedisCheckpointStorage>();
builder.Services.AddSingleton<IWorkflowEventPublisher, LoggingWorkflowEventPublisher>();
builder.Services.AddSingleton<IWorkflowStateMachine, WorkflowStateMachine>();
builder.Services.AddSingleton<IWorkflowRunner, WorkflowRunner>();
builder.Services.AddSingleton<ISqlParser, LightweightSqlParser>();
builder.Services.AddSingleton(executionPlanOptions);
builder.Services.AddSingleton<IExecutionPlanProvider, ExecutionPlanProvider>();
builder.Services.AddSingleton<IExecutionPlanAnalyzer, ExecutionPlanAnalyzer>();
builder.Services.AddSingleton<ITableIndexMetadataProvider, TableIndexMetadataProvider>();
builder.Services.AddSingleton<ITableIndexMetadataAnalyzer, TableIndexMetadataAnalyzer>();
builder.Services.AddSingleton<IIndexRecommendationGenerator, IndexRecommendationGenerator>();
builder.Services.AddSingleton<IWorkflowExecutor, SqlParserExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ExecutionPlanExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, IndexAdvisorExecutor>();
builder.Services.AddSingleton<MigrationReadinessState>();
builder.Services.AddHostedService<EfMigrationHostedService>();
builder.Services.AddHostedService<RunningWorkflowRecoveryHostedService>();

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

static string ResolveRedisConnectionString(IConfiguration configuration)
{
    var fromAspire = configuration.GetConnectionString("redis");
    if (!string.IsNullOrWhiteSpace(fromAspire))
    {
        return fromAspire;
    }

    var fallback = configuration.GetConnectionString("Redis");
    if (!string.IsNullOrWhiteSpace(fallback))
    {
        return fallback;
    }

    var nested = configuration["DbOptimizer:ConnectionStrings:Redis"];
    if (!string.IsNullOrWhiteSpace(nested))
    {
        return nested;
    }

    throw new InvalidOperationException("Missing Redis connection string: ConnectionStrings:redis or ConnectionStrings:Redis or DbOptimizer:ConnectionStrings:Redis");
}

static ExecutionPlanOptions CreateDefaultExecutionPlanOptions()
{
    return new ExecutionPlanOptions
    {
        MySql = new ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-mysql"
        },
        PostgreSql = new ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-postgres"
        },
        TimeoutSeconds = 30,
        RetryCount = 2,
        RetryDelayMilliseconds = 1000,
        EnableDirectDbFallback = true
    };
}
