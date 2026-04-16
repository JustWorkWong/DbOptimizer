namespace DbOptimizer.AgentRuntime;

public class Worker(ILogger<Worker> logger, RuntimeOptions runtimeOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                using var scope = logger.BeginScope(new Dictionary<string, object>
                {
                    ["requestId"] = "-",
                    ["sessionId"] = "runtime-heartbeat",
                    ["executionId"] = "runtime-heartbeat"
                });

                logger.LogInformation(
                    "AgentRuntime heartbeat. AI Endpoint={AiEndpoint}, Model={AiModel}, MCP Timeout={McpTimeoutSeconds}s, Workflow Retry={WorkflowRetryCount}, RegenerationMaxRounds={RegenerationMaxRounds}",
                    runtimeOptions.Ai.Endpoint,
                    runtimeOptions.Ai.Model,
                    runtimeOptions.Mcp.TimeoutSeconds,
                    runtimeOptions.Workflow.MaxRetryCount,
                    runtimeOptions.Workflow.RegenerationMaxRounds);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
