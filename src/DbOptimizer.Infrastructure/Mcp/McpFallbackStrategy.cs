using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Mcp;

/// <summary>
/// MCP 降级策略实现
/// </summary>
public sealed class McpFallbackStrategy : IMcpFallbackStrategy
{
    private readonly ILogger<McpFallbackStrategy> _logger;

    public McpFallbackStrategy(ILogger<McpFallbackStrategy> logger)
    {
        _logger = logger;
    }

    public async Task<McpFallbackResult<T>> ExecuteWithFallbackAsync<T>(
        Func<CancellationToken, Task<T>> primaryAction,
        Func<CancellationToken, Task<T>>? fallbackAction = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await primaryAction(cancellationToken);
            return new McpFallbackResult<T>(result, true, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary MCP action failed, attempting fallback");

            if (fallbackAction is null)
            {
                return new McpFallbackResult<T>(default, false, false, ex.Message);
            }

            try
            {
                var fallbackResult = await fallbackAction(cancellationToken);
                _logger.LogInformation("Fallback action succeeded");
                return new McpFallbackResult<T>(fallbackResult, true, true);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback action also failed");
                return new McpFallbackResult<T>(default, false, true, fallbackEx.Message);
            }
        }
    }
}
