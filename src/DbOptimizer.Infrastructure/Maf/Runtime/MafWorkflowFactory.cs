using Microsoft.Agents.AI.Workflows;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 工厂实现（占位实现）
/// </summary>
public sealed class MafWorkflowFactory : IMafWorkflowFactory
{
    public Workflow BuildSqlAnalysisWorkflow()
    {
        // TODO: 实现 SQL 分析 workflow 构建逻辑
        throw new NotImplementedException("SQL Analysis Workflow not yet implemented");
    }

    public Workflow BuildDbConfigWorkflow()
    {
        // TODO: 实现数据库配置优化 workflow 构建逻辑
        throw new NotImplementedException("DB Config Workflow not yet implemented");
    }
}
