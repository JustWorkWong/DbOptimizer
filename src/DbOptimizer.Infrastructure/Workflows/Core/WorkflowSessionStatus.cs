namespace DbOptimizer.Infrastructure.Workflows;

public static class WorkflowSessionStatus
{
    public const string Running = "Running";
    public const string WaitingForReview = "WaitingForReview";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsWaitingForReview(string? status)
    {
        return string.Equals(status, WaitingForReview, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "WaitingReview", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "suspended", StringComparison.OrdinalIgnoreCase);
    }
}
