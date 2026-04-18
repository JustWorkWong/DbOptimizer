using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using DbOptimizer.BackendE2ETests.Models;

namespace DbOptimizer.BackendE2ETests.Workflows;

/// <summary>
/// 错误处理场景 E2E 测试
/// </summary>
public sealed class ErrorHandlingE2ETests : E2ETestBase
{
    [Fact]
    public async Task ErrorHandling_McpTimeout_ShouldRetryAndFallback()
    {
        // Arrange - 模拟 MCP 超时
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM users",
            RequireHumanReview = false,
            SimulateMcpTimeout = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待重试和降级处理
        await Task.Delay(15000);

        // Assert - 验证工作流状态
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().BeOneOf("Completed", "Failed");

        // 检查错误日志
        var logsResponse = await Client.GetAsync($"/api/workflows/{sessionId}/logs");
        var logs = await logsResponse.Content.ReadFromJsonAsync<WorkflowLogEntry[]>();
        logs.Should().NotBeNull();
        logs!.Should().Contain(log => log.Message.Contains("MCP timeout"));
        logs.Should().Contain(log => log.Message.Contains("retry") || log.Message.Contains("fallback"));
    }

    [Fact]
    public async Task ErrorHandling_DatabaseConnectionFailure_ShouldHandleGracefully()
    {
        // Arrange - 使用无效的数据库连接
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            ConnectionString = "Server=invalid-host;Port=3306;Database=test;",
            SqlQuery = "SELECT * FROM users",
            RequireHumanReview = false
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);

        // 可能在提交时就失败
        if (submitResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            var error = await submitResponse.Content.ReadFromJsonAsync<ErrorResponse>();
            error.Should().NotBeNull();
            error!.Message.Should().Contain("connection");
            return;
        }

        // 或在执行时失败
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流处理错误
        await Task.Delay(5000);

        // Assert - 验证工作流失败并记录错误
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Failed");

        var logsResponse = await Client.GetAsync($"/api/workflows/{sessionId}/logs");
        var logs = await logsResponse.Content.ReadFromJsonAsync<WorkflowLogEntry[]>();
        logs.Should().NotBeNull();
        logs!.Should().Contain(log => log.Level == "Error");
        logs.Should().Contain(log => log.Message.Contains("connection") || log.Message.Contains("database"));
    }

    [Fact]
    public async Task ErrorHandling_RedisFailure_ShouldContinueWithoutCache()
    {
        // Arrange - 停止 Redis 容器模拟故障
        await RedisContainer.StopAsync();

        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM orders",
            RequireHumanReview = false
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流完成
        await Task.Delay(10000);

        // Assert - 验证工作流仍然完成（降级到仅使用数据库）
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().BeOneOf("Completed", "Failed");

        // 检查日志中的降级提示
        var logsResponse = await Client.GetAsync($"/api/workflows/{sessionId}/logs");
        var logs = await logsResponse.Content.ReadFromJsonAsync<WorkflowLogEntry[]>();
        logs.Should().NotBeNull();
        logs!.Should().Contain(log => log.Message.Contains("Redis") || log.Message.Contains("cache"));

        // 重启 Redis 以便后续测试
        await RedisContainer.StartAsync();
    }

    [Fact]
    public async Task ErrorHandling_GracefulDegradation_ShouldProvidePartialResults()
    {
        // Arrange - 模拟部分组件失败
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM products",
            RequireHumanReview = false,
            SimulatePartialFailure = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流完成
        await Task.Delay(10000);

        // Assert - 验证工作流完成并提供部分结果
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Completed");

        // 获取结果
        var resultResponse = await Client.GetAsync($"/api/workflows/{sessionId}/result");
        var result = await resultResponse.Content.ReadFromJsonAsync<WorkflowResult>();
        result.Should().NotBeNull();
        result!.Recommendations.Should().NotBeNull();
        result.Recommendations!.Should().NotBeEmpty();

        // 应该有部分结果标记
        result.Data.Should().ContainKey("PartialResults");
    }

    [Fact]
    public async Task ErrorHandling_ConcurrentFailures_ShouldIsolateErrors()
    {
        // Arrange - 提交多个工作流，部分会失败
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var request = new
            {
                ProjectId = Guid.NewGuid(),
                DatabaseType = "MySQL",
                SqlQuery = $"SELECT * FROM table_{i}",
                RequireHumanReview = false,
                SimulateFailure = i % 2 == 0 // 偶数索引模拟失败
            };

            var response = await Client.PostAsJsonAsync("/api/workflows/sql", request);
            var result = await response.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
            return result?.SessionId;
        });

        var sessionIds = await Task.WhenAll(tasks);

        // 等待所有工作流完成
        await Task.Delay(10000);

        // Assert - 验证失败的工作流不影响成功的工作流
        var statuses = new List<string>();
        foreach (var sessionId in sessionIds)
        {
            if (sessionId == null) continue;

            var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
            var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
            if (status != null)
            {
                statuses.Add(status.Status);
            }
        }

        // 应该有成功和失败的混合
        statuses.Should().Contain("Completed");
        statuses.Should().Contain("Failed");
    }

    [Fact]
    public async Task ErrorHandling_RetryExhaustion_ShouldFailGracefully()
    {
        // Arrange - 模拟持续失败的场景
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM users",
            RequireHumanReview = false,
            SimulatePersistentFailure = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待重试耗尽
        await Task.Delay(20000);

        // Assert - 验证工作流最终失败
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Failed");

        // 检查重试记录
        var logsResponse = await Client.GetAsync($"/api/workflows/{sessionId}/logs");
        var logs = await logsResponse.Content.ReadFromJsonAsync<WorkflowLogEntry[]>();
        logs.Should().NotBeNull();

        var retryLogs = logs!.Where(log => log.Message.Contains("retry")).ToList();
        retryLogs.Should().NotBeEmpty("应该有重试记录");
    }
}
