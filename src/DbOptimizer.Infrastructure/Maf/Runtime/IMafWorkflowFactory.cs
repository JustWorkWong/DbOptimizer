using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 工厂接口，负责按类型构建 workflow 实例
/// </summary>
public interface IMafWorkflowFactory
{
    /// <summary>
    /// 构建 SQL 分析 workflow
    /// </summary>
    Workflow BuildSqlAnalysisWorkflow();

    /// <summary>
    /// 构建数据库配置优化 workflow
    /// </summary>
    Workflow BuildDbConfigWorkflow();
}
