using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

/// <summary>
/// 熔断器状态
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// 关闭（正常工作）
    /// </summary>
    Closed,

    /// <summary>
    /// 打开（熔断中）
    /// </summary>
    Open,

    /// <summary>
    /// 半开（尝试恢复）
    /// </summary>
    HalfOpen
}

/// <summary>
/// 熔断器配置
/// </summary>
public sealed record CircuitBreakerConfig
{
    /// <summary>
    /// 失败阈值（连续失败多少次后熔断）
    /// </summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>
    /// 成功阈值（半开状态下连续成功多少次后恢复）
    /// </summary>
    public int SuccessThreshold { get; init; } = 2;

    /// <summary>
    /// 熔断超时时间（毫秒）
    /// </summary>
    public int TimeoutMs { get; init; } = 30000;

    /// <summary>
    /// 采样窗口大小（秒）
    /// </summary>
    public int SamplingDurationSeconds { get; init; } = 60;
}

/// <summary>
/// 熔断器实现
/// </summary>
public sealed class CircuitBreaker
{
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount;
    private int _successCount;
    private DateTimeOffset _lastFailureTime;
    private DateTimeOffset _openedAt;

    public CircuitBreaker(CircuitBreakerConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// 执行操作（带熔断保护）
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        // 检查熔断器状态
        EnsureCircuitNotOpen(operationName);

        try
        {
            var result = await operation(cancellationToken);
            OnSuccess(operationName);
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(operationName, ex);
            throw;
        }
    }

    private void EnsureCircuitNotOpen(string operationName)
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                var elapsed = DateTimeOffset.UtcNow - _openedAt;
                if (elapsed.TotalMilliseconds < _config.TimeoutMs)
                {
                    throw new InvalidOperationException(
                        $"Circuit breaker is open for operation '{operationName}'. " +
                        $"Retry after {(_config.TimeoutMs - elapsed.TotalMilliseconds) / 1000:F1} seconds.");
                }

                // 超时后进入半开状态
                _state = CircuitBreakerState.HalfOpen;
                _successCount = 0;
                _logger.LogInformation(
                    "Circuit breaker entering half-open state. Operation={Operation}",
                    operationName);
            }
        }
    }

    private void OnSuccess(string operationName)
    {
        lock (_lock)
        {
            _failureCount = 0;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                _successCount++;
                if (_successCount >= _config.SuccessThreshold)
                {
                    _state = CircuitBreakerState.Closed;
                    _successCount = 0;
                    _logger.LogInformation(
                        "Circuit breaker closed after successful recovery. Operation={Operation}",
                        operationName);
                }
            }
        }
    }

    private void OnFailure(string operationName, Exception exception)
    {
        lock (_lock)
        {
            _lastFailureTime = DateTimeOffset.UtcNow;
            _failureCount++;

            var category = MafErrorClassifier.Classify(exception);

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // 半开状态下失败，立即重新打开
                _state = CircuitBreakerState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _successCount = 0;
                _logger.LogWarning(
                    exception,
                    "Circuit breaker re-opened after failure in half-open state. Operation={Operation}, ErrorCategory={Category}",
                    operationName,
                    category);
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount >= _config.FailureThreshold)
            {
                // 关闭状态下达到失败阈值，打开熔断器
                _state = CircuitBreakerState.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _logger.LogError(
                    exception,
                    "Circuit breaker opened due to repeated failures. Operation={Operation}, FailureCount={FailureCount}, ErrorCategory={Category}",
                    operationName,
                    _failureCount,
                    category);
            }
        }
    }

    /// <summary>
    /// 重置熔断器
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _successCount = 0;
            _logger.LogInformation("Circuit breaker manually reset");
        }
    }
}
