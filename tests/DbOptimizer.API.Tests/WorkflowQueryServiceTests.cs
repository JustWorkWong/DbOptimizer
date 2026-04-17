using System.Text.Json;
using DbOptimizer.API.Api;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Workflows;
using Xunit;

namespace DbOptimizer.API.Tests;

public sealed class WorkflowQueryServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsWaitingReviewProjectionWithResultAndReviewMetadata()
    {
        var sessionId = Guid.NewGuid();
        var checkpoint = CreateCheckpoint(
            sessionId,
            WorkflowCheckpointStatus.WaitingForReview,
            completedExecutors: ["SqlParserExecutor", "ExecutionPlanExecutor", "IndexAdvisorExecutor", "CoordinatorExecutor"],
            contextValues: new Dictionary<string, object?>
            {
                [WorkflowContextKeys.FinalResult] = new OptimizationReport
                {
                    Summary = "summary",
                    OverallConfidence = 0.91,
                    IndexRecommendations = [new IndexRecommendation { TableName = "users", CreateDdl = "CREATE INDEX idx_users_age ON users(age)", Confidence = 0.95, EstimatedBenefit = 97, Reasoning = "reason", Columns = ["age"], IndexType = "BTREE", EvidenceRefs = [] }]
                },
                [WorkflowContextKeys.ReviewId] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                [WorkflowContextKeys.ReviewStatus] = "Pending"
            });

        var service = new WorkflowQueryService(new StubCheckpointStorage(checkpoint), new WorkflowResultSerializer());

        var response = await service.GetAsync(sessionId);

        Assert.NotNull(response);
        Assert.Equal("WaitingForReview", response!.Status);
        Assert.Equal("HumanReviewExecutor", response.CurrentExecutor);
        Assert.Equal(67, response.Progress);
        Assert.NotNull(response.Result);
        Assert.Equal("sql-optimization-report", response.Result!.ResultType);
        Assert.Equal("summary", response.Result!.Summary);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), response.ReviewId);
        Assert.Equal("Pending", response.ReviewStatus);
    }

    [Fact]
    public async Task GetAsync_ReturnsCompletedProjectionWithFullProgressAndError()
    {
        var sessionId = Guid.NewGuid();
        var checkpoint = CreateCheckpoint(
            sessionId,
            WorkflowCheckpointStatus.Completed,
            completedExecutors: ["SqlParserExecutor", "ExecutionPlanExecutor", "IndexAdvisorExecutor", "CoordinatorExecutor", "HumanReviewExecutor", "RegenerationExecutor"],
            contextValues: new Dictionary<string, object?>
            {
                ["LastError"] = "none"
            });

        var service = new WorkflowQueryService(new StubCheckpointStorage(checkpoint), new WorkflowResultSerializer());

        var response = await service.GetAsync(sessionId);

        Assert.NotNull(response);
        Assert.Equal("Completed", response!.Status);
        Assert.Equal(100, response.Progress);
        Assert.Null(response.CurrentExecutor);
        Assert.NotNull(response.CompletedAt);
        Assert.Equal("none", response.ErrorMessage);
    }

    private static WorkflowCheckpoint CreateCheckpoint(
        Guid sessionId,
        WorkflowCheckpointStatus status,
        IReadOnlyList<string> completedExecutors,
        IDictionary<string, object?> contextValues)
    {
        var context = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in contextValues)
        {
            context[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
        }

        return new WorkflowCheckpoint
        {
            SessionId = sessionId,
            WorkflowType = "SqlAnalysis",
            Status = status,
            CurrentExecutor = status is WorkflowCheckpointStatus.WaitingForReview or WorkflowCheckpointStatus.Completed
                ? string.Empty
                : "CoordinatorExecutor",
            CheckpointVersion = 6,
            Context = context,
            CompletedExecutors = completedExecutors,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private sealed class StubCheckpointStorage(WorkflowCheckpoint? checkpoint) : ICheckpointStorage
    {
        public Task SaveCheckpointAsync(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowCheckpoint?> LoadCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(checkpoint?.SessionId == sessionId ? checkpoint : null);
        }

        public Task DeleteCheckpointAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
