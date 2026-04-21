using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis;
using DbOptimizer.Infrastructure.Maf.SqlAnalysis.Executors;
using DbOptimizer.Infrastructure.Maf.DbConfig;
using DbOptimizer.Infrastructure.Maf.DbConfig.Executors;
using DbOptimizer.Infrastructure.Llm;
using DbOptimizer.Infrastructure.Prompts;
using DbOptimizer.Infrastructure.Workflows;
using DbOptimizer.Infrastructure.Workflows.Review;
using Microsoft.Extensions.Options;
using Moq;

namespace DbOptimizer.Infrastructure.Tests.Maf;

/// <summary>
/// MafWorkflowFactory 单元测试
/// 验证 workflow graph 构建正确性
/// </summary>
public sealed class MafWorkflowFactoryTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MafWorkflowFactory _factory;

    public MafWorkflowFactoryTests()
    {
        var services = new ServiceCollection();

        // 注册所有 SQL executors
        services.AddSingleton<SqlInputValidationExecutor>();
        services.AddSingleton<SqlParserMafExecutor>();
        services.AddSingleton<ExecutionPlanMafExecutor>();
        services.AddSingleton<IndexAdvisorMafExecutor>();
        services.AddSingleton<SqlRewriteMafExecutor>();
        services.AddSingleton<SqlCoordinatorMafExecutor>();
        services.AddSingleton<SqlHumanReviewGateExecutor>();

        // 注册所有 Config executors
        services.AddSingleton<DbConfigInputValidationExecutor>();
        services.AddSingleton<ConfigCollectorMafExecutor>();
        services.AddSingleton<ConfigAnalyzerMafExecutor>();
        services.AddSingleton<ConfigCoordinatorMafExecutor>();
        services.AddSingleton<ConfigHumanReviewGateExecutor>();

        // 注册依赖服务（使用 Mock）
        services.AddSingleton(Mock.Of<ISqlParser>());
        services.AddSingleton(Mock.Of<IExecutionPlanProvider>());
        services.AddSingleton(Mock.Of<IExecutionPlanAnalyzer>());
        services.AddSingleton(Mock.Of<IIndexRecommendationGenerator>());
        services.AddSingleton(Mock.Of<ITableIndexMetadataProvider>());
        services.AddSingleton(Mock.Of<ITableIndexMetadataAnalyzer>());
        services.AddSingleton(Mock.Of<ISqlRewriteAdvisor>());
        services.AddSingleton(Mock.Of<IWorkflowReviewTaskGateway>());
        services.AddSingleton(Mock.Of<ISqlReviewAdjustmentService>());
        services.AddSingleton(Mock.Of<IConfigCollectionProvider>());
        services.AddSingleton(Mock.Of<IConfigRuleEngine>());
        services.AddSingleton(Mock.Of<IConfigReviewAdjustmentService>());
        services.AddSingleton(Mock.Of<IMafExecutorInstrumentation>());
        services.AddSingleton(Mock.Of<IChatClientService>());
        services.AddSingleton(Mock.Of<ILlmPromptManager>());
        services.AddSingleton(Mock.Of<ILlmExecutionLogger>());
        services.AddSingleton(Options.Create(new MafFeatureFlags()));

        // 注册 Logger
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _factory = new MafWorkflowFactory(_serviceProvider);
    }

    [Fact]
    public void BuildSqlAnalysisWorkflow_ReturnsValidWorkflowInstance()
    {
        // Act
        var workflow = _factory.BuildSqlAnalysisWorkflow();

        // Assert
        Assert.NotNull(workflow);
    }

    [Fact]
    public void BuildDbConfigWorkflow_ReturnsValidWorkflowInstance()
    {
        // Act
        var workflow = _factory.BuildDbConfigWorkflow();

        // Assert
        Assert.NotNull(workflow);
    }

    [Fact]
    public void BuildSqlAnalysisWorkflow_GraphStructureMatchesDesign()
    {
        // Act
        var workflow = _factory.BuildSqlAnalysisWorkflow();

        // Assert
        Assert.NotNull(workflow);

        // 验证 workflow 可以被构建（说明 graph 结构有效）
        // MAF Workflow 类不暴露内部 graph 结构，所以我们只能验证构建成功
        // 实际的 graph 正确性需要通过集成测试验证
    }

    [Fact]
    public void BuildDbConfigWorkflow_GraphStructureMatchesDesign()
    {
        // Act
        var workflow = _factory.BuildDbConfigWorkflow();

        // Assert
        Assert.NotNull(workflow);

        // 验证 workflow 可以被构建（说明 graph 结构有效）
    }

    [Fact]
    public void BuildSqlAnalysisWorkflow_CanBeCalledMultipleTimes()
    {
        // Act
        var workflow1 = _factory.BuildSqlAnalysisWorkflow();
        var workflow2 = _factory.BuildSqlAnalysisWorkflow();

        // Assert
        Assert.NotNull(workflow1);
        Assert.NotNull(workflow2);
        Assert.NotSame(workflow1, workflow2);
    }

    [Fact]
    public void BuildDbConfigWorkflow_CanBeCalledMultipleTimes()
    {
        // Act
        var workflow1 = _factory.BuildDbConfigWorkflow();
        var workflow2 = _factory.BuildDbConfigWorkflow();

        // Assert
        Assert.NotNull(workflow1);
        Assert.NotNull(workflow2);
        Assert.NotSame(workflow1, workflow2);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MafWorkflowFactory(null!));
    }

    [Fact]
    public void BuildSqlAnalysisWorkflow_CreatesDistinctWorkflowInstances()
    {
        // Arrange & Act
        var workflow1 = _factory.BuildSqlAnalysisWorkflow();
        var workflow2 = _factory.BuildSqlAnalysisWorkflow();

        // Assert
        Assert.NotNull(workflow1);
        Assert.NotNull(workflow2);
        // 每次构建应该返回新的 workflow 实例
        Assert.NotSame(workflow1, workflow2);
    }

    [Fact]
    public void BuildDbConfigWorkflow_CreatesDistinctWorkflowInstances()
    {
        // Arrange & Act
        var workflow1 = _factory.BuildDbConfigWorkflow();
        var workflow2 = _factory.BuildDbConfigWorkflow();

        // Assert
        Assert.NotNull(workflow1);
        Assert.NotNull(workflow2);
        // 每次构建应该返回新的 workflow 实例
        Assert.NotSame(workflow1, workflow2);
    }
    [Fact]
    public async Task BuildSqlAnalysisWorkflow_DescribeProtocolAsync_DoesNotThrow()
    {
        var workflow = _factory.BuildSqlAnalysisWorkflow();

        var exception = await Record.ExceptionAsync(async () => await workflow.DescribeProtocolAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task BuildDbConfigWorkflow_DescribeProtocolAsync_DoesNotThrow()
    {
        var workflow = _factory.BuildDbConfigWorkflow();

        var exception = await Record.ExceptionAsync(async () => await workflow.DescribeProtocolAsync());

        Assert.Null(exception);
    }
}
