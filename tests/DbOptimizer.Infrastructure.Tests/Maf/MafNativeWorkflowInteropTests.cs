using FluentAssertions;
using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Tests.Maf;

public sealed class MafNativeWorkflowInteropTests
{
    [Fact]
    public async Task RunStreamingAsync_CanRoundTripExternalRequestAndResponse()
    {
        var workflow = CreateReviewWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            "Approve SQL rewrite?",
            checkpointManager,
            sessionId: Guid.NewGuid().ToString(),
            cts.Token);

        RequestInfoEvent? pendingRequest = null;
        await run.RunToCompletionAsync(
            evt =>
            {
                switch (evt)
                {
                    case RequestInfoEvent requestEvent:
                        pendingRequest = requestEvent;
                        return requestEvent.Request.CreateResponse(true);

                    case WorkflowOutputEvent outputEvent:
                        outputEvent.Data.Should().BeOfType<string>();
                        return null;

                    default:
                        return null;
                }
            },
            cts.Token);

        pendingRequest.Should().NotBeNull();
        pendingRequest!.Request.TryGetDataAs<string>(out var requestPayload).Should().BeTrue();
        requestPayload.Should().Be("Approve SQL rewrite?");
        run.Checkpoints.Should().NotBeEmpty();
        (await run.GetStatusAsync()).Should().Be(RunStatus.Ended);
    }

    [Fact]
    public async Task ResumeStreamingAsync_ReEmitsPendingRequestAndCompletesAfterResponse()
    {
        var workflow = CreateReviewWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        CheckpointInfo checkpoint;
        RequestInfoEvent? initialRequest = null;

        await using (var initialRun = await InProcessExecution.RunStreamingAsync(
                         workflow,
                         "Need human confirmation",
                         checkpointManager,
                         sessionId: Guid.NewGuid().ToString(),
                         cts.Token))
        {
            await foreach (var evt in initialRun.WatchStreamAsync(cts.Token))
            {
                if (evt is RequestInfoEvent requestEvent)
                {
                    initialRequest = requestEvent;
                    break;
                }
            }

            initialRequest.Should().NotBeNull();
            await WaitForStatusAsync(initialRun, RunStatus.PendingRequests, cts.Token);
            (await initialRun.GetStatusAsync()).Should().Be(RunStatus.PendingRequests);
            initialRun.Checkpoints.Should().NotBeEmpty();
            initialRun.Checkpoints.Should().NotBeEmpty();
            checkpoint = initialRun.Checkpoints.Last();
        }

        await using var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            checkpointManager,
            cts.Token);

        RequestInfoEvent? replayedRequest = null;
        await resumedRun.RunToCompletionAsync(
            evt =>
            {
                switch (evt)
                {
                    case RequestInfoEvent requestEvent:
                        replayedRequest = requestEvent;
                        return requestEvent.Request.CreateResponse(false);

                    case WorkflowOutputEvent outputEvent:
                        outputEvent.Data.Should().BeOfType<string>();
                        return null;

                    default:
                        return null;
                }
            },
            cts.Token);

        replayedRequest.Should().NotBeNull();
        replayedRequest!.Request.TryGetDataAs<string>(out var replayedPayload).Should().BeTrue();
        replayedPayload.Should().Be("Need human confirmation");
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Ended);
    }

    private static Workflow CreateReviewWorkflow()
    {
        var reviewPort = RequestPort.Create<string, bool>("review-port");
        var finalizeExecutor = new ReviewDecisionExecutor();

        return new WorkflowBuilder(reviewPort)
            .AddEdge(reviewPort, finalizeExecutor)
            .WithOutputFrom(finalizeExecutor)
            .Build();
    }

    private sealed class ReviewDecisionExecutor : Executor<bool>
    {
        public ReviewDecisionExecutor()
            : base("review-decision")
        {
        }

        public override ValueTask HandleAsync(
            bool message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return context.YieldOutputAsync(message ? "approved" : "rejected", cancellationToken);
        }
    }

    private static async Task WaitForStatusAsync(StreamingRun run, RunStatus expected, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await run.GetStatusAsync(cancellationToken) == expected)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}
