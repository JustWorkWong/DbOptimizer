using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

namespace DbOptimizer.Infrastructure.Tests.Maf.ErrorHandling;

public sealed class RetryPolicyTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly RetryPolicy _retryPolicy;

    public RetryPolicyTests()
    {
        _mockLogger = new Mock<ILogger>();
        var config = new RetryPolicyConfig
        {
            MaxRetryAttempts = 3,
            InitialDelayMs = 10,
            MaxDelayMs = 100,
            BackoffMultiplier = 2.0,
            EnableJitter = false // 禁用抖动以便测试
        };
        _retryPolicy = new RetryPolicy(config, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstAttempt_ReturnsResult()
    {
        // Arrange
        var expectedResult = 42;
        var attemptCount = 0;

        // Act
        var result = await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;
                return expectedResult;
            },
            "test-operation",
            CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);
        attemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWithNonRetryableError_ThrowsImmediately()
    {
        // Arrange
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _retryPolicy.ExecuteAsync<int>(
                async ct =>
                {
                    attemptCount++;
                    await Task.CompletedTask;
                    throw new ArgumentException("Validation error");
                },
                "test-operation",
                CancellationToken.None);
        });

        attemptCount.Should().Be(1); // 不应重试
    }

    [Fact]
    public async Task ExecuteAsync_FailsWithRetryableError_RetriesAndSucceeds()
    {
        // Arrange
        var attemptCount = 0;
        var expectedResult = 42;

        // Act
        var result = await _retryPolicy.ExecuteAsync(
            async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;

                if (attemptCount < 3)
                {
                    throw new TimeoutException("Timeout");
                }

                return expectedResult;
            },
            "test-operation",
            CancellationToken.None);

        // Assert
        result.Should().Be(expectedResult);
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_FailsAllRetries_ThrowsException()
    {
        // Arrange
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _retryPolicy.ExecuteAsync<int>(
                async ct =>
                {
                    attemptCount++;
                    await Task.CompletedTask;
                    throw new TimeoutException("Timeout");
                },
                "test-operation",
                CancellationToken.None);
        });

        attemptCount.Should().Be(3); // 应该重试 3 次
    }
}
