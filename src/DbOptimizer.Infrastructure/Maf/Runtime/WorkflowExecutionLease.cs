namespace DbOptimizer.Infrastructure.Maf.Runtime;

public sealed class WorkflowExecutionLease(Action releaseAction) : IDisposable
{
    private Action? _releaseAction = releaseAction;

    public void Dispose()
    {
        Interlocked.Exchange(ref _releaseAction, null)?.Invoke();
    }
}
