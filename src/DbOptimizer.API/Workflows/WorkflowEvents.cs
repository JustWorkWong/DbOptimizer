namespace DbOptimizer.API.Workflows;

internal enum WorkflowEventType
{
    WorkflowStarted,
    ExecutorStarted,
    ExecutorCompleted,
    ExecutorFailed,
    WorkflowWaitingReview,
    WorkflowCompleted,
    WorkflowFailed,
    CheckpointSaved
}

internal sealed record WorkflowEventMessage(
    WorkflowEventType EventType,
    Guid SessionId,
    string WorkflowType,
    DateTimeOffset Timestamp,
    object Payload);

internal interface IWorkflowEventPublisher
{
    Task PublishAsync(WorkflowEventMessage workflowEvent, CancellationToken cancellationToken = default);
}

/* =========================
 * Workflow 事件发布器
 * 设计目标：
 * 1) 在真正接入 SSE 之前，先提供统一的事件发布抽象
 * 2) 当前默认用结构化日志承载事件，便于联调与排障
 * 3) 后续替换为 SSE / 消息总线时不影响工作流框架
 * ========================= */
internal sealed class LoggingWorkflowEventPublisher(ILogger<LoggingWorkflowEventPublisher> logger) : IWorkflowEventPublisher
{
    public Task PublishAsync(WorkflowEventMessage workflowEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Workflow event published. EventType={EventType}, SessionId={SessionId}, WorkflowType={WorkflowType}, Payload={Payload}",
            workflowEvent.EventType,
            workflowEvent.SessionId,
            workflowEvent.WorkflowType,
            workflowEvent.Payload);

        return Task.CompletedTask;
    }
}
