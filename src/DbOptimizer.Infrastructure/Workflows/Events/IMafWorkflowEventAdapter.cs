using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Workflows.Application;

namespace DbOptimizer.Infrastructure.Workflows.Events;

/// <summary>
/// MAF 工作流事件适配器
/// 将 MAF 内部事件转换为业务可消费事件
/// </summary>
public interface IMafWorkflowEventAdapter
{
    /// <summary>
    /// 创建快照事件
    /// </summary>
    WorkflowEventRecord CreateSnapshot(WorkflowStatusResponse snapshot);

    /// <summary>
    /// 映射 MAF 事件到业务事件
    /// </summary>
    IReadOnlyList<WorkflowEventRecord> Map(
        Guid sessionId,
        string workflowType,
        IReadOnlyList<WorkflowEventRecord> mafEvents);
}
