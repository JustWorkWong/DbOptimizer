using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;
using DbOptimizer.Infrastructure.Maf.DbConfig.Executors;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

/// <summary>
/// MAF Workflow 工厂实现
/// 职责：使用 WorkflowBuilder 构建 SQL 和 Config workflow graph
/// </summary>
public sealed class MafWorkflowFactory : IMafWorkflowFactory
{
    private readonly IServiceProvider _serviceProvider;

    public MafWorkflowFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 构建 SQL 分析 workflow
    /// Graph: validation → parser → plan → parallel(index, rewrite) → coordinator → review gate
    /// </summary>
    public Workflow BuildSqlAnalysisWorkflow()
    {
        // 创建 executor bindings
        var validation = CreateBinding<SqlInputValidationExecutor>("sql-validation");
        var parser = CreateBinding<SqlParserMafExecutor>("sql-parser");
        var plan = CreateBinding<ExecutionPlanMafExecutor>("execution-plan");
        var indexAdvisor = CreateBinding<IndexAdvisorMafExecutor>("index-advisor");
        var sqlRewrite = CreateBinding<SqlRewriteMafExecutor>("sql-rewrite");
        var coordinator = CreateBinding<SqlCoordinatorMafExecutor>("sql-coordinator");
        var reviewGate = CreateBinding<SqlHumanReviewGateExecutor>("sql-review-gate");

        // 构建 workflow graph
        var builder = new WorkflowBuilder(validation);

        // Sequential: validation → parser → plan
        builder.AddEdge(validation, parser);
        builder.AddEdge(parser, plan);

        // Parallel: plan → index + rewrite
        builder.AddEdge(plan, indexAdvisor);

        // Sequential: index → rewrite (rewrite depends on index output)
        builder.AddEdge(indexAdvisor, sqlRewrite);

        // Sequential: rewrite → coordinator
        builder.AddEdge(sqlRewrite, coordinator);

        // Sequential: coordinator → review gate
        builder.AddEdge(coordinator, reviewGate);

        return builder.Build();
    }

    /// <summary>
    /// 构建数据库配置优化 workflow
    /// Graph: validation → collector → analyzer → coordinator → review gate
    /// </summary>
    public Workflow BuildDbConfigWorkflow()
    {
        // 创建 executor bindings
        var validation = CreateBinding<DbConfigInputValidationExecutor>("config-validation");
        var collector = CreateBinding<ConfigCollectorMafExecutor>("config-collector");
        var analyzer = CreateBinding<ConfigAnalyzerMafExecutor>("config-analyzer");
        var coordinator = CreateBinding<ConfigCoordinatorMafExecutor>("config-coordinator");
        var reviewGate = CreateBinding<ConfigHumanReviewGateExecutor>("config-review-gate");

        // 构建 workflow graph
        var builder = new WorkflowBuilder(validation);

        // Sequential: validation → collector → analyzer → coordinator → review gate
        builder.AddEdge(validation, collector);
        builder.AddEdge(collector, analyzer);
        builder.AddEdge(analyzer, coordinator);
        builder.AddEdge(coordinator, reviewGate);

        return builder.Build();
    }

    /// <summary>
    /// 创建 ExecutorBinding
    /// </summary>
    private ExecutorBinding CreateBinding<TExecutor>(string id) where TExecutor : Executor
    {
        return new ServiceProviderExecutorBinding(
            id,
            _ => ValueTask.FromResult<Executor>(_serviceProvider.GetRequiredService<TExecutor>()),
            typeof(TExecutor),
            null);
    }
}

/// <summary>
/// 基于 IServiceProvider 的 ExecutorBinding 实现
/// </summary>
internal record ServiceProviderExecutorBinding(
    string Id,
    Func<string, ValueTask<Executor>> FactoryAsync,
    Type ExecutorType,
    object? RawValue) : ExecutorBinding(Id, FactoryAsync, ExecutorType, RawValue)
{
    public override bool IsSharedInstance => true;
    public override bool SupportsConcurrentSharedExecution => false;
    public override bool SupportsResetting => false;
}
