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
        var validation = CreateBinding<SqlInputValidationExecutor, SqlAnalysis.SqlAnalysisWorkflowCommand, SqlAnalysis.SqlAnalysisWorkflowCommand>("sql_analysis");
        var parser = CreateBinding<SqlParserMafExecutor, SqlAnalysis.SqlAnalysisWorkflowCommand, SqlAnalysis.SqlParsingCompletedMessage>("sql_analysis");
        var plan = CreateBinding<ExecutionPlanMafExecutor, SqlAnalysis.SqlParsingCompletedMessage, SqlAnalysis.ExecutionPlanCompletedMessage>("sql_analysis");
        var indexAdvisor = CreateBinding<IndexAdvisorMafExecutor, SqlAnalysis.ExecutionPlanCompletedMessage, SqlAnalysis.IndexRecommendationCompletedMessage>("sql_analysis");
        var sqlRewrite = CreateBinding<SqlRewriteMafExecutor, SqlAnalysis.IndexRecommendationCompletedMessage, SqlAnalysis.SqlRewriteCompletedMessage>("sql_analysis");
        var coordinator = CreateBinding<SqlCoordinatorMafExecutor, SqlAnalysis.SqlRewriteCompletedMessage, SqlAnalysis.SqlOptimizationDraftReadyMessage>("sql_analysis");
        var reviewGate = CreateBinding<SqlHumanReviewGateExecutor>();
        var reviewPort = MafReviewPorts.SqlReview;

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
        builder.AddEdge(reviewGate, reviewPort);
        builder.AddEdge(reviewPort, reviewGate);
        builder.WithOutputFrom(reviewGate);

        return builder.Build();
    }

    /// <summary>
    /// 构建数据库配置优化 workflow
    /// Graph: validation → collector → analyzer → coordinator → review gate
    /// </summary>
    public Workflow BuildDbConfigWorkflow()
    {
        // 创建 executor bindings
        var validation = CreateBinding<DbConfigInputValidationExecutor, DbConfig.DbConfigWorkflowCommand, DbConfig.DbConfigWorkflowCommand>("db_config_optimization");
        var collector = CreateBinding<ConfigCollectorMafExecutor, DbConfig.DbConfigWorkflowCommand, DbConfig.ConfigSnapshotCollectedMessage>("db_config_optimization");
        var analyzer = CreateBinding<ConfigAnalyzerMafExecutor, DbConfig.ConfigSnapshotCollectedMessage, DbConfig.ConfigRecommendationsGeneratedMessage>("db_config_optimization");
        var coordinator = CreateBinding<ConfigCoordinatorMafExecutor, DbConfig.ConfigRecommendationsGeneratedMessage, DbConfig.DbConfigOptimizationDraftReadyMessage>("db_config_optimization");
        var reviewGate = CreateBinding<ConfigHumanReviewGateExecutor>();
        var reviewPort = MafReviewPorts.ConfigReview;

        // 构建 workflow graph
        var builder = new WorkflowBuilder(validation);

        // Sequential: validation → collector → analyzer → coordinator → review gate
        builder.AddEdge(validation, collector);
        builder.AddEdge(collector, analyzer);
        builder.AddEdge(analyzer, coordinator);
        builder.AddEdge(coordinator, reviewGate);
        builder.AddEdge(reviewGate, reviewPort);
        builder.AddEdge(reviewPort, reviewGate);
        builder.WithOutputFrom(reviewGate);

        return builder.Build();
    }

    /// <summary>
    /// 创建 ExecutorBinding
    /// </summary>
    private ExecutorBinding CreateBinding<TExecutor, TInput, TOutput>(string workflowType)
        where TExecutor : Executor<TInput, TOutput>
    {
        var executor = _serviceProvider.GetRequiredService<TExecutor>();
        var instrumentation = _serviceProvider.GetRequiredService<IMafExecutorInstrumentation>();
        var wrappedExecutor = new ObservedExecutor<TInput, TOutput>(workflowType, executor, instrumentation);

        return new ServiceProviderExecutorBinding(
            wrappedExecutor.Id,
            _ => ValueTask.FromResult<Executor>(wrappedExecutor),
            wrappedExecutor.GetType(),
            null);
    }

    private ExecutorBinding CreateBinding<TExecutor, TInput>(string workflowType)
        where TExecutor : Executor<TInput>
    {
        var executor = _serviceProvider.GetRequiredService<TExecutor>();
        var instrumentation = _serviceProvider.GetRequiredService<IMafExecutorInstrumentation>();
        var wrappedExecutor = new ObservedExecutor<TInput>(workflowType, executor, instrumentation);

        return new ServiceProviderExecutorBinding(
            wrappedExecutor.Id,
            _ => ValueTask.FromResult<Executor>(wrappedExecutor),
            wrappedExecutor.GetType(),
            null);
    }

    private ExecutorBinding CreateBinding<TExecutor>()
        where TExecutor : Executor
    {
        var executor = _serviceProvider.GetRequiredService<TExecutor>();

        return new ServiceProviderExecutorBinding(
            executor.Id,
            _ => ValueTask.FromResult<Executor>(executor),
            executor.GetType(),
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
