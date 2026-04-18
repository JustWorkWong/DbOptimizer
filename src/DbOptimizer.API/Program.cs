using DbOptimizer.API.Api;
using DbOptimizer.API.DatabaseMigrations;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.SlowQuery;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Mcp;
using DbOptimizer.Infrastructure.Prompts;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var postgreSqlConnectionString = ResolvePostgreSqlConnectionString(builder.Configuration);
var redisConnectionString = ResolveRedisConnectionString(builder.Configuration);
var corsOrigins = builder.Configuration.GetSection("DbOptimizer:Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://127.0.0.1:5173", "http://localhost:5173"];
var workflowExecutionPlanOptions = builder.Configuration
    .GetSection(DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions.SectionName)
    .Get<DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions>()
    ?? CreateDefaultWorkflowExecutionPlanOptions();
var slowQueryExecutionPlanOptions = builder.Configuration
    .GetSection(DbOptimizer.Core.Models.ExecutionPlanOptions.SectionName)
    .Get<DbOptimizer.Core.Models.ExecutionPlanOptions>()
    ?? CreateDefaultSlowQueryExecutionPlanOptions();
var workflowRuntimeOptions = builder.Configuration.GetSection(WorkflowRuntimeOptions.SectionName).Get<WorkflowRuntimeOptions>()
    ?? new WorkflowRuntimeOptions();
var mafWorkflowRuntimeOptions = builder.Configuration.GetSection("MafWorkflowRuntime").Get<MafWorkflowRuntimeOptions>()
    ?? new MafWorkflowRuntimeOptions();
var configCollectionOptions = builder.Configuration.GetSection(ConfigCollectionOptions.SectionName).Get<ConfigCollectionOptions>()
    ?? new ConfigCollectionOptions();
var slowQueryCollectionOptions = builder.Configuration.GetSection(SlowQueryCollectionOptions.SectionName).Get<SlowQueryCollectionOptions>()
    ?? new SlowQueryCollectionOptions();
var tokenUsageRecorderOptions = builder.Configuration.GetSection("TokenUsageRecorder").Get<DbOptimizer.Infrastructure.Workflows.Monitoring.TokenUsageRecorderOptions>()
    ?? new DbOptimizer.Infrastructure.Workflows.Monitoring.TokenUsageRecorderOptions();

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId;
});

/* =========================
 * OpenTelemetry 配置
 * - Logs/Metrics/Traces 统一导出到 Aspire Dashboard
 * - 使用 OTLP 协议
 * ========================= */
var serviceName = "DbOptimizer.API";
var serviceVersion = "1.0.0";
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(serviceName, serviceVersion: serviceVersion));
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);
    });
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: serviceVersion))
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter("DbOptimizer.*")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("DbOptimizer.*")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
    });


builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend-dev", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DbOptimizer API",
        Version = "v1",
        Description = "Workflow, review, dashboard, and history endpoints for DbOptimizer."
    });
});

/* =========================
 * EF Core 迁移启动注册
 * - 启动时执行 Database.MigrateAsync()
 * - 迁移成功后再标记健康就绪
 * ========================= */
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
builder.Services.AddSingleton<WorkflowEventHub>();
builder.Services.AddSingleton<IWorkflowEventPublisher>(serviceProvider => serviceProvider.GetRequiredService<WorkflowEventHub>());
builder.Services.AddSingleton<IWorkflowEventQueryService>(serviceProvider => serviceProvider.GetRequiredService<WorkflowEventHub>());
builder.Services.AddSingleton<IWorkflowResultSerializer, WorkflowResultSerializer>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Application.IWorkflowApplicationService, DbOptimizer.Infrastructure.Workflows.Application.WorkflowApplicationService>();
builder.Services.AddSingleton<IReviewApplicationService, ReviewApplicationService>();
builder.Services.AddSingleton<IHistoryQueryService, HistoryQueryService>();

// Workflow 事件投影与监控服务
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Events.IMafWorkflowEventAdapter, DbOptimizer.Infrastructure.Workflows.Events.MafWorkflowEventAdapter>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Events.IWorkflowProgressCalculator, DbOptimizer.Infrastructure.Workflows.Events.WorkflowProgressCalculator>();
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(tokenUsageRecorderOptions));
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Monitoring.ITokenUsageRecorder, DbOptimizer.Infrastructure.Workflows.Monitoring.TokenUsageRecorder>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Projection.IWorkflowProjectionWriter, DbOptimizer.Infrastructure.Workflows.Projection.WorkflowProjectionWriter>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.ISqlParser, DbOptimizer.Infrastructure.Workflows.LightweightSqlParser>();
builder.Services.AddSingleton<DbOptimizer.Core.Models.ISqlParser, DbOptimizer.Core.Models.LightweightSqlParser>();
builder.Services.AddSingleton(workflowExecutionPlanOptions);
builder.Services.AddSingleton(slowQueryExecutionPlanOptions);
builder.Services.AddSingleton(workflowRuntimeOptions);
builder.Services.AddSingleton(configCollectionOptions);
builder.Services.AddSingleton(slowQueryCollectionOptions);

// MAF Workflow Runtime 服务注册
builder.Services.AddSingleton(mafWorkflowRuntimeOptions);
builder.Services.AddSingleton<IMafWorkflowFactory, MafWorkflowFactory>();
builder.Services.AddSingleton<IMafWorkflowRuntime, MafWorkflowRuntime>();
builder.Services.AddSingleton<IMafRunStateStore, MafRunStateStore>();
builder.Services.AddSingleton<IMafCheckpointStore, MafCheckpointStore>();
builder.Services.AddSingleton<IMcpFallbackStrategy, McpFallbackStrategy>();

// MCP 服务注册
var mcpOptions = builder.Configuration.GetSection("DbOptimizer:Mcp").Get<DbOptimizer.Infrastructure.Mcp.McpOptions>()
    ?? throw new InvalidOperationException("Missing required configuration section: DbOptimizer:Mcp");
var mySqlConnectionString = builder.Configuration.GetConnectionString("dboptimizer-mysql")
    ?? throw new InvalidOperationException("Missing MySQL connection string: ConnectionStrings:dboptimizer-mysql");
var mcpFallbackOptions = new DbOptimizer.Infrastructure.Mcp.McpFallbackOptions
{
    MySqlConnectionString = mySqlConnectionString,
    PostgreSqlConnectionString = postgreSqlConnectionString,
    TimeoutSeconds = mcpOptions.TimeoutSeconds
};
builder.Services.AddSingleton(mcpOptions);
builder.Services.AddSingleton(mcpFallbackOptions);
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Mcp.IDatabaseMcpFallbackExecutor, DbOptimizer.Infrastructure.Mcp.DatabaseMcpFallbackExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Mcp.MySqlMcpClient>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Mcp.PostgreSqlMcpClient>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Mcp.IMcpClientFactory, DbOptimizer.Infrastructure.Mcp.McpClientFactory>();

builder.Services.AddSingleton<IExecutionPlanProvider, ExecutionPlanProvider>();
builder.Services.AddSingleton<IExecutionPlanAnalyzer, ExecutionPlanAnalyzer>();
builder.Services.AddSingleton<ITableIndexMetadataProvider, TableIndexMetadataProvider>();
builder.Services.AddSingleton<ITableIndexMetadataAnalyzer, TableIndexMetadataAnalyzer>();
builder.Services.AddSingleton<IIndexRecommendationGenerator, IndexRecommendationGenerator>();
builder.Services.AddSingleton<IConfigCollectionProvider, ConfigCollectionProvider>();
builder.Services.AddSingleton<IConfigRule, MySqlBufferPoolRule>();
builder.Services.AddSingleton<IConfigRule, MySqlMaxConnectionsRule>();
builder.Services.AddSingleton<IConfigRule, PostgreSqlSharedBuffersRule>();
builder.Services.AddSingleton<IConfigRule, PostgreSqlWorkMemRule>();
builder.Services.AddSingleton<IConfigRule, MySqlQueryCacheRule>();
builder.Services.AddSingleton<IConfigRuleEngine, ConfigRuleEngine>();
builder.Services.AddSingleton<IReviewTaskService, ReviewTaskService>();
builder.Services.AddSingleton<IConfigReviewTaskService, ConfigReviewTaskService>();
builder.Services.AddSingleton<IPromptVersionService, PromptVersionService>();
builder.Services.AddSingleton<IWorkflowExecutor, SqlParserExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ExecutionPlanExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, IndexAdvisorExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, CoordinatorExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, HumanReviewExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, RegenerationExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ConfigCollectorExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ConfigAnalyzerExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ConfigCoordinatorExecutor>();
builder.Services.AddSingleton<IWorkflowExecutor, ConfigReviewExecutor>();

// MAF SQL Analysis Executors
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.ISqlRewriteAdvisor, DbOptimizer.Infrastructure.Maf.SqlAnalysis.NoOpSqlRewriteAdvisor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.ISqlReviewAdjustmentService, DbOptimizer.Infrastructure.Maf.SqlAnalysis.SqlReviewAdjustmentService>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.SqlInputValidationExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.SqlParserMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.ExecutionPlanMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.IndexAdvisorMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.SqlRewriteMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.SqlCoordinatorMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors.SqlHumanReviewGateExecutor>();

// MAF DB Config Executors
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.IConfigReviewAdjustmentService, DbOptimizer.Infrastructure.Maf.DbConfig.ConfigReviewAdjustmentService>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.Executors.DbConfigInputValidationExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.Executors.ConfigCollectorMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.Executors.ConfigAnalyzerMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.Executors.ConfigCoordinatorMafExecutor>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Maf.DbConfig.Executors.ConfigHumanReviewGateExecutor>();

// Workflow Review Services
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Review.IWorkflowReviewTaskGateway, DbOptimizer.Infrastructure.Workflows.Review.WorkflowReviewTaskGateway>();
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.Review.IWorkflowReviewResponseFactory, DbOptimizer.Infrastructure.Workflows.Review.WorkflowReviewResponseFactory>();

builder.Services.AddSingleton<ISlowQueryCollector, SlowQueryCollector>();
builder.Services.AddSingleton<ISlowQueryNormalizer, SlowQueryNormalizer>();
builder.Services.AddSingleton<ISlowQueryRepository, SlowQueryRepository>();
builder.Services.AddSingleton<ISlowQueryWorkflowSubmissionService, SlowQueryWorkflowSubmissionService>();
builder.Services.AddSingleton<ISlowQueryDashboardQueryService, SlowQueryDashboardQueryService>();
builder.Services.AddSingleton<MigrationReadinessState>();
builder.Services.AddHostedService<EfMigrationHostedService>();
builder.Services.AddHostedService<RunningWorkflowRecoveryHostedService>();
builder.Services.AddHostedService<SlowQueryCollectionService>();

var app = builder.Build();

app.UseCors("frontend-dev");
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DbOptimizer API v1");
    options.RoutePrefix = "swagger";
});

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

app.MapGet("/debug/connections", (IConfiguration config) =>
{
    return Results.Ok(new
    {
        postgres = MaskPassword(config.GetConnectionString("dboptimizer-postgres")),
        mysql = MaskPassword(config.GetConnectionString("dboptimizer-mysql")),
        redis = MaskPassword(config.GetConnectionString("redis")),
        postgresResolved = MaskPassword(postgreSqlConnectionString),
        redisResolved = MaskPassword(redisConnectionString)
    });

    static string? MaskPassword(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return connStr;
        return System.Text.RegularExpressions.Regex.Replace(
            connStr,
            @"(password|pwd)=([^;]+)",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
});

app.MapWorkflowApi();
app.MapReviewApi();
app.MapDashboardAndHistoryApi();
app.MapSlowQueryApi();
app.MapWorkflowEventsApi();
app.MapPromptVersionApi();

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

static DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions CreateDefaultWorkflowExecutionPlanOptions()
{
    return new DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions
    {
        MySql = new DbOptimizer.Infrastructure.Workflows.ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-mysql"
        },
        PostgreSql = new DbOptimizer.Infrastructure.Workflows.ExecutionPlanMcpServerOptions
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

static DbOptimizer.Core.Models.ExecutionPlanOptions CreateDefaultSlowQueryExecutionPlanOptions()
{
    return new DbOptimizer.Core.Models.ExecutionPlanOptions
    {
        MySql = new DbOptimizer.Core.Models.ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "npx",
            Arguments = "-y @modelcontextprotocol/server-mysql"
        },
        PostgreSql = new DbOptimizer.Core.Models.ExecutionPlanMcpServerOptions
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
