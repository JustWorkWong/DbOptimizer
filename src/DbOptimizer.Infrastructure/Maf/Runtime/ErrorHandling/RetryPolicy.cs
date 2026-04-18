using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

/// <summary>
/// 重试策略配置
/// </summary>
public sealed record RetryPolicyConfig
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// 初始延迟（毫秒）
    /// </summary>
    public int InitialDelayMs { get; init; } = 100;

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public int MaxDelayMs { get; init; } = 5000;

    /// <summary>
    /// 指数退避倍数
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// 是否启用抖动
    /// </summary>
    public bool EnableJitter { get; init; } = true;
}

/// <summary>
/// 重试策略执行器
/// </summary>
public sealed class RetryPolicy
{
    private readonly RetryPolicyConfig _config;
    private readonly ILogger _logger;
    private readonly Random _random = new();

    public RetryPolicy(RetryPolicyConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < _config.MaxRetryAttempts)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogInformation(
                        "Retrying operation. Operation={Operation}, Attempt={Attempt}/{MaxAttempts}",
                        operationName,
                        attempt + 1,
                        _config.MaxRetryAttempts);
                }

                return await operation(cancellationToken);
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                lastException = ex;
                attempt++;

                var category = MafErrorClassifier.Classify(ex);
                _logger.LogWarning(
                    ex,
                    "Operation failed, will retry. Operation={Operation}, Attempt={Attempt}/{MaxAttempts}, ErrorCategory={Category}",
                    operationName,
                    attempt,
                    _config.MaxRetryAttempts,
                    category);

                if (attempt < _config.MaxRetryAttempts)
                {
                    var delay = CalculateDelay(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        // 所有重试都失败
        _logger.LogError(
            lastException,
            "Operation failed after all retry attempts. Operation={Operation}, TotalAttempts={TotalAttempts}",
            operationName,
            attempt);

        throw new InvalidOperationException(
            $"Operation '{operationName}' failed after {attempt} attempts",
            lastException);
    }

    private bool ShouldRetry(Exception exception, int currentAttempt)
    {
        if (currentAttempt >= _config.MaxRetryAttempts)
        {
            return false;
        }

        var category = MafErrorClassifier.Classify(exception);
        return MafErrorClassifier.IsRetryable(category);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        // 指数退避：delay = initialDelay * (backoffMultiplier ^ attempt)
        var delayMs = _config.InitialDelayMs * Math.Pow(_config.BackoffMultiplier, attempt - 1);
        delayMs = Math.Min(delayMs, _config.MaxDelayMs);

        // 添加抖动，避免雷鸣羊群效应
        if (_config.EnableJitter)
        {
            var jitter = _random.NextDouble() * 0.3; // ±30% 抖动
            delayMs *= (1.0 + jitter - 0.15);
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }
}
