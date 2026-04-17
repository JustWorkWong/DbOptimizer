using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.Infrastructure.Workflows;

public interface IWorkflowRunner
{
    Task<WorkflowRunResult> RunAsync(
        WorkflowContext context,
        IReadOnlyList<IWorkflowExecutor> executors,
        CancellationToken cancellationToken = default);

    Task<WorkflowRunResult> ResumeAsync(
        WorkflowCheckpoint checkpoint,
        IReadOnlyList<IWorkflowExecutor> executors,
        CancellationToken cancellationToken = default);
}

/* =========================
 * Workflow 串行运行器
 * 设计目标：
 * 1) 统一编排串行 Executor 执行顺序
 * 2) 在每个关键节点发布事件并保存 Checkpoint
 * 3) 支持基于已有 Checkpoint 恢复未完成的 Workflow
 * ========================= */
public sealed class WorkflowRunner(
    ICheckpointStorage checkpointStorage,
    IWorkflowEventPublisher workflowEventPublisher,
    IWorkflowStateMachine workflowStateMachine,
    IWorkflowExecutionAuditService workflowExecutionAuditService,
    ILogger<WorkflowRunner> logger) : IWorkflowRunner
{
    public Task<WorkflowRunResult> RunAsync(
        WorkflowContext context,
        IReadOnlyList<IWorkflowExecutor> executors,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInternalAsync(context, executors, isResume: false, cancellationToken);
    }

    public Task<WorkflowRunResult> ResumeAsync(
        WorkflowCheckpoint checkpoint,
        IReadOnlyList<IWorkflowExecutor> executors,
        CancellationToken cancellationToken = default)
    {
        if (checkpoint.Status is WorkflowCheckpointStatus.Completed or WorkflowCheckpointStatus.Failed or WorkflowCheckpointStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Workflow is terminal and cannot be resumed. SessionId={checkpoint.SessionId}, Status={checkpoint.Status}");
        }

        var context = WorkflowContext.FromCheckpoint(checkpoint, cancellationToken);
        return ExecuteInternalAsync(context, executors, isResume: true, cancellationToken);
    }

    private async Task<WorkflowRunResult> ExecuteInternalAsync(
        WorkflowContext context,
        IReadOnlyList<IWorkflowExecutor> executors,
        bool isResume,
        CancellationToken cancellationToken)
    {
        if (executors.Count == 0)
        {
            throw new InvalidOperationException("Workflow requires at least one executor.");
        }

        if (!isResume)
        {
            context.ApplyStatus(WorkflowCheckpointStatus.Running);
            await PublishTrackedEventAsync(
                context,
                WorkflowEventType.WorkflowStarted,
                DateTimeOffset.UtcNow,
                new { isResume = false },
                cancellationToken);
        }

        var startIndex = ResolveStartIndex(context, executors);

        for (var index = startIndex; index < executors.Count; index++)
        {
            var executor = executors[index];
            var startedAt = DateTimeOffset.UtcNow;
            var executionId = await workflowExecutionAuditService.StartExecutionAsync(
                context,
                executor.Name,
                startedAt,
                cancellationToken);

            context.SetCurrentExecutor(executor.Name);
            context.ApplyStatus(WorkflowCheckpointStatus.Running);

            await SaveCheckpointAndPublishAsync(context, cancellationToken);
            await PublishTrackedEventAsync(
                context,
                WorkflowEventType.ExecutorStarted,
                startedAt,
                WorkflowEventPayloadFactory.BuildExecutorStartedPayload(context, executor.Name, startedAt),
                cancellationToken);

            try
            {
                var result = await executor.ExecuteAsync(context, cancellationToken);
                var completedAt = DateTimeOffset.UtcNow;
                var durationMs = (long)(completedAt - startedAt).TotalMilliseconds;

                if (!result.IsSuccess)
                {
                    workflowStateMachine.EnsureCanTransition(context.Status, WorkflowCheckpointStatus.Failed);
                    context.ApplyStatus(WorkflowCheckpointStatus.Failed);
                    context.Set("LastError", result.ErrorMessage ?? "Unknown workflow error.");

                    await workflowExecutionAuditService.FailExecutionAsync(
                        context,
                        executionId,
                        executor.Name,
                        result.ErrorMessage ?? "Unknown workflow error.",
                        startedAt,
                        completedAt,
                        result.Output,
                        cancellationToken: cancellationToken);

                    await SaveCheckpointAndPublishAsync(context, cancellationToken);
                await PublishTrackedEventAsync(
                    context,
                    WorkflowEventType.ExecutorFailed,
                    completedAt,
                    WorkflowEventPayloadFactory.BuildExecutorFailedPayload(
                        context,
                        executor.Name,
                        result.ErrorMessage ?? "Unknown workflow error.",
                        completedAt,
                        durationMs,
                        result.Output),
                    cancellationToken);
                    await PublishTrackedEventAsync(
                        context,
                        WorkflowEventType.WorkflowFailed,
                        completedAt,
                        new { executorName = executor.Name, errorMessage = result.ErrorMessage },
                        cancellationToken);

                    return BuildResult(context, result.ErrorMessage);
                }

                context.MarkExecutorCompleted(executor.Name);

                workflowStateMachine.EnsureCanTransition(context.Status, result.NextStatus);
                context.ApplyStatus(result.NextStatus);

                await workflowExecutionAuditService.CompleteExecutionAsync(
                    context,
                    executionId,
                    executor.Name,
                    result,
                    startedAt,
                    completedAt,
                    cancellationToken);

                await PublishTrackedEventAsync(
                    context,
                    WorkflowEventType.ExecutorCompleted,
                    completedAt,
                    WorkflowEventPayloadFactory.BuildExecutorCompletedPayload(
                        context,
                        executor.Name,
                        result,
                        completedAt,
                        durationMs),
                    cancellationToken);

                await SaveCheckpointAndPublishAsync(context, cancellationToken);

                if (result.NextStatus == WorkflowCheckpointStatus.WaitingForReview)
                {
                    await PublishTrackedEventAsync(
                        context,
                        WorkflowEventType.WorkflowWaitingReview,
                        completedAt,
                        new { executorName = executor.Name },
                        cancellationToken);

                    return BuildResult(context, errorMessage: null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || context.CancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Workflow executor cancelled. SessionId={SessionId}, ExecutorName={ExecutorName}",
                    context.SessionId,
                    executor.Name);

                if (context.Status != WorkflowCheckpointStatus.Cancelled)
                {
                    workflowStateMachine.EnsureCanTransition(context.Status, WorkflowCheckpointStatus.Cancelled);
                    context.ApplyStatus(WorkflowCheckpointStatus.Cancelled);
                }

                await workflowExecutionAuditService.CancelExecutionAsync(
                    context,
                    executionId,
                    executor.Name,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    CancellationToken.None);

                context.Set("LastError", "Workflow cancelled.");
                if (!WorkflowTimeline.GetEvents(context).Any(item => item.EventType == WorkflowEventType.WorkflowCancelled))
                {
                    await PublishTrackedEventAsync(
                        context,
                        WorkflowEventType.WorkflowCancelled,
                        DateTimeOffset.UtcNow,
                        new { reason = "CancellationTokenTriggered", executorName = executor.Name },
                        CancellationToken.None);
                }

                await SaveCheckpointAndPublishAsync(context, CancellationToken.None);
                return BuildResult(context, "Workflow cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow executor failed unexpectedly. SessionId={SessionId}, ExecutorName={ExecutorName}", context.SessionId, executor.Name);

                workflowStateMachine.EnsureCanTransition(context.Status, WorkflowCheckpointStatus.Failed);
                context.ApplyStatus(WorkflowCheckpointStatus.Failed);
                context.Set("LastError", ex.Message);

                await workflowExecutionAuditService.FailExecutionAsync(
                    context,
                    executionId,
                    executor.Name,
                    ex.Message,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    exception: ex,
                    cancellationToken: cancellationToken);

                await SaveCheckpointAndPublishAsync(context, cancellationToken);
                await PublishTrackedEventAsync(
                    context,
                    WorkflowEventType.ExecutorFailed,
                    DateTimeOffset.UtcNow,
                    WorkflowEventPayloadFactory.BuildExecutorFailedPayload(
                        context,
                        executor.Name,
                        ex.Message,
                        DateTimeOffset.UtcNow,
                        (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                    cancellationToken);
                await PublishTrackedEventAsync(
                    context,
                    WorkflowEventType.WorkflowFailed,
                    DateTimeOffset.UtcNow,
                    new { executorName = executor.Name, errorMessage = ex.Message },
                    cancellationToken);

                return BuildResult(context, ex.Message);
            }
        }

        workflowStateMachine.EnsureCanTransition(context.Status, WorkflowCheckpointStatus.Completed);
        context.ApplyStatus(WorkflowCheckpointStatus.Completed);

        await PublishTrackedEventAsync(
            context,
            WorkflowEventType.WorkflowCompleted,
            DateTimeOffset.UtcNow,
            new { completedExecutors = context.CompletedExecutors.Count },
            cancellationToken);
        await SaveCheckpointAndPublishAsync(context, cancellationToken);
        await checkpointStorage.DeleteCheckpointAsync(context.SessionId, cancellationToken);

        return BuildResult(context, errorMessage: null);
    }

    private async Task SaveCheckpointAndPublishAsync(WorkflowContext context, CancellationToken cancellationToken)
    {
        var checkpointVersion = context.AdvanceCheckpointVersion();
        var checkpointSavedEvent = new WorkflowEventMessage(
            WorkflowEventType.CheckpointSaved,
            context.SessionId,
            context.WorkflowType,
            DateTimeOffset.UtcNow,
            new { checkpointVersion, currentExecutor = context.CurrentExecutor, status = context.Status.ToString() });
        var trackedEvent = WorkflowTimeline.Append(context, checkpointSavedEvent);
        var checkpoint = context.CreateCheckpointSnapshot();

        await checkpointStorage.SaveCheckpointAsync(checkpoint, cancellationToken);
        await workflowEventPublisher.PublishAsync(checkpointSavedEvent with { Sequence = trackedEvent.Sequence }, cancellationToken);
    }

    private async Task PublishTrackedEventAsync(
        WorkflowContext context,
        WorkflowEventType eventType,
        DateTimeOffset timestamp,
        object payload,
        CancellationToken cancellationToken)
    {
        var workflowEvent = new WorkflowEventMessage(
            eventType,
            context.SessionId,
            context.WorkflowType,
            timestamp,
            payload);
        var trackedEvent = WorkflowTimeline.Append(context, workflowEvent);
        await workflowEventPublisher.PublishAsync(workflowEvent with { Sequence = trackedEvent.Sequence }, cancellationToken);
    }

    private static int ResolveStartIndex(WorkflowContext context, IReadOnlyList<IWorkflowExecutor> executors)
    {
        if (!string.IsNullOrWhiteSpace(context.CurrentExecutor))
        {
            var currentIndex = executors.ToList().FindIndex(executor => executor.Name == context.CurrentExecutor);
            if (currentIndex >= 0)
            {
                return currentIndex;
            }
        }

        for (var index = 0; index < executors.Count; index++)
        {
            if (!context.CompletedExecutors.Contains(executors[index].Name))
            {
                return index;
            }
        }

        return executors.Count;
    }

    private static WorkflowRunResult BuildResult(WorkflowContext context, string? errorMessage)
    {
        return new WorkflowRunResult(
            context.SessionId,
            context.Status,
            context.CurrentExecutor,
            context.CompletedExecutors,
            context.CheckpointVersion,
            errorMessage);
    }
}
