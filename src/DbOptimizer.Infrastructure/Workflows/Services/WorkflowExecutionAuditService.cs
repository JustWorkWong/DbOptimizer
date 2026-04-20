using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using System.Text.Json;
using DbOptimizer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Workflows;

public interface IWorkflowExecutionAuditService
{
    Task<Guid?> StartExecutionAsync(
        WorkflowContext context,
        string executorName,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task CompleteExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        WorkflowExecutorResult result,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task FailExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        string errorMessage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        object? output = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default);

    Task CancelExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowExecutionAuditService(
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    ILogger<WorkflowExecutionAuditService> logger) : IWorkflowExecutionAuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid?> StartExecutionAsync(
        WorkflowContext context,
        string executorName,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entity = new AgentExecutionEntity
            {
                ExecutionId = Guid.NewGuid(),
                SessionId = context.SessionId,
                AgentName = executorName,
                ExecutorName = executorName,
                StartedAt = startedAt,
                Status = "Running",
                InputData = WorkflowExecutionAuditHelper.Serialize(new
                {
                    workflowType = context.WorkflowType,
                    executorName,
                    checkpointVersion = context.CheckpointVersion,
                    keys = context.Data.Keys.OrderBy(key => key).ToArray(),
                    sqlText = WorkflowExecutionAuditHelper.TryGetValue<string>(context, WorkflowContextKeys.SqlText),
                    databaseId = WorkflowExecutionAuditHelper.TryGetValue<string>(context, WorkflowContextKeys.DatabaseId),
                    databaseType = WorkflowExecutionAuditHelper.TryGetValue<string>(context, WorkflowContextKeys.DatabaseType)
                })
            };

            dbContext.AgentExecutions.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            return entity.ExecutionId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution start. SessionId={SessionId}, ExecutorName={ExecutorName}",
                context.SessionId,
                executorName);
            return null;
        }
    }

    public async Task CompleteExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        WorkflowExecutorResult result,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = result.NextStatus == Checkpointing.WorkflowCheckpointStatus.WaitingForReview
                ? WorkflowSessionStatus.WaitingForReview
                : "Completed";
            entity.OutputData = WorkflowExecutionAuditHelper.Serialize(new
            {
                nextStatus = result.NextStatus.ToString(),
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                output = result.Output
            });
            entity.TokenUsage = WorkflowExecutionAuditHelper.Serialize(WorkflowExecutionAuditHelper.BuildTokenUsage(result.Output));
            entity.ErrorMessage = null;

            var toolCalls = WorkflowExecutionAuditHelper.BuildToolCalls(context, executionId.Value, executorName, startedAt, completedAt, result.Output);
            var decisionRecords = WorkflowExecutionAuditHelper.BuildDecisionRecords(executionId.Value, executorName, result.Output);
            var errorLogs = WorkflowExecutionAuditHelper.BuildRecoveredErrorLogs(context, executionId.Value, executorName, result.Output);

            if (toolCalls.Count > 0)
            {
                dbContext.ToolCalls.AddRange(toolCalls);
            }

            if (decisionRecords.Count > 0)
            {
                dbContext.DecisionRecords.AddRange(decisionRecords);
            }

            if (errorLogs.Count > 0)
            {
                dbContext.ErrorLogs.AddRange(errorLogs);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution completion. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

    public async Task FailExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        string errorMessage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        object? output = null,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = "Failed";
            entity.OutputData = WorkflowExecutionAuditHelper.Serialize(new
            {
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                output
            });
            entity.TokenUsage = WorkflowExecutionAuditHelper.Serialize(WorkflowExecutionAuditHelper.BuildTokenUsage(output));
            entity.ErrorMessage = errorMessage;

            dbContext.ErrorLogs.Add(new ErrorLogEntity
            {
                LogId = Guid.NewGuid(),
                SessionId = context.SessionId,
                ExecutionId = executionId.Value,
                ErrorType = exception?.GetType().Name ?? "ExecutorFailure",
                ErrorMessage = errorMessage,
                StackTrace = exception?.ToString(),
                Context = WorkflowExecutionAuditHelper.Serialize(new
                {
                    workflowType = context.WorkflowType,
                    executorName,
                    checkpointVersion = context.CheckpointVersion
                }),
                CreatedAt = completedAt
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution failure. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

    public async Task CancelExecutionAsync(
        WorkflowContext context,
        Guid? executionId,
        string executorName,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        if (!executionId.HasValue)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await dbContext.AgentExecutions
                .SingleOrDefaultAsync(item => item.ExecutionId == executionId.Value, cancellationToken);

            if (entity is null)
            {
                return;
            }

            entity.CompletedAt = completedAt;
            entity.Status = "Cancelled";
            entity.ErrorMessage = "Workflow cancelled.";
            entity.OutputData = WorkflowExecutionAuditHelper.Serialize(new
            {
                durationMs = (long)(completedAt - startedAt).TotalMilliseconds
            });
            entity.TokenUsage = WorkflowExecutionAuditHelper.Serialize(WorkflowExecutionAuditHelper.BuildTokenUsage(null));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to persist workflow execution cancellation. SessionId={SessionId}, ExecutorName={ExecutorName}, ExecutionId={ExecutionId}",
                context.SessionId,
                executorName,
                executionId);
        }
    }

}
