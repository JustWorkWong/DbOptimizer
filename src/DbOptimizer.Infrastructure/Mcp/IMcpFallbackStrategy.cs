namespace DbOptimizer.Infrastructure.Mcp;

/// <summary>
/// MCP 降级策略接口，用于处理 MCP 调用失败时的降级逻辑
/// </summary>
public interface IMcpFallbackStrategy
{
    /// <summary>
    /// 执行带降级的 MCP 操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="primaryAction">主要 MCP 操作</param>
    /// <param name="fallbackAction">降级操作（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    Task<McpFallbackResult<T>> ExecuteWithFallbackAsync<T>(
        Func<CancellationToken, Task<T>> primaryAction,
        Func<CancellationToken, Task<T>>? fallbackAction = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MCP 降级结果
/// </summary>
public sealed record McpFallbackResult<T>(
    T? Value,
    bool IsSuccess,
    bool UsedFallback,
    string? ErrorMessage = null);
