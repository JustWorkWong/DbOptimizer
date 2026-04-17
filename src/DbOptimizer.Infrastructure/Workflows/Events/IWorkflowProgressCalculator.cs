namespace DbOptimizer.Infrastructure.Workflows.Events;

/// <summary>
/// 工作流进度计算器
/// 根据工作流类型和当前节点计算进度百分比
/// </summary>
public interface IWorkflowProgressCalculator
{
    /// <summary>
    /// 计算工作流进度百分比
    /// </summary>
    /// <param name="workflowType">工作流类型（SqlAnalysis / DbConfigOptimization）</param>
    /// <param name="nodeName">当前执行节点名称</param>
    /// <param name="status">当前状态</param>
    /// <returns>进度百分比 (0-100)</returns>
    int GetProgressPercent(string workflowType, string nodeName, string status);
}
