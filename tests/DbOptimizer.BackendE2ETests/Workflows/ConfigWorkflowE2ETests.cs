using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using DbOptimizer.BackendE2ETests.Models;

namespace DbOptimizer.BackendE2ETests.Workflows;

/// <summary>
/// 配置优化工作流 E2E 测试
/// </summary>
public sealed class ConfigWorkflowE2ETests : E2ETestBase
{
    [Fact]
    public async Task ConfigWorkflow_CompleteFlow_ShouldSucceed()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            RequireHumanReview = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/config", request);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流执行到审核门控
        await Task.Delay(8000);

        // Act - 获取工作流状态
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("PendingReview");

        // Act - 审核通过
        var reviewRequest = new { Action = "Approve", Comment = "配置优化建议合理" };
        var reviewResponse = await Client.PostAsJsonAsync($"/api/workflows/{sessionId}/review", reviewRequest);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 等待工作流完成
        await Task.Delay(2000);

        // Assert - 验证最终状态
        var finalStatusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var finalStatus = await finalStatusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        finalStatus.Should().NotBeNull();
        finalStatus!.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task ConfigWorkflow_ReviewFlow_ShouldHandleApprovalAndRejection()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "PostgreSQL",
            RequireHumanReview = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/config", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待到达审核门控
        await Task.Delay(8000);

        // Act - 审核驳回
        var reviewRequest = new { Action = "Reject", Comment = "需要更详细的影响分析" };
        var reviewResponse = await Client.PostAsJsonAsync($"/api/workflows/{sessionId}/review", reviewRequest);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - 验证状态为已拒绝
        await Task.Delay(1000);
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Rejected");
    }

    [Fact]
    public async Task ConfigWorkflow_McpTimeout_ShouldFallbackGracefully()
    {
        // Arrange - 模拟 MCP 超时场景
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            RequireHumanReview = false,
            SimulateMcpTimeout = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/config", request);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流处理超时
        await Task.Delay(10000);

        // Assert - 验证工作流使用降级策略完成
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().BeOneOf("Completed", "Failed");

        // 如果完成，应该有降级标记
        if (status.Status == "Completed" && status.Metadata != null)
        {
            status.Metadata.Should().ContainKey("UsedFallback");
        }
    }

    [Fact]
    public async Task ConfigWorkflow_NoReviewRequired_ShouldAutoComplete()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            RequireHumanReview = false
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/config", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流自动完成
        await Task.Delay(10000);

        // Assert - 验证直接完成，无需审核
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task ConfigWorkflow_DatabaseConnectionFailure_ShouldHandleGracefully()
    {
        // Arrange - 使用无效的数据库连接
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            ConnectionString = "Server=invalid;Database=test;",
            RequireHumanReview = false
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/config", request);

        // 可能在提交时就失败，或在执行时失败
        if (submitResponse.StatusCode == HttpStatusCode.OK)
        {
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
            var logs = await logsResponse.Content.ReadFromJsonAsync<List<WorkflowLogEntry>>();
            logs.Should().NotBeNull();
            logs!.Should().Contain(log => log.Level == "Error");
        }
        else
        {
            submitResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task ConfigWorkflow_ParallelExecution_ShouldNotInterfere()
    {
        // Arrange - 提交多个配置工作流
        var projectId = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 2).Select(async i =>
        {
            var request = new
            {
                ProjectId = projectId,
                DatabaseType = "MySQL",
                RequireHumanReview = false
            };

            var response = await Client.PostAsJsonAsync("/api/workflows/config", request);
            var result = await response.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
            return result!.SessionId;
        });

        var sessionIds = await Task.WhenAll(tasks);

        // 等待所有工作流完成
        await Task.Delay(15000);

        // Assert - 验证所有工作流都完成
        foreach (var sessionId in sessionIds)
        {
            var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
            var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
            status.Should().NotBeNull();
            status!.Status.Should().Be("Completed");
        }
    }
}
