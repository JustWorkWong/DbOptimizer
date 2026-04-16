using DbOptimizer.AgentRuntime;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables("DBOPTIMIZER_");

var runtimeOptions = builder.Configuration.GetSection(RuntimeOptions.SectionName).Get<RuntimeOptions>()
    ?? throw new InvalidOperationException($"Missing required configuration section: {RuntimeOptions.SectionName}");

runtimeOptions = ResolveAiApiKeyFromEnvironment(runtimeOptions);
ValidateRuntimeOptions(runtimeOptions, builder.Environment);

builder.Services.AddSingleton(runtimeOptions);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

return;

static RuntimeOptions ResolveAiApiKeyFromEnvironment(RuntimeOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.Ai.ApiKey))
    {
        return options;
    }

    var keyFromEnv = Environment.GetEnvironmentVariable(options.Ai.ApiKeyEnvVar);
    if (string.IsNullOrWhiteSpace(keyFromEnv))
    {
        return options;
    }

    return options with
    {
        Ai = options.Ai with
        {
            ApiKey = keyFromEnv
        }
    };
}

static void ValidateRuntimeOptions(RuntimeOptions options, IHostEnvironment hostEnvironment)
{
    ThrowIfMissing(options.ConnectionStrings.PostgreSql, "DbOptimizer:ConnectionStrings:PostgreSql");
    ThrowIfMissing(options.ConnectionStrings.Redis, "DbOptimizer:ConnectionStrings:Redis");
    ThrowIfMissing(options.ConnectionStrings.MySql, "DbOptimizer:ConnectionStrings:MySql");

    ThrowIfMissing(options.Ai.Endpoint, "DbOptimizer:Ai:Endpoint");
    ThrowIfMissing(options.Ai.Model, "DbOptimizer:Ai:Model");
    ThrowIfMissing(options.Ai.ApiKeyEnvVar, "DbOptimizer:Ai:ApiKeyEnvVar");

    if (!hostEnvironment.IsDevelopment())
    {
        ThrowIfMissing(options.Ai.ApiKey, "DbOptimizer:Ai:ApiKey");
    }

    if (options.Ai.RequestTimeoutSeconds <= 0)
    {
        throw new InvalidOperationException("DbOptimizer:Ai:RequestTimeoutSeconds must be > 0");
    }

    if (options.Ai.MaxTokens <= 0)
    {
        throw new InvalidOperationException("DbOptimizer:Ai:MaxTokens must be > 0");
    }

    ThrowIfMissing(options.Mcp.MySql.Transport, "DbOptimizer:Mcp:MySql:Transport");
    ThrowIfMissing(options.Mcp.MySql.Command, "DbOptimizer:Mcp:MySql:Command");
    ThrowIfMissing(options.Mcp.PostgreSql.Transport, "DbOptimizer:Mcp:PostgreSql:Transport");
    ThrowIfMissing(options.Mcp.PostgreSql.Command, "DbOptimizer:Mcp:PostgreSql:Command");

    if (options.Mcp.TimeoutSeconds <= 0)
    {
        throw new InvalidOperationException("DbOptimizer:Mcp:TimeoutSeconds must be > 0");
    }

    if (options.Mcp.RetryCount < 0)
    {
        throw new InvalidOperationException("DbOptimizer:Mcp:RetryCount must be >= 0");
    }

    if (options.Workflow.StepTimeoutSeconds <= 0)
    {
        throw new InvalidOperationException("DbOptimizer:Workflow:StepTimeoutSeconds must be > 0");
    }

    if (options.Workflow.MaxRetryCount < 0)
    {
        throw new InvalidOperationException("DbOptimizer:Workflow:MaxRetryCount must be >= 0");
    }

    if (options.Workflow.RegenerationMaxRounds < 0)
    {
        throw new InvalidOperationException("DbOptimizer:Workflow:RegenerationMaxRounds must be >= 0");
    }
}

static void ThrowIfMissing(string value, string key)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required configuration value: {key}");
    }
}

public sealed record RuntimeOptions
{
    public const string SectionName = "DbOptimizer";

    public required RuntimeConnectionStrings ConnectionStrings { get; init; }

    public required AiOptions Ai { get; init; }

    public required McpOptions Mcp { get; init; }

    public required WorkflowOptions Workflow { get; init; }
}

public sealed record RuntimeConnectionStrings
{
    public required string PostgreSql { get; init; }

    public required string Redis { get; init; }

    public required string MySql { get; init; }
}

public sealed record AiOptions
{
    public required string Endpoint { get; init; }

    public required string Model { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public string ApiKeyEnvVar { get; init; } = "OPENAI_API_KEY";

    public int RequestTimeoutSeconds { get; init; }

    public int MaxTokens { get; init; }
}

public sealed record McpOptions
{
    public required McpServerOptions MySql { get; init; }

    public required McpServerOptions PostgreSql { get; init; }

    public int TimeoutSeconds { get; init; }

    public int RetryCount { get; init; }
}

public sealed record McpServerOptions
{
    public bool Enabled { get; init; }

    public required string Transport { get; init; }

    public required string Command { get; init; }

    public required string Arguments { get; init; }
}

public sealed record WorkflowOptions
{
    public int StepTimeoutSeconds { get; init; }

    public int MaxRetryCount { get; init; }

    public int RegenerationMaxRounds { get; init; }
}
