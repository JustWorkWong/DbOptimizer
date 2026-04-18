namespace DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

/// <summary>
/// MAF 错误分类
/// </summary>
public enum MafErrorCategory
{
    /// <summary>
    /// 未知错误
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 输入验证错误（不可重试）
    /// </summary>
    ValidationError = 1,

    /// <summary>
    /// 网络错误（可重试）
    /// </summary>
    NetworkError = 2,

    /// <summary>
    /// 超时错误（可重试）
    /// </summary>
    TimeoutError = 3,

    /// <summary>
    /// 数据库错误（部分可重试）
    /// </summary>
    DatabaseError = 4,

    /// <summary>
    /// MCP 调用错误（可重试）
    /// </summary>
    McpError = 5,

    /// <summary>
    /// Redis 错误（可重试）
    /// </summary>
    RedisError = 6,

    /// <summary>
    /// 业务逻辑错误（不可重试）
    /// </summary>
    BusinessLogicError = 7,

    /// <summary>
    /// 配置错误（不可重试）
    /// </summary>
    ConfigurationError = 8,

    /// <summary>
    /// 资源不足错误（可重试）
    /// </summary>
    ResourceExhaustedError = 9
}
