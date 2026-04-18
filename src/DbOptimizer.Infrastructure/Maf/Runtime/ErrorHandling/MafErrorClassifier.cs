using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

/// <summary>
/// MAF 错误分类器
/// </summary>
public static class MafErrorClassifier
{
    /// <summary>
    /// 分类异常
    /// </summary>
    public static MafErrorCategory Classify(Exception exception)
    {
        return exception switch
        {
            // 验证错误（子类在前）
            ArgumentNullException => MafErrorCategory.ValidationError,
            ArgumentException => MafErrorCategory.ValidationError,
            InvalidOperationException => MafErrorCategory.BusinessLogicError,

            // 网络相关
            SocketException => MafErrorCategory.NetworkError,
            HttpRequestException => MafErrorCategory.NetworkError,

            // Redis 相关（在超时前检查，因为 RedisTimeoutException 继承自 TimeoutException）
            RedisTimeoutException => MafErrorCategory.TimeoutError,
            RedisConnectionException => MafErrorCategory.RedisError,
            RedisException => MafErrorCategory.RedisError,

            // 超时相关（子类在前）
            TaskCanceledException => MafErrorCategory.TimeoutError,
            OperationCanceledException => MafErrorCategory.TimeoutError,
            TimeoutException => MafErrorCategory.TimeoutError,

            // 数据库相关
            DbUpdateException => MafErrorCategory.DatabaseError,
            NpgsqlException npgsqlEx => ClassifyNpgsqlException(npgsqlEx),

            _ => MafErrorCategory.Unknown
        };
    }

    /// <summary>
    /// 判断错误是否可重试
    /// </summary>
    public static bool IsRetryable(MafErrorCategory category)
    {
        return category switch
        {
            MafErrorCategory.NetworkError => true,
            MafErrorCategory.TimeoutError => true,
            MafErrorCategory.McpError => true,
            MafErrorCategory.RedisError => true,
            MafErrorCategory.ResourceExhaustedError => true,
            MafErrorCategory.DatabaseError => true, // 部分数据库错误可重试
            _ => false
        };
    }

    /// <summary>
    /// 获取用户友好的错误消息
    /// </summary>
    public static string GetUserFriendlyMessage(MafErrorCategory category, Exception exception)
    {
        return category switch
        {
            MafErrorCategory.ValidationError => "输入数据验证失败，请检查输入参数",
            MafErrorCategory.NetworkError => "网络连接失败，请稍后重试",
            MafErrorCategory.TimeoutError => "操作超时，请稍后重试",
            MafErrorCategory.DatabaseError => "数据库操作失败，请稍后重试",
            MafErrorCategory.McpError => "MCP 服务调用失败，请稍后重试",
            MafErrorCategory.RedisError => "缓存服务暂时不可用，请稍后重试",
            MafErrorCategory.BusinessLogicError => "业务逻辑错误，请联系管理员",
            MafErrorCategory.ConfigurationError => "系统配置错误，请联系管理员",
            MafErrorCategory.ResourceExhaustedError => "系统资源不足，请稍后重试",
            _ => "系统错误，请联系管理员"
        };
    }

    private static MafErrorCategory ClassifyNpgsqlException(NpgsqlException ex)
    {
        // PostgreSQL 错误码分类
        // 参考：https://www.postgresql.org/docs/current/errcodes-appendix.html
        return ex.SqlState switch
        {
            // 连接异常
            "08000" or "08003" or "08006" => MafErrorCategory.NetworkError,

            // 超时
            "57014" => MafErrorCategory.TimeoutError,

            // 资源不足
            "53000" or "53100" or "53200" or "53300" or "53400" => MafErrorCategory.ResourceExhaustedError,

            _ => MafErrorCategory.DatabaseError
        };
    }
}
