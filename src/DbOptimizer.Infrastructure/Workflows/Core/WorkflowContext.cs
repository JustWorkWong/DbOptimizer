using DbOptimizer.Core.Models;
using System.Text.Json;
using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * Workflow 运行时上下文
 * 设计目标：
 * 1) 统一承载 Workflow 的共享数据、状态与恢复点
 * 2) 与 Checkpoint 模型直接对齐，便于保存和恢复
 * 3) 通过 JsonElement 保持上下文可序列化，避免把运行时对象直接写入快照
 * ========================= */
public sealed class WorkflowContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, JsonElement> _data;
    private readonly HashSet<string> _completedExecutors;

    public WorkflowContext(
        Guid sessionId,
        string workflowType,
        CancellationToken cancellationToken = default,
        IDictionary<string, JsonElement>? data = null)
    {
        SessionId = sessionId;
        WorkflowType = workflowType;
        CancellationToken = cancellationToken;
        _data = data is null
            ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(data, StringComparer.OrdinalIgnoreCase);
        _completedExecutors = new HashSet<string>(StringComparer.Ordinal);
    }

    public Guid SessionId { get; }

    public string WorkflowType { get; }

    public CancellationToken CancellationToken { get; }

    public WorkflowCheckpointStatus Status { get; private set; } = WorkflowCheckpointStatus.Running;

    public string CurrentExecutor { get; private set; } = string.Empty;

    public int CheckpointVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastCheckpointAt { get; private set; }

    public IReadOnlyCollection<string> CompletedExecutors => _completedExecutors;

    public IReadOnlyDictionary<string, JsonElement> Data => _data;

    public void Set<T>(string key, T value)
    {
        _data[key] = JsonSerializer.SerializeToElement(value, SerializerOptions);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public T Get<T>(string key)
    {
        if (!TryGet<T>(key, out var value))
        {
            throw new KeyNotFoundException($"Workflow context key not found: {key}");
        }

        return value!;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_data.TryGetValue(key, out var jsonElement))
        {
            value = jsonElement.Deserialize<T>(SerializerOptions);
            return true;
        }

        value = default;
        return false;
    }

    public void SetCurrentExecutor(string executorName)
    {
        CurrentExecutor = executorName;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkExecutorCompleted(string executorName)
    {
        _completedExecutors.Add(executorName);
        CurrentExecutor = string.Empty;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyStatus(WorkflowCheckpointStatus status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public WorkflowCheckpoint CreateCheckpointSnapshot()
    {
        return new WorkflowCheckpoint
        {
            SessionId = SessionId,
            WorkflowType = WorkflowType,
            Status = Status,
            CurrentExecutor = CurrentExecutor,
            CheckpointVersion = CheckpointVersion <= 0 ? 1 : CheckpointVersion,
            Context = new Dictionary<string, JsonElement>(_data, StringComparer.OrdinalIgnoreCase),
            CompletedExecutors = _completedExecutors.ToArray(),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastCheckpointAt = LastCheckpointAt ?? UpdatedAt
        };
    }

    public int AdvanceCheckpointVersion()
    {
        CheckpointVersion = CheckpointVersion <= 0 ? 1 : CheckpointVersion + 1;
        LastCheckpointAt = DateTimeOffset.UtcNow;
        UpdatedAt = LastCheckpointAt.Value;
        return CheckpointVersion;
    }

    public static WorkflowContext FromCheckpoint(WorkflowCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        var context = new WorkflowContext(
            checkpoint.SessionId,
            checkpoint.WorkflowType,
            cancellationToken,
            checkpoint.Context);

        context.Status = checkpoint.Status;
        context.CurrentExecutor = checkpoint.CurrentExecutor;
        context.CheckpointVersion = checkpoint.CheckpointVersion;
        context.CreatedAt = checkpoint.CreatedAt;
        context.UpdatedAt = checkpoint.UpdatedAt;
        context.LastCheckpointAt = checkpoint.LastCheckpointAt;

        foreach (var completedExecutor in checkpoint.CompletedExecutors)
        {
            context._completedExecutors.Add(completedExecutor);
        }

        return context;
    }
}
