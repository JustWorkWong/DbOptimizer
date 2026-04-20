using DbOptimizer.Infrastructure.Workflows;
using Microsoft.Extensions.Logging;

namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class WorkflowExecutionConcurrencyGate(
    WorkflowExecutionOptions options,
    ILogger<WorkflowExecutionConcurrencyGate> logger) : IWorkflowExecutionConcurrencyGate
{
    private readonly object _sync = new();
    private int _totalActiveRuns;
    private int _sqlActiveRuns;
    private int _configActiveRuns;

    public WorkflowExecutionLease Acquire(string workflowType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);

        lock (_sync)
        {
            var activeForType = GetActiveCount(workflowType);
            var typeLimit = GetWorkflowTypeLimit(workflowType);

            if (_totalActiveRuns >= options.MaxConcurrentRuns || activeForType >= typeLimit)
            {
                logger.LogWarning(
                    "Workflow execution throttled. WorkflowType={WorkflowType}, TotalActiveRuns={TotalActiveRuns}, TotalLimit={TotalLimit}, WorkflowTypeActiveRuns={WorkflowTypeActiveRuns}, WorkflowTypeLimit={WorkflowTypeLimit}",
                    workflowType,
                    _totalActiveRuns,
                    options.MaxConcurrentRuns,
                    activeForType,
                    typeLimit);

                throw new WorkflowExecutionThrottledException(
                    workflowType,
                    options.MaxConcurrentRuns,
                    typeLimit,
                    _totalActiveRuns,
                    activeForType);
            }

            _totalActiveRuns++;
            IncrementWorkflowType(workflowType);

            logger.LogInformation(
                "Workflow execution slot acquired. WorkflowType={WorkflowType}, TotalActiveRuns={TotalActiveRuns}, WorkflowTypeActiveRuns={WorkflowTypeActiveRuns}",
                workflowType,
                _totalActiveRuns,
                GetActiveCount(workflowType));

            return new WorkflowExecutionLease(() => Release(workflowType));
        }
    }

    private void Release(string workflowType)
    {
        lock (_sync)
        {
            _totalActiveRuns = Math.Max(0, _totalActiveRuns - 1);
            DecrementWorkflowType(workflowType);

            logger.LogInformation(
                "Workflow execution slot released. WorkflowType={WorkflowType}, TotalActiveRuns={TotalActiveRuns}, WorkflowTypeActiveRuns={WorkflowTypeActiveRuns}",
                workflowType,
                _totalActiveRuns,
                GetActiveCount(workflowType));
        }
    }

    private int GetWorkflowTypeLimit(string workflowType)
    {
        return workflowType switch
        {
            "sql_analysis" => options.MaxConcurrentSqlRuns,
            "db_config_optimization" => options.MaxConcurrentConfigRuns,
            _ => options.MaxConcurrentRuns
        };
    }

    private int GetActiveCount(string workflowType)
    {
        return workflowType switch
        {
            "sql_analysis" => _sqlActiveRuns,
            "db_config_optimization" => _configActiveRuns,
            _ => _totalActiveRuns
        };
    }

    private void IncrementWorkflowType(string workflowType)
    {
        switch (workflowType)
        {
            case "sql_analysis":
                _sqlActiveRuns++;
                break;
            case "db_config_optimization":
                _configActiveRuns++;
                break;
        }
    }

    private void DecrementWorkflowType(string workflowType)
    {
        switch (workflowType)
        {
            case "sql_analysis":
                _sqlActiveRuns = Math.Max(0, _sqlActiveRuns - 1);
                break;
            case "db_config_optimization":
                _configActiveRuns = Math.Max(0, _configActiveRuns - 1);
                break;
        }
    }
}
