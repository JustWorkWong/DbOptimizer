namespace DbOptimizer.Infrastructure.DatabaseMigrations;

/* =========================
 * 迁移健康状态
 * 用于 /health 输出当前迁移是否完成
 * ========================= */
public sealed class MigrationReadinessState
{
    public bool IsReady { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void MarkReady()
    {
        IsReady = true;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        IsReady = false;
        ErrorMessage = errorMessage;
    }
}
