using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using DbOptimizer.Infrastructure.Maf.Runtime.ErrorHandling;

namespace DbOptimizer.Infrastructure.Tests.Maf.ErrorHandling;

public sealed class MafErrorClassifierTests
{
    [Fact]
    public void Classify_ArgumentException_ReturnsValidationError()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var category = MafErrorClassifier.Classify(exception);

        // Assert
        category.Should().Be(MafErrorCategory.ValidationError);
    }

    [Fact]
    public void Classify_TimeoutException_ReturnsTimeoutError()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");

        // Act
        var category = MafErrorClassifier.Classify(exception);

        // Assert
        category.Should().Be(MafErrorCategory.TimeoutError);
    }

    [Fact]
    public void Classify_TaskCanceledException_ReturnsTimeoutError()
    {
        // Arrange
        var exception = new TaskCanceledException("Task was canceled");

        // Act
        var category = MafErrorClassifier.Classify(exception);

        // Assert
        category.Should().Be(MafErrorCategory.TimeoutError);
    }

    [Fact]
    public void Classify_InvalidOperationException_ReturnsBusinessLogicError()
    {
        // Arrange
        var exception = new InvalidOperationException("Invalid operation");

        // Act
        var category = MafErrorClassifier.Classify(exception);

        // Assert
        category.Should().Be(MafErrorCategory.BusinessLogicError);
    }

    [Fact]
    public void Classify_UnknownException_ReturnsUnknown()
    {
        // Arrange
        var exception = new Exception("Unknown error");

        // Act
        var category = MafErrorClassifier.Classify(exception);

        // Assert
        category.Should().Be(MafErrorCategory.Unknown);
    }

    [Theory]
    [InlineData(MafErrorCategory.NetworkError, true)]
    [InlineData(MafErrorCategory.TimeoutError, true)]
    [InlineData(MafErrorCategory.McpError, true)]
    [InlineData(MafErrorCategory.RedisError, true)]
    [InlineData(MafErrorCategory.ValidationError, false)]
    [InlineData(MafErrorCategory.BusinessLogicError, false)]
    [InlineData(MafErrorCategory.ConfigurationError, false)]
    public void IsRetryable_ReturnsExpectedResult(MafErrorCategory category, bool expectedRetryable)
    {
        // Act
        var isRetryable = MafErrorClassifier.IsRetryable(category);

        // Assert
        isRetryable.Should().Be(expectedRetryable);
    }

    [Fact]
    public void GetUserFriendlyMessage_ValidationError_ReturnsUserFriendlyMessage()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var message = MafErrorClassifier.GetUserFriendlyMessage(
            MafErrorCategory.ValidationError,
            exception);

        // Assert
        message.Should().Contain("验证失败");
    }

    [Fact]
    public void GetUserFriendlyMessage_NetworkError_ReturnsUserFriendlyMessage()
    {
        // Arrange
        var exception = new Exception("Network error");

        // Act
        var message = MafErrorClassifier.GetUserFriendlyMessage(
            MafErrorCategory.NetworkError,
            exception);

        // Assert
        message.Should().Contain("网络连接失败");
    }
}
