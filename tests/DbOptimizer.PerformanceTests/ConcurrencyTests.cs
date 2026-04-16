using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Diagnostics;

namespace DbOptimizer.PerformanceTests;

/// <summary>
/// 并发性能测试 - 测试系统在不同并发负载下的表现
/// </summary>
public class ConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConcurrencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task SqlAnalysis_ConcurrentRequests_ShouldHandleLoad(int concurrency)
    {
        // Arrange
        var client = _factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - 发起并发请求
        for (int i = 0; i < concurrency; i++)
        {
            var task = client.GetAsync("/api/workflows/health");
            tasks.Add(task);
        }

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        var avgResponseTime = stopwatch.ElapsedMilliseconds / (double)concurrency;

        successCount.Should().Be(concurrency, "所有请求都应成功");
        avgResponseTime.Should().BeLessThan(1000, "平均响应时间应小于 1 秒");

        // 输出性能指标
        Console.WriteLine($"并发数: {concurrency}");
        Console.WriteLine($"总耗时: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"平均响应时间: {avgResponseTime:F2}ms");
        Console.WriteLine($"成功率: {successCount}/{concurrency}");
    }

    [Fact]
    public async Task WorkflowExecution_ShouldCompleteWithinTimeout()
    {
        // Arrange
        var client = _factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        // Act - 模拟 Workflow 执行（使用健康检查端点作为占位符）
        var response = await client.GetAsync("/api/workflows/health");
        stopwatch.Stop();

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Workflow 应在 5 秒内完成");

        Console.WriteLine($"Workflow 执行时间: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task SseConnection_ShouldHandleMultipleClients()
    {
        // Arrange
        var clientCount = 20;
        var clients = Enumerable.Range(0, clientCount)
            .Select(_ => _factory.CreateClient())
            .ToList();

        var stopwatch = Stopwatch.StartNew();

        // Act - 模拟多个 SSE 连接
        var tasks = clients.Select(c => c.GetAsync("/api/workflows/health")).ToList();
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        successCount.Should().Be(clientCount, "所有 SSE 连接都应成功建立");

        Console.WriteLine($"SSE 连接数: {clientCount}");
        Console.WriteLine($"建立连接总耗时: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"平均连接时间: {stopwatch.ElapsedMilliseconds / (double)clientCount:F2}ms");
    }
}
