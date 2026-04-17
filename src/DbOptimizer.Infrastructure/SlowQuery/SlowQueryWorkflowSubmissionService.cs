using DbOptimizer.Infrastructure.Persistence;
using DbOptimizer.Infrastructure.Workflows.Application;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.SlowQuery;

/// <summary>
/// 慢查询工作流提交服务实现
/// 职责：
/// 1) 将慢查询自动提交为 SQL 分析工作流
/// 2) 设置 sourceType = "slow-query"
/// 3) 设置 sourceRefId = queryId
/// </summary>
public sealed class SlowQueryWorkflowSubmissionService(
    IWorkflowApplicationService workflowApplicationService,
    ILogger<SlowQueryWorkflowSubmissionService> logger) : ISlowQueryWorkflowSubmissionService
{
    public async Task<Guid> SubmitAsync(
        SlowQueryEntity slowQuery,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CreateSqlAnalysisWorkflowRequest
            {
                SqlText = slowQuery.SqlFingerprint,
                DatabaseId = slowQuery.DatabaseId,
                DatabaseEngine = slowQuery.DatabaseType,
                SourceType = "slow-query",
                SourceRefId = slowQuery.QueryId,
                Options = new SqlAnalysisWorkflowOptions
                {
                    EnableIndexRecommendation = true,
                    EnableSqlRewrite = true,
                    RequireHumanReview = true
                }
            };

            var response = await workflowApplicationService.StartSqlAnalysisAsync(request, cancellationToken);

            logger.LogInformation(
                "慢查询已自动提交为工作流。QueryId={QueryId}, SessionId={SessionId}, DatabaseId={DatabaseId}",
                slowQuery.QueryId,
                response.SessionId,
                slowQuery.DatabaseId);

            return response.SessionId;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "慢查询工作流提交失败。QueryId={QueryId}, DatabaseId={DatabaseId}",
                slowQuery.QueryId,
                slowQuery.DatabaseId);
            throw;
        }
    }
}
