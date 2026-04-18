using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Xunit;

namespace DbOptimizer.BackendE2ETests.Workflows;

/// <summary>
/// SSE 事件流 E2E 测试
/// </summary>
public sealed class SseStreamingE2ETests : E2ETestBase
{
    [Fact]
    public async Task SseStream_RealTimeEventDelivery_ShouldReceiveEvents()
    {
        // Arrange - 提交工作流
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM users",
            RequireHumanReview = false
        };

        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<Models.WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // Act - 连接 SSE 流
        var sseClient = Factory.CreateClient();
        var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
        sseRequest.Headers.Add("Accept", "text/event-stream");

        var sseResponse = await sseClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        sseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        sseResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // Assert - 读取事件流
        var events = new List<string>();
        using var stream = await sseResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;

                if (line.StartsWith("data: "))
                {
                    var eventData = line.Substring(6);
                    events.Add(eventData);

                    if (eventData.Contains("\"type\":\"WorkflowCompleted\""))
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 超时是正常的
        }

        // Assert - 验证收到的事件
        events.Should().NotBeEmpty();
        events.Should().Contain(e => e.Contains("\"type\":\"WorkflowStarted\""));
        events.Should().Contain(e => e.Contains("\"type\":\"ExecutorStarted\""));
    }

    [Fact]
    public async Task SseStream_Reconnection_ShouldResumeFromLastEvent()
    {
        // Arrange - 提交工作流
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM orders",
            RequireHumanReview = true
        };

        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<Models.WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // Act - 第一次连接，读取部分事件
        var firstEvents = new List<string>();
        var sseClient1 = Factory.CreateClient();
        var sseRequest1 = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
        sseRequest1.Headers.Add("Accept", "text/event-stream");

        var sseResponse1 = await sseClient1.SendAsync(sseRequest1, HttpCompletionOption.ResponseHeadersRead);
        using var stream1 = await sseResponse1.Content.ReadAsStreamAsync();
        using var reader1 = new StreamReader(stream1);
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        string? lastEventId = null;
        try
        {
            while (!cts1.Token.IsCancellationRequested)
            {
                var line = await reader1.ReadLineAsync(cts1.Token);
                if (line == null) break;

                if (line.StartsWith("id: "))
                {
                    lastEventId = line.Substring(4);
                }
                else if (line.StartsWith("data: "))
                {
                    firstEvents.Add(line.Substring(6));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 预期超时
        }

        // Act - 第二次连接，使用 Last-Event-ID 恢复
        var secondEvents = new List<string>();
        var sseClient2 = Factory.CreateClient();
        var sseRequest2 = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
        sseRequest2.Headers.Add("Accept", "text/event-stream");
        if (lastEventId != null)
        {
            sseRequest2.Headers.Add("Last-Event-ID", lastEventId);
        }

        var sseResponse2 = await sseClient2.SendAsync(sseRequest2, HttpCompletionOption.ResponseHeadersRead);
        using var stream2 = await sseResponse2.Content.ReadAsStreamAsync();
        using var reader2 = new StreamReader(stream2);
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (!cts2.Token.IsCancellationRequested)
            {
                var line = await reader2.ReadLineAsync(cts2.Token);
                if (line == null) break;

                if (line.StartsWith("data: "))
                {
                    secondEvents.Add(line.Substring(6));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 预期超时
        }

        // Assert - 验证没有重复事件
        firstEvents.Should().NotBeEmpty();
        secondEvents.Should().NotBeEmpty();

        var allEvents = firstEvents.Concat(secondEvents).ToList();
        var uniqueEvents = allEvents.Distinct().ToList();
        allEvents.Count.Should().Be(uniqueEvents.Count, "不应该有重复事件");
    }

    [Fact]
    public async Task SseStream_EventOrdering_ShouldMaintainSequence()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM products",
            RequireHumanReview = false
        };

        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<Models.WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // Act - 连接 SSE 流并收集事件序列
        var sseClient = Factory.CreateClient();
        var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
        sseRequest.Headers.Add("Accept", "text/event-stream");

        var sseResponse = await sseClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await sseResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var eventSequences = new List<int>();
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;

                if (line.StartsWith("id: "))
                {
                    if (int.TryParse(line.Substring(4), out var seq))
                    {
                        eventSequences.Add(seq);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 超时是正常的
        }

        // Assert - 验证序列号递增
        eventSequences.Should().NotBeEmpty();
        for (int i = 1; i < eventSequences.Count; i++)
        {
            eventSequences[i].Should().BeGreaterThan(eventSequences[i - 1], "事件序列号应该递增");
        }
    }

    [Fact]
    public async Task SseStream_MultipleClients_ShouldReceiveSameEvents()
    {
        // Arrange
        var request = new
        {
            ProjectId = Guid.NewGuid(),
            DatabaseType = "MySQL",
            SqlQuery = "SELECT * FROM inventory",
            RequireHumanReview = false
        };

        var submitResponse = await Client.PostAsJsonAsync("/api/workflows/sql", request);
        var submitResult = await submitResponse.Content.ReadFromJsonAsync<Models.WorkflowSubmitResponse>();
        submitResult.Should().NotBeNull();
        var sessionId = submitResult!.SessionId;

        // Act - 两个客户端同时连接
        var client1Events = new List<string>();
        var client2Events = new List<string>();

        var task1 = Task.Run(async () =>
        {
            var sseClient = Factory.CreateClient();
            var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
            sseRequest.Headers.Add("Accept", "text/event-stream");

            var sseResponse = await sseClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await sseResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (line == null) break;

                    if (line.StartsWith("data: "))
                    {
                        client1Events.Add(line.Substring(6));
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        var task2 = Task.Run(async () =>
        {
            var sseClient = Factory.CreateClient();
            var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows/{sessionId}/events");
            sseRequest.Headers.Add("Accept", "text/event-stream");

            var sseResponse = await sseClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await sseResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (line == null) break;

                    if (line.StartsWith("data: "))
                    {
                        client2Events.Add(line.Substring(6));
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        await Task.WhenAll(task1, task2);

        // Assert - 验证两个客户端收到相同的事件
        client1Events.Should().NotBeEmpty();
        client2Events.Should().NotBeEmpty();
        client1Events.Should().BeEquivalentTo(client2Events);
    }
}
