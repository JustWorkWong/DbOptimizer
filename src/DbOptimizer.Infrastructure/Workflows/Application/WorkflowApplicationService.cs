using System.Collections.Concurrent;
using System.Text.Json;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Checkpointing;
using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Maf.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Workflows.Application;

/// <summary>
/// Workflow 应用服务实现（已迁移到 MAF Runtime）
/// </summary>
public sealed class WorkflowApplicationService(
    IMafWorkflowRuntime mafWorkflowRuntime,
    IDbContextFactory<DbOptimizerDbContext> dbContextFactory,
    IWorkflowResultSerializer workflowResultSerializer,
    ILogger<WorkflowApplicationService> logger) : IWorkflowApplicationService
{

    public async Task<WorkflowStartResponse> StartSqlAnalysisAsync(
        CreateSqlAnalysisWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = WorkflowRequestValidator.ValidateCreateSqlAnalysisRequest(request);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Validation failed: {string.Join("; ", validationResult.Errors)}");
        }

        var databaseEngine = ResolveDatabaseEngine(request.DatabaseEngine, request.DatabaseId);
        var sessionId = Guid.NewGuid();

        // 委托给 MAF Runtime
        var command = new SqlAnalysisWorkflowCommand(
            SessionId: sessionId,
            SqlText: request.SqlText.Trim(),
            DatabaseType: databaseEngine,
            SchemaName: null,
            EnableIndexRecommendation: request.Options.EnableIndexRecommendation,
            EnableSqlRewrite: request.Options.EnableSqlRewrite,
            RequireHumanReview: request.Options.RequireHumanReview,
            DatabaseId: request.DatabaseId.Trim(),
            SourceType: request.SourceType,
            SourceRefId: request.SourceRefId);

        var mafResponse = await mafWorkflowRuntime.StartSqlAnalysisAsync(command, cancellationToken);

        // 返回统一格式的响应
        return new WorkflowStartResponse(
            mafResponse.SessionId,
            "SqlAnalysis",
            "maf",
            mafResponse.Status,
            DateTimeOffset.UtcNow);
    }

    public async Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(
        CreateDbConfigOptimizationWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = WorkflowRequestValidator.ValidateCreateDbConfigOptimizationRequest(request);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Validation failed: {string.Join("; ", validationResult.Errors)}");
        }

        var databaseEngine = ResolveDatabaseEngine(request.DatabaseType, request.DatabaseId);
        var sessionId = Guid.NewGuid();

        // 委托给 MAF Runtime
        var command = new DbConfigWorkflowCommand(
            SessionId: sessionId,
            DatabaseId: request.DatabaseId.Trim(),
            DatabaseType: databaseEngine,
            AllowFallbackSnapshot: request.Options.AllowFallbackSnapshot,
            RequireHumanReview: request.Options.RequireHumanReview);

        var mafResponse = await mafWorkflowRuntime.StartDbConfigOptimizationAsync(command, cancellationToken);

        // 返回统一格式的响应
        return new WorkflowStartResponse(
            mafResponse.SessionId,
            "DbConfigOptimization",
            "maf",
            mafResponse.Status,
            DateTimeOffset.UtcNow);
    }

    public async Task<WorkflowStatusResponse?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        // 从 workflow_sessions 表读取状态（MAF 和 legacy 都存储在这里）
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await dbContext.WorkflowSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session is null)
        {
            return null;
        }

        // 解析 state JSON（如果有）
        WorkflowResultEnvelope? result = null;
        WorkflowReviewSummaryDto? review = null;
        WorkflowErrorDto? error = null;
        string? databaseId = null;
        string? databaseType = null;

        if (!string.IsNullOrWhiteSpace(session.State) && session.State != "{}")
        {
            try
            {
                var stateDoc = JsonDocument.Parse(session.State);

                // 提取 databaseId 和 databaseType
                if (stateDoc.RootElement.TryGetProperty("DatabaseId", out var dbIdElement))
                {
                    databaseId = dbIdElement.GetString();
                }
                if (stateDoc.RootElement.TryGetProperty("DatabaseType", out var dbTypeElement))
                {
                    databaseType = dbTypeElement.GetString();
                }

                // 尝试提取 result
                if (stateDoc.RootElement.TryGetProperty("FinalResult", out var finalResultElement))
                {
                    result = workflowResultSerializer.ToEnvelope(
                        session.WorkflowType,
                        finalResultElement,
                        databaseId,
                        databaseType);
                }

                // 尝试提取 review
                if (stateDoc.RootElement.TryGetProperty("ReviewId", out var reviewIdElement) &&
                    reviewIdElement.ValueKind != JsonValueKind.Null)
                {
                    var reviewId = reviewIdElement.GetGuid();
                    var reviewStatus = stateDoc.RootElement.TryGetProperty("ReviewStatus", out var statusElement)
                        ? statusElement.GetString() ?? "Unknown"
                        : "Unknown";
                    review = new WorkflowReviewSummaryDto(reviewId, reviewStatus);
                }

                // 尝试提取 error
                if (stateDoc.RootElement.TryGetProperty("LastError", out var errorElement) &&
                    errorElement.ValueKind == JsonValueKind.String)
                {
                    var errorMessage = errorElement.GetString();
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        error = new WorkflowErrorDto("WORKFLOW_ERROR", errorMessage, null);
                    }
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Failed to parse workflow state JSON. SessionId={SessionId}", sessionId);
            }
        }

        // 如果有错误消息但没有解析出 error 对象，使用 ErrorMessage 字段
        if (error is null && !string.IsNullOrWhiteSpace(session.ErrorMessage))
        {
            error = new WorkflowErrorDto("WORKFLOW_ERROR", session.ErrorMessage, null);
        }

        return new WorkflowStatusResponse(
            session.SessionId,
            session.WorkflowType,
            session.EngineType,
            session.Status,
            null, // CurrentExecutor - MAF 不暴露这个细节
            CalculateProgressFromStatus(session.Status),
            session.CreatedAt,
            session.UpdatedAt,
            session.CompletedAt,
            new WorkflowSourceDto(session.SourceType, session.SourceRefId?.ToString()),
            review,
            result,
            error);
    }

    private static int CalculateProgressFromStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed" => 100,
            "failed" => 100,
            "cancelled" => 100,
            "waitingforreview" => 85,
            "waitingreview" => 85,
            "suspended" => 85,
            "running" => 50,
            "pending" => 10,
            _ => 0
        };
    }

    public async Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        // 委托给 MAF Runtime
        var mafResponse = await mafWorkflowRuntime.CancelAsync(sessionId, cancellationToken);

        return new WorkflowCancelResponse(
            mafResponse.SessionId,
            "Unknown", // MAF Runtime 不返回 WorkflowType，需要从数据库查询
            "maf",
            mafResponse.Status);
    }

    private static string ResolveDatabaseEngine(string? databaseEngine, string databaseId)
    {
        var candidate = string.IsNullOrWhiteSpace(databaseEngine)
            ? databaseId
            : databaseEngine;

        if (candidate.Contains("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return "mysql";
        }

        if (candidate.Contains("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return "postgresql";
        }

        throw new InvalidOperationException(
            $"Unable to resolve database engine. Provide DatabaseEngine or a DatabaseId that includes mysql/postgres. DatabaseId={databaseId}, DatabaseEngine={databaseEngine}");
    }
}
