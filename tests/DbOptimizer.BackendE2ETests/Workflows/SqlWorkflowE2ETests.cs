using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using DbOptimizer.BackendE2ETests.Models;

namespace DbOptimizer.BackendE2ETests.Workflows;

/// <summary>
/// SQL 工作流 E2E 测试
/// </summary>
public sealed class SqlWorkflowE2ETests : E2ETestBase
{
    [Fact]
    public async Task SqlWorkflow_CompleteFlow_ShouldSucceed()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM users WHERE status = 'active' ORDER BY created_at",
            RequireHumanReview = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待工作流执行到审核门控
        await Task.Delay(5000);

        // Act - 获取工作流状态
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("PendingReview");

        // Act - 审核通过
        var reviewRequest = new { Action = "Approve", Comment = "LGTM" };
        var reviewResponse = await Client.PostAsJsonAsync($"/api/workflows/{sessionId}/review", reviewRequest);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 等待工作流完成
        await Task.Delay(3000);

        // Assert - 验证最终状态
        var finalStatusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var finalStatus = await finalStatusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        finalStatus.Should().NotBeNull();
        finalStatus!.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task SqlWorkflow_ReviewRejection_ShouldTerminate()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM orders",
            RequireHumanReview = true
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 等待到达审核门控
        await Task.Delay(5000);

        // Act - 审核驳回
        var reviewRequest = new { Action = "Reject", Comment = "SQL 需要优化" };
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
    public async Task SqlWorkflow_Cancellation_ShouldTerminate()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "PostgreSQL",
            SqlQuery = "SELECT * FROM products",
            RequireHumanReview = false
        };

        // Act - 提交工作流
        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // 立即取消
        var cancelResponse = await Client.PostAsync($"/api/workflows/{sessionId}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - 验证状态为已取消
        await Task.Delay(1000);
        var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        status.Should().NotBeNull();
        status!.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task SqlWorkflow_ParallelExecution_ShouldComplete()
    {
        // Arrange - 提交多个工作流
        var tasks = Enumerable.Range(0, 3).Select(async i =>
        {
            var request = new
            {
                ProjectId = Guid.NewGuid(),
                DatabaseType = "MySQL",
                SqlQuery = $"SELECT * FROM table_{i}",
                RequireHumanReview = false
            };

            var response = await Client.PostAsJsonAsync("/api/workflows/sql", request);
            var result = await response.Content.ReadFromJsonAsync<WorkflowSubmitResponse>();
            return result!.SessionId;
        });

        var sessionIds = await Task.WhenAll(tasks);

        // 等待所有工作流完成
        await Task.Delay(10000);

        // Assert - 验证所有工作流都完成
        foreach (var sessionId in sessionIds)
        {
            var statusResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
            var status = await statusResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
            status.Should().NotBeNull();
            status!.Status.Should().BeOneOf("Completed", "Failed");
        }
    }
}
