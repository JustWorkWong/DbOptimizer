using FluentAssertions;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;

namespace DbOptimizer.Infrastructure.Tests.Maf;

public sealed class MafNativeWorkflowInteropTests
{
    private static readonly RequestPort ReviewPort =
        RequestPort.Create<ReviewRequestMessage, ReviewResponseMessage>("review-port");

    [Fact]
    public async Task RunStreamingAsync_CanRoundTripExternalRequestAndResponse()
    {
        var workflow = CreateReviewWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new ReviewRequestMessage("Approve SQL rewrite?"),
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
                        return requestEvent.Request.CreateResponse(new ReviewResponseMessage(true));

                    case WorkflowOutputEvent outputEvent:
                        outputEvent.Data.Should().BeOfType<string>();
                        return null;

                    default:
                        return null;
                }
            },
            cts.Token);

        pendingRequest.Should().NotBeNull();
        pendingRequest!.Request.TryGetDataAs<ReviewRequestMessage>(out var requestPayload).Should().BeTrue();
        requestPayload!.Prompt.Should().Be("Approve SQL rewrite?");
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
                         new ReviewRequestMessage("Need human confirmation"),
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
                        return requestEvent.Request.CreateResponse(new ReviewResponseMessage(false));

                    case WorkflowOutputEvent outputEvent:
                        outputEvent.Data.Should().BeOfType<string>();
                        return null;

                    default:
                        return null;
                }
            },
            cts.Token);

        replayedRequest.Should().NotBeNull();
        replayedRequest!.Request.TryGetDataAs<ReviewRequestMessage>(out var replayedPayload).Should().BeTrue();
        replayedPayload!.Prompt.Should().Be("Need human confirmation");
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Ended);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResumeStreamingAsync_WithGateExecutor_RemainsIdleBecauseResponseReturnsToSender(bool includeGateAsOutput)
    {
        var workflow = CreateGateReviewWorkflow(includeGateAsOutput);
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        CheckpointInfo checkpoint;

        await using (var initialRun = await InProcessExecution.RunStreamingAsync(
                         workflow,
                         "Need human confirmation",
                         checkpointManager,
                         sessionId: Guid.NewGuid().ToString(),
                         cts.Token))
        {
            await foreach (var evt in initialRun.WatchStreamAsync(cts.Token))
            {
                if (evt is RequestInfoEvent)
                {
                    break;
                }
            }

            await WaitForStatusAsync(initialRun, RunStatus.PendingRequests, cts.Token);
            checkpoint = initialRun.Checkpoints.Last();
        }

        await using var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            checkpointManager,
            cts.Token);

        var requestCount = 0;
        await resumedRun.RunToCompletionAsync(
            evt =>
            {
                if (evt is RequestInfoEvent requestEvent)
                {
                    requestCount++;
                    return requestEvent.Request.CreateResponse(new ReviewResponseMessage(true));
                }

                return null;
            },
            cts.Token);

        requestCount.Should().Be(1);
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Idle);
    }

    [Fact]
    public async Task ResumeStreamingAsync_WithBidirectionalReviewExecutor_EmitsOutputButRemainsIdle()
    {
        var workflow = CreateBidirectionalReviewWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        CheckpointInfo checkpoint;

        await using (var initialRun = await InProcessExecution.RunStreamingAsync(
                         workflow,
                         new StartReviewMessage("Need human confirmation"),
                         checkpointManager,
                         sessionId: Guid.NewGuid().ToString(),
                         cts.Token))
        {
            await foreach (var evt in initialRun.WatchStreamAsync(cts.Token))
            {
                if (evt is RequestInfoEvent)
                {
                    break;
                }
            }

            await WaitForStatusAsync(initialRun, RunStatus.PendingRequests, cts.Token);
            checkpoint = initialRun.Checkpoints.Last();
        }

        await using var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            checkpointManager,
            cts.Token);

        var requestCount = 0;
        var outputs = new List<string>();
        await resumedRun.RunToCompletionAsync(
            evt =>
            {
                switch (evt)
                {
                    case RequestInfoEvent requestEvent:
                        requestCount++;
                        return requestEvent.Request.CreateResponse(new ReviewResponseMessage(true));
                    case WorkflowOutputEvent outputEvent:
                        outputs.Add(outputEvent.Data.Should().BeOfType<string>().Subject);
                        return null;
                    default:
                        return null;
                }
            },
            cts.Token);

        requestCount.Should().Be(1);
        outputs.Should().ContainSingle().Which.Should().Be("approved");
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Idle);
    }

    [Fact]
    public async Task ResumeStreamingAsync_WithExplicitExternalRequest_BidirectionalReviewExecutor_DoesNotEmitOutputOnResume()
    {
        var workflow = CreateExplicitExternalRequestWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        CheckpointInfo checkpoint;

        await using (var initialRun = await InProcessExecution.RunStreamingAsync(
                         workflow,
                         new StartReviewMessage("Need human confirmation"),
                         checkpointManager,
                         sessionId: Guid.NewGuid().ToString(),
                         cts.Token))
        {
            await foreach (var evt in initialRun.WatchStreamAsync(cts.Token))
            {
                if (evt is RequestInfoEvent)
                {
                    break;
                }
            }

            await WaitForStatusAsync(initialRun, RunStatus.PendingRequests, cts.Token);
            checkpoint = initialRun.Checkpoints.Last();
        }

        await using var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            checkpointManager,
            cts.Token);

        var requestCount = 0;
        var outputs = new List<string>();
        await resumedRun.RunToCompletionAsync(
            evt =>
            {
                switch (evt)
                {
                    case RequestInfoEvent requestEvent:
                        requestCount++;
                        return requestEvent.Request.CreateResponse(new ReviewResponseMessage(true));
                    case WorkflowOutputEvent outputEvent:
                        outputs.Add(outputEvent.Data.Should().BeOfType<string>().Subject);
                        return null;
                    default:
                        return null;
                }
            },
            cts.Token);

        requestCount.Should().Be(1);
        outputs.Should().BeEmpty();
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Idle);
    }

    [Fact]
    public async Task ResumeStreamingAsync_WithExplicitExternalRequest_DirectSendResponseAsync_DoesNotEmitOutput()
    {
        var workflow = CreateExplicitExternalRequestWorkflow();
        var checkpointManager = CheckpointManager.CreateInMemory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        CheckpointInfo checkpoint;
        RequestInfoEvent? pendingRequest = null;

        await using (var initialRun = await InProcessExecution.RunStreamingAsync(
                         workflow,
                         new StartReviewMessage("Need human confirmation"),
                         checkpointManager,
                         sessionId: Guid.NewGuid().ToString(),
                         cts.Token))
        {
            await foreach (var evt in initialRun.WatchStreamAsync(cts.Token))
            {
                if (evt is RequestInfoEvent requestEvent)
                {
                    pendingRequest = requestEvent;
                    break;
                }
            }

            pendingRequest.Should().NotBeNull();
            await WaitForStatusAsync(initialRun, RunStatus.PendingRequests, cts.Token);
            checkpoint = initialRun.Checkpoints.Last();
        }

        var storedResponse = ExternalRequest.Create(
                ReviewPort,
                new ReviewRequestMessage("placeholder"),
                pendingRequest!.Request.RequestId)
            .CreateResponse(new ReviewResponseMessage(true));

        await using var resumedRun = await InProcessExecution.ResumeStreamingAsync(
            workflow,
            checkpoint,
            checkpointManager,
            cts.Token);

        await resumedRun.SendResponseAsync(storedResponse);

        var outputs = new List<string>();
        await resumedRun.RunToCompletionAsync(
            evt =>
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    outputs.Add(outputEvent.Data.Should().BeOfType<string>().Subject);
                }

                return null;
            },
            cts.Token);

        outputs.Should().BeEmpty();
        (await resumedRun.GetStatusAsync()).Should().Be(RunStatus.Idle);
    }

    private static Workflow CreateReviewWorkflow()
    {
        var finalizeExecutor = new ReviewDecisionExecutor();

        return new WorkflowBuilder(ReviewPort)
            .AddEdge(ReviewPort, finalizeExecutor)
            .WithOutputFrom(finalizeExecutor)
            .Build();
    }

    private static Workflow CreateGateReviewWorkflow(bool includeGateAsOutput)
    {
        var gateExecutor = new ReviewGateExecutor();
        var finalizeExecutor = new ReviewDecisionExecutor();
        var builder = new WorkflowBuilder(gateExecutor)
            .AddEdge(gateExecutor, ReviewPort)
            .AddEdge(ReviewPort, finalizeExecutor)
            .WithOutputFrom(finalizeExecutor);

        if (includeGateAsOutput)
        {
            builder.WithOutputFrom(gateExecutor);
        }

        return builder.Build();
    }

    private static Workflow CreateBidirectionalReviewWorkflow()
    {
        var executor = new BidirectionalReviewExecutor();

        return new WorkflowBuilder(executor)
            .AddEdge(executor, ReviewPort)
            .AddEdge(ReviewPort, executor)
            .WithOutputFrom(executor)
            .Build();
    }

    private static Workflow CreateExplicitExternalRequestWorkflow()
    {
        var executor = new ExplicitExternalRequestReviewExecutor();

        return new WorkflowBuilder(executor)
            .AddEdge(executor, ReviewPort)
            .AddEdge(ReviewPort, executor)
            .WithOutputFrom(executor)
            .Build();
    }

    private sealed record StartReviewMessage(string Prompt);

    private sealed record ReviewRequestMessage(string Prompt);

    private sealed record ReviewResponseMessage(bool Approved);

    private sealed class ReviewGateExecutor : Executor<string>
    {
        public ReviewGateExecutor()
            : base("review-gate")
        {
        }

        public override async ValueTask HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var request = ExternalRequest.Create(
                ReviewPort,
                new ReviewRequestMessage(message),
                Guid.NewGuid().ToString("N"));

            await context.SendMessageAsync(request, ReviewPort.Id, cancellationToken);
        }
    }

    private sealed class ReviewDecisionExecutor : Executor<ReviewResponseMessage>
    {
        public ReviewDecisionExecutor()
            : base("review-decision")
        {
        }

        public override ValueTask HandleAsync(
            ReviewResponseMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return context.YieldOutputAsync(message.Approved ? "approved" : "rejected", cancellationToken);
        }
    }

    [System.Obsolete]
    [SendsMessage(typeof(ReviewRequestMessage))]
    private sealed class BidirectionalReviewExecutor :
        ReflectingExecutor<BidirectionalReviewExecutor>,
        IMessageHandler<StartReviewMessage>,
        IMessageHandler<ReviewResponseMessage, string>
    {
        public BidirectionalReviewExecutor()
            : base("bidirectional-review")
        {
        }

        public async ValueTask HandleAsync(
            StartReviewMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await context.SendMessageAsync(new ReviewRequestMessage(message.Prompt), cancellationToken);
        }

        public ValueTask<string> HandleAsync(
            ReviewResponseMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message.Approved ? "approved" : "rejected");
        }
    }

    [System.Obsolete]
    private sealed class ExplicitExternalRequestReviewExecutor :
        ReflectingExecutor<ExplicitExternalRequestReviewExecutor>,
        IMessageHandler<StartReviewMessage>,
        IMessageHandler<ReviewResponseMessage, string>
    {
        public ExplicitExternalRequestReviewExecutor()
            : base("explicit-external-review")
        {
        }

        public async ValueTask HandleAsync(
            StartReviewMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var request = ExternalRequest.Create(
                ReviewPort,
                new ReviewRequestMessage(message.Prompt),
                Guid.NewGuid().ToString("N"));

            await context.SendMessageAsync(request, ReviewPort.Id, cancellationToken);
        }

        public ValueTask<string> HandleAsync(
            ReviewResponseMessage message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message.Approved ? "approved" : "rejected");
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
