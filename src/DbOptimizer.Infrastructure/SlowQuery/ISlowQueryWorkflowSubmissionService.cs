namespace DbOptimizer.Infrastructure.SlowQuery;

/// <summary>
/// 慢查询工作流提交服务接口
/// 职责：将慢查询自动提交为 SQL 分析工作流
/// </summary>
public interface ISlowQueryWorkflowSubmissionService
{
    /// <summary>
    /// 提交慢查询为 SQL 分析工作流
    /// </summary>
    /// <param name="slowQuery">慢查询实体</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的工作流会话 ID</returns>
    Task<Guid> SubmitAsync(
        DbOptimizer.Infrastructure.Persistence.SlowQueryEntity slowQuery,
        CancellationToken cancellationToken = default);
}
