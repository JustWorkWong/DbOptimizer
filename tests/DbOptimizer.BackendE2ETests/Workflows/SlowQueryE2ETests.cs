using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using DbOptimizer.BackendE2ETests.Models;

namespace DbOptimizer.BackendE2ETests.Workflows;

/// <summary>
/// 慢查询自动化 E2E 测试
/// </summary>
public sealed class SlowQueryE2ETests : E2ETestBase
{
    [Fact]
    public async Task SlowQuery_Collection_ShouldTriggerWorkflow()
    {
        // Arrange - 模拟慢查询数据
        var slowQueryData = new
        {
            ProjectId = Guid.NewGuid(),
            Query = "SELECT * FROM orders o JOIN customers c ON o.customer_id = c.id WHERE o.status = 'pending'",
            ExecutionTime = 5.2,
            Timestamp = DateTime.UtcNow,
            DatabaseType = "MySQL"
        };

        // Act - 上报慢查询
        var reportResponse = await Client.PostAsJsonAsync("/api/slow-queries", slowQueryData);
        reportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reportResult = await reportResponse.Content.ReadFromJsonAsync<SlowQueryReportResponse>();
        reportResult.Should().NotBeNull();
        var slowQueryId = reportResult!.SlowQueryId;

        // 等待自动触发工作流
        await Task.Delay(3000);

        // Assert - 验证工作流已创建
        var workflowsResponse = await Client.GetAsync($"/api/slow-queries/{slowQueryId}/workflows");
        workflowsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var workflows = await workflowsResponse.Content.ReadFromJsonAsync<WorkflowSummary[]>();
        workflows.Should().NotBeNull();
        workflows!.Length.Should().BeGreaterThan(0);

        var workflow = workflows[0];
        workflow.Type.Should().Be("SqlAnalysis");
        workflow.Status.Should().BeOneOf("Running", "PendingReview", "Completed");
    }

    [Fact]
    public async Task SlowQuery_AutomaticWorkflowSubmission_ShouldCreateBidirectionalLink()
    {
        // Arrange
        var slowQueryData = new
        {
            ProjectId = Guid.NewGuid(),
            Query = "SELECT COUNT(*) FROM large_table WHERE created_at > NOW() - INTERVAL 1 DAY",
            ExecutionTime = 8.5,
            Timestamp = DateTime.UtcNow,
            DatabaseType = "PostgreSQL"
        };

        // Act - 上报慢查询
        var reportResponse = await Client.PostAsJsonAsync("/api/slow-queries", slowQueryData);
        var reportResult = await reportResponse.Content.ReadFromJsonAsync<SlowQueryReportResponse>();
        reportResult.Should().NotBeNull();
        var slowQueryId = reportResult!.SlowQueryId;

        // 等待自动工作流创建
        await Task.Delay(3000);

        // Assert - 验证双向关联
        // 1. 从慢查询查工作流
        var workflowsResponse = await Client.GetAsync($"/api/slow-queries/{slowQueryId}/workflows");
        var workflows = await workflowsResponse.Content.ReadFromJsonAsync<WorkflowSummary[]>();
        workflows.Should().NotBeNull();
        workflows!.Length.Should().BeGreaterThan(0);

        var sessionId = workflows[0].SessionId;

        // 2. 从工作流查慢查询
        var workflowResponse = await Client.GetAsync($"/api/workflows/{sessionId}");
        var workflow = await workflowResponse.Content.ReadFromJsonAsync<WorkflowStatusResponse>();
        workflow.Should().NotBeNull();
        workflow!.SourceSlowQueryId.Should().Be(slowQueryId);
    }

    [Fact]
    public async Task SlowQuery_BidirectionalTracking_ShouldMaintainConsistency()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var slowQueries = new[]
        {
            new
            {
                ProjectId = projectId,
                Query = "SELECT * FROM table1",
                ExecutionTime = 3.0,
                Timestamp = DateTime.UtcNow,
                DatabaseType = "MySQL"
            },
            new
            {
                ProjectId = projectId,
                Query = "SELECT * FROM table2",
                ExecutionTime = 4.5,
                Timestamp = DateTime.UtcNow,
                DatabaseType = "MySQL"
            }
        };

        // Act - 上报多个慢查询
        var slowQueryIds = new List<Guid>();
        foreach (var query in slowQueries)
        {
            var response = await Client.PostAsJsonAsync("/api/slow-queries", query);
            var result = await response.Content.ReadFromJsonAsync<SlowQueryReportResponse>();
            result.Should().NotBeNull();
            slowQueryIds.Add(result!.SlowQueryId);
        }

        // 等待工作流创建
        await Task.Delay(5000);

        // Assert - 验证每个慢查询都有对应的工作流
        foreach (var slowQueryId in slowQueryIds)
        {
            var workflowsResponse = await Client.GetAsync($"/api/slow-queries/{slowQueryId}/workflows");
            var workflows = await workflowsResponse.Content.ReadFromJsonAsync<WorkflowSummary[]>();
            workflows.Should().NotBeNull();
            workflows!.Length.Should().BeGreaterThan(0);
        }

        // Assert - 验证项目级别的关联
        var projectWorkflowsResponse = await Client.GetAsync($"/api/projects/{projectId}/workflows");
        var projectWorkflows = await projectWorkflowsResponse.Content.ReadFromJsonAsync<WorkflowSummary[]>();
        projectWorkflows.Should().NotBeNull();
        projectWorkflows!.Length.Should().BeGreaterOrEqualTo(slowQueryIds.Count);
    }

    [Fact]
    public async Task SlowQuery_WorkflowCompletion_ShouldUpdateSlowQueryStatus()
    {
        // Arrange
        var slowQueryData = new
        {
            ProjectId = Guid.NewGuid(),
            Query = "SELECT * FROM products WHERE category_id IN (SELECT id FROM categories)",
            ExecutionTime = 6.0,
            Timestamp = DateTime.UtcNow,
            DatabaseType = "MySQL"
        };

        // Act - 上报慢查询
        var reportResponse = await Client.PostAsJsonAsync("/api/slow-queries", slowQueryData);
        var reportResult = await reportResponse.Content.ReadFromJsonAsync<SlowQueryReportResponse>();
        reportResult.Should().NotBeNull();
        var slowQueryId = reportResult!.SlowQueryId;

        // 等待工作流创建并执行
        await Task.Delay(3000);

        // 获取工作流 ID
        var workflowsResponse = await Client.GetAsync($"/api/slow-queries/{slowQueryId}/workflows");
        var workflows = await workflowsResponse.Content.ReadFromJsonAsync<WorkflowSummary[]>();
        workflows.Should().NotBeNull();
        var sessionId = workflows![0].SessionId;

        // 等待工作流完成（假设不需要审核）
        await Task.Delay(10000);

        // Assert - 验证慢查询状态已更新
        var slowQueryResponse = await Client.GetAsync($"/api/slow-queries/{slowQueryId}");
        var slowQuery = await slowQueryResponse.Content.ReadFromJsonAsync<SlowQueryReportResponse>();
        slowQuery.Should().NotBeNull();
        slowQuery!.Status.Should().BeOneOf("Analyzed", "Optimized");
    }
}
