using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

namespace DbOptimizer.Infrastructure.Tests.Maf.ErrorHandling;

public sealed class CircuitBreakerTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly CircuitBreaker _circuitBreaker;

    public CircuitBreakerTests()
    {
        _mockLogger = new Mock<ILogger>();
        var config = new CircuitBreakerConfig
        {
            FailureThreshold = 3,
            SuccessThreshold = 2,
            TimeoutMs = 1000
        };
        _circuitBreaker = new CircuitBreaker(config, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_StateRemainsClosed()
    {
        // Arrange
        var expectedResult = 42;

        // Act
        var result = await _circuitBreaker.ExecuteAsync(
            async ct =>
            {
                await Task.CompletedTask;
                return expectedResult;
            },
            "test-operation",
            CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);
        _circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedFailures_OpensCircuit()
    {
        // Arrange & Act
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _circuitBreaker.ExecuteAsync<int>(
                    async ct =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Timeout");
                    },
                    "test-operation",
                    CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        // Assert
        _circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task ExecuteAsync_CircuitOpen_ThrowsImmediately()
    {
        // Arrange - 先打开熔断器
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _circuitBreaker.ExecuteAsync<int>(
                    async ct =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Timeout");
                    },
                    "test-operation",
                    CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _circuitBreaker.ExecuteAsync(
                async ct =>
                {
                    await Task.CompletedTask;
                    return 42;
                },
                "test-operation",
                CancellationToken.None);
        });
    }

    [Fact]
    public async Task ExecuteAsync_CircuitHalfOpen_SuccessfulOperationClosesCircuit()
    {
        // Arrange - 打开熔断器
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await _circuitBreaker.ExecuteAsync<int>(
                    async ct =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Timeout");
                    },
                    "test-operation",
                    CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        // 等待超时，进入半开状态
        await Task.Delay(1100);

        // Act - 成功执行 2 次（达到 SuccessThreshold）
        for (int i = 0; i < 2; i++)
        {
            await _circuitBreaker.ExecuteAsync(
                async ct =>
                {
                    await Task.CompletedTask;
                    return 42;
                },
                "test-operation",
                CancellationToken.None);
        }

        // Assert
        _circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public void Reset_OpensCircuit_ClosesCircuit()
    {
        // Arrange - 打开熔断器
        for (int i = 0; i < 3; i++)
        {
            try
            {
                _circuitBreaker.ExecuteAsync<int>(
                    async ct =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Timeout");
                    },
                    "test-operation",
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        _circuitBreaker.State.Should().Be(CircuitBreakerState.Open);

        // Act
        _circuitBreaker.Reset();

        // Assert
        _circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }
}
