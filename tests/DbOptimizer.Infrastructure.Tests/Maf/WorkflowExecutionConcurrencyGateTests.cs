using DbOptimizer.Infrastructure.Maf.Runtime;
using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbOptimizer.Infrastructure.Tests.Maf;

public sealed class WorkflowExecutionConcurrencyGateTests
{
    [Fact]
    public void Acquire_WhenTotalLimitReached_ThrowsWorkflowExecutionThrottledException()
    {
        var gate = CreateGate(maxConcurrentRuns: 1, maxConcurrentSqlRuns: 1, maxConcurrentConfigRuns: 1);

        using var lease = gate.Acquire("sql_analysis");

        var exception = Assert.Throws<WorkflowExecutionThrottledException>(() => gate.Acquire("db_config_optimization"));

        Assert.Equal(1, exception.TotalLimit);
        Assert.Equal("db_config_optimization", exception.WorkflowType);
    }

    [Fact]
    public void Acquire_WhenWorkflowSpecificLimitReached_ThrowsWorkflowExecutionThrottledException()
    {
        var gate = CreateGate(maxConcurrentRuns: 3, maxConcurrentSqlRuns: 1, maxConcurrentConfigRuns: 2);

        using var lease = gate.Acquire("sql_analysis");

        var exception = Assert.Throws<WorkflowExecutionThrottledException>(() => gate.Acquire("sql_analysis"));

        Assert.Equal(1, exception.WorkflowTypeLimit);
        Assert.Equal("sql_analysis", exception.WorkflowType);
    }

    [Fact]
    public void Acquire_AfterRelease_AllowsNewExecution()
    {
        var gate = CreateGate(maxConcurrentRuns: 1, maxConcurrentSqlRuns: 1, maxConcurrentConfigRuns: 1);

        var lease = gate.Acquire("sql_analysis");
        lease.Dispose();

        using var nextLease = gate.Acquire("sql_analysis");

        Assert.NotNull(nextLease);
    }

    private static WorkflowExecutionConcurrencyGate CreateGate(
        int maxConcurrentRuns,
        int maxConcurrentSqlRuns,
        int maxConcurrentConfigRuns)
    {
        return new WorkflowExecutionConcurrencyGate(
            new WorkflowExecutionOptions
            {
                MaxConcurrentRuns = maxConcurrentRuns,
                MaxConcurrentSqlRuns = maxConcurrentSqlRuns,
                MaxConcurrentConfigRuns = maxConcurrentConfigRuns
            },
            NullLogger<WorkflowExecutionConcurrencyGate>.Instance);
    }
}
