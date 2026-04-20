namespace DbOptimizer.Infrastructure.Maf.Runtime;

public interface IWorkflowExecutionConcurrencyGate
{
    WorkflowExecutionLease Acquire(string workflowType);
}
