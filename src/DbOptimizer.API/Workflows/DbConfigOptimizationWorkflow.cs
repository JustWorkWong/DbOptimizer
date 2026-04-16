namespace DbOptimizer.API.Workflows;

/* =========================
 * DbConfigOptimizationWorkflow
 * 职责：定义配置优化工作流的 Executor 执行顺序
 * ========================= */
internal static class DbConfigOptimizationWorkflow
{
    public static IReadOnlyList<IWorkflowExecutor> CreateExecutors(IServiceProvider services)
    {
        return new IWorkflowExecutor[]
        {
            services.GetRequiredService<ConfigCollectorExecutor>(),
            services.GetRequiredService<ConfigAnalyzerExecutor>(),
            services.GetRequiredService<ConfigCoordinatorExecutor>(),
            services.GetRequiredService<ConfigReviewExecutor>()
        };
    }
}
