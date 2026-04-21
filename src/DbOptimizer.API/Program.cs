using DbOptimizer.API.Api;
using DbOptimizer.API.DatabaseMigrations;
using DbOptimizer.API.Mcp;
using DbOptimizer.API.Validators;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.SlowQuery;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Mcp;
using DbOptimizer.Infrastructure.Prompts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using System.Text.Json;

if (LocalDatabaseMcpServer.TryParse(args, out var localMcpEngine))
{
    await LocalDatabaseMcpServer.RunAsync(localMcpEngine);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var currentAssemblyPath = typeof(Program).Assembly.Location;
var postgreSqlConnectionString = ResolvePostgreSqlConnectionString(builder.Configuration);
var redisConnectionString = ResolveRedisConnectionString(builder.Configuration);
var allowLoopbackCorsOrigins = builder.Environment.IsDevelopment();
var corsOrigins = builder.Configuration.GetSection("DbOptimizer:Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://127.0.0.1:5173", "http://localhost:5173"];
var workflowExecutionPlanOptions = builder.Configuration
    .GetSection(DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions.SectionName)
    .Get<DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions>()
    ?? CreateDefaultWorkflowExecutionPlanOptions();
NormalizeWorkflowExecutionPlanOptions(workflowExecutionPlanOptions, currentAssemblyPath);
var slowQueryExecutionPlanOptions = builder.Configuration
    .GetSection(DbOptimizer.Core.Models.ExecutionPlanOptions.SectionName)
    .Get<DbOptimizer.Core.Models.ExecutionPlanOptions>()
    ?? CreateDefaultSlowQueryExecutionPlanOptions();
NormalizeSlowQueryExecutionPlanOptions(slowQueryExecutionPlanOptions, currentAssemblyPath);
var workflowRuntimeOptions = builder.Configuration.GetSection(WorkflowRuntimeOptions.SectionName).Get<WorkflowRuntimeOptions>()
    ?? new WorkflowRuntimeOptions();
var mafWorkflowRuntimeOptions = builder.Configuration.GetSection("MafWorkflowRuntime").Get<MafWorkflowRuntimeOptions>()
    ?? new MafWorkflowRuntimeOptions();
var workflowExecutionOptions = builder.Configuration.GetSection(DbOptimizer.Infrastructure.Workflows.WorkflowExecutionOptions.SectionName).Get<DbOptimizer.Infrastructure.Workflows.WorkflowExecutionOptions>()
    ?? new DbOptimizer.Infrastructure.Workflows.WorkflowExecutionOptions();
workflowExecutionOptions.ValidateForCurrentImplementation();
var configCollectionOptions = builder.Configuration.GetSection(ConfigCollectionOptions.SectionName).Get<ConfigCollectionOptions>()
    ?? new ConfigCollectionOptions();
NormalizeConfigCollectionOptions(configCollectionOptions, currentAssemblyPath);
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

// 密码安全：过滤包含敏感信息的日志
builder.Logging.AddFilter((category, level) =>
{
    // 过滤包含密码关键字的日志类别
    if (category != null && (
        category.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
        category.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
        category.Contains("Token", StringComparison.OrdinalIgnoreCase)))
    {
        return false;
    }
    return true;
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
            .AllowAnyHeader()
            .AllowAnyMethod();

        if (allowLoopbackCorsOrigins)
        {
            policy.SetIsOriginAllowed(origin =>
                IsAllowedLoopbackOrigin(origin) || corsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            policy.WithOrigins(corsOrigins);
        }
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
builder.Services.AddSingleton<DbOptimizer.Infrastructure.Workflows.IWorkflowExecutionAuditService, DbOptimizer.Infrastructure.Workflows.WorkflowExecutionAuditService>();
builder.Services.AddSingleton<DbOptimizer.Core.Models.ISqlParser, DbOptimizer.Core.Models.LightweightSqlParser>();
builder.Services.AddSingleton(workflowExecutionPlanOptions);
builder.Services.AddSingleton(slowQueryExecutionPlanOptions);
builder.Services.AddSingleton(workflowRuntimeOptions);
builder.Services.AddSingleton(workflowExecutionOptions);
builder.Services.AddSingleton(configCollectionOptions);
builder.Services.AddSingleton(slowQueryCollectionOptions);

// MAF Workflow Runtime 服务注册
builder.Services.AddSingleton(mafWorkflowRuntimeOptions);
builder.Services.AddSingleton<IWorkflowExecutionConcurrencyGate, WorkflowExecutionConcurrencyGate>();
builder.Services.AddSingleton<IMafWorkflowFactory, MafWorkflowFactory>();
builder.Services.AddSingleton<IMafExecutorInstrumentation, MafExecutorInstrumentation>();
builder.Services.AddSingleton<ICheckpointStore<JsonElement>, MafJsonCheckpointStore>();
builder.Services.AddSingleton<CheckpointManager>(serviceProvider =>
    CheckpointManager.CreateJson(serviceProvider.GetRequiredService<ICheckpointStore<JsonElement>>()));
builder.Services.AddSingleton<IMafWorkflowRuntime, MafWorkflowRuntime>();
builder.Services.AddSingleton<IMafRunStateStore, MafRunStateStore>();
builder.Services.AddSingleton<IMafCheckpointStore, MafCheckpointStore>();
builder.Services.AddSingleton<IMcpFallbackStrategy, McpFallbackStrategy>();

// MCP 服务注册
var mcpOptions = builder.Configuration.GetSection("DbOptimizer:Mcp").Get<DbOptimizer.Infrastructure.Mcp.McpOptions>()
    ?? throw new InvalidOperationException("Missing required configuration section: DbOptimizer:Mcp");
mcpOptions = NormalizeMcpOptions(mcpOptions, currentAssemblyPath);
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

// FluentValidation 验证器注册
builder.Services.AddValidatorsFromAssemblyContaining<DbOptimizer.Infrastructure.Workflows.Application.Validators.CreateSqlAnalysisWorkflowRequestValidator>();
builder.Services.AddScoped<IValidator<SubmitReviewRequest>, ApiSubmitReviewRequestValidator>();
builder.Services.AddSingleton<IConfigRule, MySqlMaxConnectionsRule>();
builder.Services.AddSingleton<IConfigRule, PostgreSqlSharedBuffersRule>();
builder.Services.AddSingleton<IConfigRule, PostgreSqlWorkMemRule>();
builder.Services.AddSingleton<IConfigRule, MySqlQueryCacheRule>();
builder.Services.AddSingleton<IConfigRuleEngine, ConfigRuleEngine>();
builder.Services.AddSingleton<IReviewTaskService, ReviewTaskService>();
builder.Services.AddSingleton<IConfigReviewTaskService, ConfigReviewTaskService>();
builder.Services.AddSingleton<IPromptVersionService, PromptVersionService>();

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

app.MapGet("/debug/connections", (IConfiguration config, IWebHostEnvironment environment) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        postgresConfigured = !string.IsNullOrWhiteSpace(config.GetConnectionString("dboptimizer-postgres")),
        mysqlConfigured = !string.IsNullOrWhiteSpace(config.GetConnectionString("dboptimizer-mysql")),
        redisConfigured = !string.IsNullOrWhiteSpace(config.GetConnectionString("redis")),
        postgresResolvedConfigured = !string.IsNullOrWhiteSpace(postgreSqlConnectionString),
        redisResolvedConfigured = !string.IsNullOrWhiteSpace(redisConnectionString)
    });
});

app.MapWorkflowApi();
app.MapReviewApi();
app.MapDashboardApi();
app.MapHistoryApi();
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

static bool IsAllowedLoopbackOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return uri.IsLoopback
        && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));
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
            Command = "dotnet",
            Arguments = LocalDatabaseMcpServer.BuildArguments(typeof(Program).Assembly.Location, "mysql")
        },
        PostgreSql = new DbOptimizer.Infrastructure.Workflows.ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "dotnet",
            Arguments = LocalDatabaseMcpServer.BuildArguments(typeof(Program).Assembly.Location, "postgresql")
        },
        TimeoutSeconds = 30,
        RetryCount = 2,
        RetryDelayMilliseconds = 1000,
        EnableDirectDbFallback = false
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
            Command = "dotnet",
            Arguments = LocalDatabaseMcpServer.BuildArguments(typeof(Program).Assembly.Location, "mysql")
        },
        PostgreSql = new DbOptimizer.Core.Models.ExecutionPlanMcpServerOptions
        {
            Enabled = true,
            Transport = "stdio",
            Command = "dotnet",
            Arguments = LocalDatabaseMcpServer.BuildArguments(typeof(Program).Assembly.Location, "postgresql")
        },
        TimeoutSeconds = 30,
        RetryCount = 2,
        RetryDelayMilliseconds = 1000,
        EnableDirectDbFallback = false
    };
}

static void NormalizeWorkflowExecutionPlanOptions(DbOptimizer.Infrastructure.Workflows.ExecutionPlanOptions options, string assemblyPath)
{
    NormalizeWorkflowServerOptions(options.MySql, "mysql", assemblyPath);
    NormalizeWorkflowServerOptions(options.PostgreSql, "postgresql", assemblyPath);
}

static void NormalizeSlowQueryExecutionPlanOptions(DbOptimizer.Core.Models.ExecutionPlanOptions options, string assemblyPath)
{
    NormalizeSlowQueryServerOptions(options.MySql, "mysql", assemblyPath);
    NormalizeSlowQueryServerOptions(options.PostgreSql, "postgresql", assemblyPath);
}

static void NormalizeConfigCollectionOptions(ConfigCollectionOptions options, string assemblyPath)
{
    NormalizeConfigCollectionServerOptions(options.MySql, "mysql", assemblyPath);
    NormalizeConfigCollectionServerOptions(options.PostgreSql, "postgresql", assemblyPath);
}

static DbOptimizer.Infrastructure.Mcp.McpOptions NormalizeMcpOptions(DbOptimizer.Infrastructure.Mcp.McpOptions options, string assemblyPath)
{
    return options with
    {
        MySql = NormalizeMcpServerOptions(options.MySql, "mysql", assemblyPath),
        PostgreSql = NormalizeMcpServerOptions(options.PostgreSql, "postgresql", assemblyPath)
    };
}

static void NormalizeWorkflowServerOptions(DbOptimizer.Infrastructure.Workflows.ExecutionPlanMcpServerOptions serverOptions, string engine, string assemblyPath)
{
    if (!LocalDatabaseMcpServer.ShouldUseLocalServer(serverOptions.Command, serverOptions.Arguments))
    {
        return;
    }

    serverOptions.Command = "dotnet";
    serverOptions.Arguments = LocalDatabaseMcpServer.BuildArguments(assemblyPath, engine);
}

static void NormalizeConfigCollectionServerOptions(ConfigCollectionMcpServerOptions serverOptions, string engine, string assemblyPath)
{
    if (!LocalDatabaseMcpServer.ShouldUseLocalServer(serverOptions.Command, serverOptions.Arguments))
    {
        return;
    }

    serverOptions.Command = "dotnet";
    serverOptions.Arguments = LocalDatabaseMcpServer.BuildArguments(assemblyPath, engine);
}

static void NormalizeSlowQueryServerOptions(DbOptimizer.Core.Models.ExecutionPlanMcpServerOptions serverOptions, string engine, string assemblyPath)
{
    if (!LocalDatabaseMcpServer.ShouldUseLocalServer(serverOptions.Command, serverOptions.Arguments))
    {
        return;
    }

    serverOptions.Command = "dotnet";
    serverOptions.Arguments = LocalDatabaseMcpServer.BuildArguments(assemblyPath, engine);
}

static DbOptimizer.Infrastructure.Mcp.McpServerOptions NormalizeMcpServerOptions(DbOptimizer.Infrastructure.Mcp.McpServerOptions serverOptions, string engine, string assemblyPath)
{
    if (!LocalDatabaseMcpServer.ShouldUseLocalServer(serverOptions.Command, serverOptions.Arguments))
    {
        return serverOptions;
    }

    return serverOptions with
    {
        Command = "dotnet",
        Arguments = LocalDatabaseMcpServer.BuildArguments(assemblyPath, engine)
    };
}
