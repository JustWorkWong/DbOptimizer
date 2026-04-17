using Microsoft.EntityFrameworkCore;

namespace DbOptimizer.Infrastructure.Persistence;

/* =========================
 * DbOptimizer EF Core 上下文
 * 设计目标：
 * 1) 统一承载 M1-01 核心表结构
 * 2) 通过 EF Core Migration 管理结构演进
 * 3) 保持与现有 snake_case 表结构一致
 * ========================= */
public sealed class DbOptimizerDbContext(DbContextOptions<DbOptimizerDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowSessionEntity> WorkflowSessions => Set<WorkflowSessionEntity>();

    public DbSet<AgentExecutionEntity> AgentExecutions => Set<AgentExecutionEntity>();

    public DbSet<ToolCallEntity> ToolCalls => Set<ToolCallEntity>();

    public DbSet<AgentMessageEntity> AgentMessages => Set<AgentMessageEntity>();

    public DbSet<DecisionRecordEntity> DecisionRecords => Set<DecisionRecordEntity>();

    public DbSet<ReviewTaskEntity> ReviewTasks => Set<ReviewTaskEntity>();

    public DbSet<PromptVersionEntity> PromptVersions => Set<PromptVersionEntity>();

    public DbSet<ErrorLogEntity> ErrorLogs => Set<ErrorLogEntity>();

    public DbSet<SlowQueryEntity> SlowQueries => Set<SlowQueryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pgcrypto");

        ConfigureWorkflowSessions(modelBuilder);
        ConfigureAgentExecutions(modelBuilder);
        ConfigureToolCalls(modelBuilder);
        ConfigureAgentMessages(modelBuilder);
        ConfigureDecisionRecords(modelBuilder);
        ConfigureReviewTasks(modelBuilder);
        ConfigurePromptVersions(modelBuilder);
        ConfigureErrorLogs(modelBuilder);
        ConfigureSlowQueries(modelBuilder);
    }

    private static void ConfigureWorkflowSessions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkflowSessionEntity>();
        entity.ToTable("workflow_sessions");
        entity.HasKey(x => x.SessionId);

        entity.Property(x => x.SessionId)
            .HasColumnName("session_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.WorkflowType).HasColumnName("workflow_type").HasMaxLength(50);
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        entity.Property(x => x.State).HasColumnName("state").HasColumnType("jsonb").HasDefaultValue("{}");
        entity.Property(x => x.EngineType).HasColumnName("engine_type").HasMaxLength(50).HasDefaultValue("maf");
        entity.Property(x => x.EngineRunId).HasColumnName("engine_run_id").HasMaxLength(100);
        entity.Property(x => x.EngineCheckpointRef).HasColumnName("engine_checkpoint_ref").HasMaxLength(200);
        entity.Property(x => x.EngineState).HasColumnName("engine_state").HasColumnType("jsonb");
        entity.Property(x => x.ResultType).HasColumnName("result_type").HasMaxLength(100);
        entity.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(50).HasDefaultValue("manual");
        entity.Property(x => x.SourceRefId).HasColumnName("source_ref_id");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");

        entity.HasIndex(x => x.Status).HasDatabaseName("idx_workflow_sessions_status");
        entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_workflow_sessions_created_at_desc").IsDescending(true);
        entity.HasIndex(x => x.WorkflowType).HasDatabaseName("idx_workflow_sessions_workflow_type");
        entity.HasIndex(x => x.ResultType).HasDatabaseName("idx_workflow_sessions_result_type");
    }

    private static void ConfigureAgentExecutions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AgentExecutionEntity>();
        entity.ToTable("agent_executions");
        entity.HasKey(x => x.ExecutionId);

        entity.Property(x => x.ExecutionId)
            .HasColumnName("execution_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.SessionId).HasColumnName("session_id");
        entity.Property(x => x.AgentName).HasColumnName("agent_name").HasMaxLength(100);
        entity.Property(x => x.ExecutorName).HasColumnName("executor_name").HasMaxLength(100);
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        entity.Property(x => x.InputData).HasColumnName("input_data").HasColumnType("jsonb");
        entity.Property(x => x.OutputData).HasColumnName("output_data").HasColumnType("jsonb");
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.TokenUsage).HasColumnName("token_usage").HasColumnType("jsonb");

        entity.HasIndex(x => x.SessionId).HasDatabaseName("idx_agent_executions_session_id");
        entity.HasIndex(x => x.AgentName).HasDatabaseName("idx_agent_executions_agent_name");
        entity.HasIndex(x => x.StartedAt).HasDatabaseName("idx_agent_executions_started_at_desc").IsDescending(true);

        entity.HasOne(x => x.Session)
            .WithMany(x => x.AgentExecutions)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureToolCalls(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ToolCallEntity>();
        entity.ToTable("tool_calls");
        entity.HasKey(x => x.CallId);

        entity.Property(x => x.CallId)
            .HasColumnName("call_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.ExecutionId).HasColumnName("execution_id");
        entity.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(100);
        entity.Property(x => x.Arguments).HasColumnName("arguments").HasColumnType("jsonb");
        entity.Property(x => x.Result).HasColumnName("result").HasColumnType("jsonb");
        entity.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");

        entity.HasIndex(x => x.ExecutionId).HasDatabaseName("idx_tool_calls_execution_id");
        entity.HasIndex(x => x.ToolName).HasDatabaseName("idx_tool_calls_tool_name");
        entity.HasIndex(x => x.StartedAt).HasDatabaseName("idx_tool_calls_started_at_desc").IsDescending(true);

        entity.HasOne(x => x.Execution)
            .WithMany(x => x.ToolCalls)
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureAgentMessages(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AgentMessageEntity>();
        entity.ToTable("agent_messages");
        entity.HasKey(x => x.MessageId);

        entity.Property(x => x.MessageId)
            .HasColumnName("message_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.ExecutionId).HasColumnName("execution_id");
        entity.Property(x => x.Role).HasColumnName("role").HasMaxLength(20);
        entity.Property(x => x.Content).HasColumnName("content");
        entity.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        entity.HasIndex(x => x.ExecutionId).HasDatabaseName("idx_agent_messages_execution_id");
        entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_agent_messages_created_at_desc").IsDescending(true);

        entity.HasOne(x => x.Execution)
            .WithMany(x => x.AgentMessages)
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureDecisionRecords(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DecisionRecordEntity>();
        entity.ToTable("decision_records", builder =>
            builder.HasCheckConstraint(
                "ck_decision_records_confidence_range",
                "confidence >= 0 AND confidence <= 100"));
        entity.HasKey(x => x.DecisionId);

        entity.Property(x => x.DecisionId)
            .HasColumnName("decision_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.ExecutionId).HasColumnName("execution_id");
        entity.Property(x => x.DecisionType).HasColumnName("decision_type").HasMaxLength(50);
        entity.Property(x => x.Reasoning).HasColumnName("reasoning");
        entity.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 2);
        entity.Property(x => x.Evidence).HasColumnName("evidence").HasColumnType("jsonb");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        entity.HasIndex(x => x.ExecutionId).HasDatabaseName("idx_decision_records_execution_id");
        entity.HasIndex(x => x.DecisionType).HasDatabaseName("idx_decision_records_decision_type");

        entity.HasOne(x => x.Execution)
            .WithMany(x => x.DecisionRecords)
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureReviewTasks(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReviewTaskEntity>();
        entity.ToTable("review_tasks");
        entity.HasKey(x => x.TaskId);

        entity.Property(x => x.TaskId)
            .HasColumnName("task_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.SessionId).HasColumnName("session_id");
        entity.Property(x => x.TaskType).HasColumnName("task_type").HasMaxLength(50);
        entity.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(100);
        entity.Property(x => x.EngineRunId).HasColumnName("engine_run_id").HasMaxLength(100);
        entity.Property(x => x.EngineCheckpointRef).HasColumnName("engine_checkpoint_ref").HasMaxLength(200);
        entity.Property(x => x.Recommendations).HasColumnName("recommendations").HasColumnType("jsonb");
        entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        entity.Property(x => x.ReviewerComment).HasColumnName("reviewer_comment");
        entity.Property(x => x.Adjustments).HasColumnName("adjustments").HasColumnType("jsonb");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");

        entity.HasIndex(x => x.SessionId).HasDatabaseName("idx_review_tasks_session_id");
        entity.HasIndex(x => x.Status).HasDatabaseName("idx_review_tasks_status");
        entity.HasIndex(x => x.TaskType).HasDatabaseName("idx_review_tasks_task_type");
        entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_review_tasks_created_at_desc").IsDescending(true);
        entity.HasIndex(x => x.RequestId).HasDatabaseName("idx_review_tasks_request_id");

        entity.HasOne(x => x.Session)
            .WithMany(x => x.ReviewTasks)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePromptVersions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PromptVersionEntity>();
        entity.ToTable("prompt_versions");
        entity.HasKey(x => x.VersionId);

        entity.Property(x => x.VersionId)
            .HasColumnName("version_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.AgentName).HasColumnName("agent_name").HasMaxLength(100);
        entity.Property(x => x.VersionNumber).HasColumnName("version_number");
        entity.Property(x => x.PromptTemplate).HasColumnName("prompt_template");
        entity.Property(x => x.Variables).HasColumnName("variables").HasColumnType("jsonb");
        entity.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false);
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(100);

        entity.HasIndex(x => new { x.AgentName, x.VersionNumber }).IsUnique().HasDatabaseName("uq_prompt_versions_agent_version");
        entity.HasIndex(x => new { x.AgentName, x.IsActive }).HasDatabaseName("idx_prompt_versions_agent_name_active");
    }

    private static void ConfigureErrorLogs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ErrorLogEntity>();
        entity.ToTable("error_logs");
        entity.HasKey(x => x.LogId);

        entity.Property(x => x.LogId)
            .HasColumnName("log_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.SessionId).HasColumnName("session_id");
        entity.Property(x => x.ExecutionId).HasColumnName("execution_id");
        entity.Property(x => x.ErrorType).HasColumnName("error_type").HasMaxLength(50);
        entity.Property(x => x.ErrorMessage).HasColumnName("error_message");
        entity.Property(x => x.StackTrace).HasColumnName("stack_trace");
        entity.Property(x => x.Context).HasColumnName("context").HasColumnType("jsonb");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        entity.HasIndex(x => x.SessionId).HasDatabaseName("idx_error_logs_session_id");
        entity.HasIndex(x => x.ExecutionId).HasDatabaseName("idx_error_logs_execution_id");
        entity.HasIndex(x => x.ErrorType).HasDatabaseName("idx_error_logs_error_type");
        entity.HasIndex(x => x.CreatedAt).HasDatabaseName("idx_error_logs_created_at_desc").IsDescending(true);

        entity.HasOne(x => x.Session)
            .WithMany(x => x.ErrorLogs)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(x => x.Execution)
            .WithMany(x => x.ErrorLogs)
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureSlowQueries(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SlowQueryEntity>();
        entity.ToTable("slow_queries");
        entity.HasKey(x => x.QueryId);

        entity.Property(x => x.QueryId)
            .HasColumnName("query_id")
            .HasDefaultValueSql("gen_random_uuid()");
        entity.Property(x => x.DatabaseId).HasColumnName("database_id").HasMaxLength(100);
        entity.Property(x => x.DatabaseType).HasColumnName("database_type").HasMaxLength(50);
        entity.Property(x => x.SqlFingerprint).HasColumnName("sql_fingerprint");
        entity.Property(x => x.QueryHash).HasColumnName("query_hash").HasMaxLength(64);
        entity.Property(x => x.OriginalSql).HasColumnName("original_sql");
        entity.Property(x => x.QueryType).HasColumnName("query_type").HasMaxLength(20);
        entity.Property(x => x.Tables).HasColumnName("tables").HasMaxLength(500);
        entity.Property(x => x.AvgExecutionTime).HasColumnName("avg_execution_time");
        entity.Property(x => x.MaxExecutionTime).HasColumnName("max_execution_time");
        entity.Property(x => x.ExecutionCount).HasColumnName("execution_count");
        entity.Property(x => x.TotalRowsExamined).HasColumnName("total_rows_examined");
        entity.Property(x => x.TotalRowsSent).HasColumnName("total_rows_sent");
        entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
        entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        entity.Property(x => x.LatestAnalysisSessionId).HasColumnName("latest_analysis_session_id");

        entity.HasIndex(x => new { x.QueryHash, x.DatabaseId }).HasDatabaseName("idx_slow_queries_hash_db");
        entity.HasIndex(x => x.LastSeenAt).HasDatabaseName("idx_slow_queries_last_seen_desc").IsDescending(true);
        entity.HasIndex(x => x.DatabaseId).HasDatabaseName("idx_slow_queries_database_id");
        entity.HasIndex(x => x.AvgExecutionTime).HasDatabaseName("idx_slow_queries_avg_time_desc").IsDescending(true);
        entity.HasIndex(x => x.LatestAnalysisSessionId).HasDatabaseName("idx_slow_queries_latest_analysis_session_id");
    }
}

public sealed class WorkflowSessionEntity
{
    public Guid SessionId { get; set; }

    public string WorkflowType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string State { get; set; } = "{}";

    public string EngineType { get; set; } = "maf";

    public string? EngineRunId { get; set; }

    public string? EngineCheckpointRef { get; set; }

    public string? EngineState { get; set; }

    public string? ResultType { get; set; }

    public string SourceType { get; set; } = "manual";

    public Guid? SourceRefId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<AgentExecutionEntity> AgentExecutions { get; set; } = new List<AgentExecutionEntity>();

    public ICollection<ReviewTaskEntity> ReviewTasks { get; set; } = new List<ReviewTaskEntity>();

    public ICollection<ErrorLogEntity> ErrorLogs { get; set; } = new List<ErrorLogEntity>();
}

public sealed class AgentExecutionEntity
{
    public Guid ExecutionId { get; set; }

    public Guid SessionId { get; set; }

    public string AgentName { get; set; } = string.Empty;

    public string ExecutorName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? InputData { get; set; }

    public string? OutputData { get; set; }

    public string? ErrorMessage { get; set; }

    public string? TokenUsage { get; set; }

    public WorkflowSessionEntity Session { get; set; } = null!;

    public ICollection<ToolCallEntity> ToolCalls { get; set; } = new List<ToolCallEntity>();

    public ICollection<AgentMessageEntity> AgentMessages { get; set; } = new List<AgentMessageEntity>();

    public ICollection<DecisionRecordEntity> DecisionRecords { get; set; } = new List<DecisionRecordEntity>();

    public ICollection<ErrorLogEntity> ErrorLogs { get; set; } = new List<ErrorLogEntity>();
}

public sealed class ToolCallEntity
{
    public Guid CallId { get; set; }

    public Guid ExecutionId { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public string Arguments { get; set; } = "{}";

    public string? Result { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public AgentExecutionEntity Execution { get; set; } = null!;
}

public sealed class AgentMessageEntity
{
    public Guid MessageId { get; set; }

    public Guid ExecutionId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? Metadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public AgentExecutionEntity Execution { get; set; } = null!;
}

public sealed class DecisionRecordEntity
{
    public Guid DecisionId { get; set; }

    public Guid ExecutionId { get; set; }

    public string DecisionType { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public decimal Confidence { get; set; }

    public string Evidence { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public AgentExecutionEntity Execution { get; set; } = null!;
}

public sealed class ReviewTaskEntity
{
    public Guid TaskId { get; set; }

    public Guid SessionId { get; set; }

    public string TaskType { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    public string? EngineRunId { get; set; }

    public string? EngineCheckpointRef { get; set; }

    public string Recommendations { get; set; } = "{}";

    public string Status { get; set; } = string.Empty;

    public string? ReviewerComment { get; set; }

    public string? Adjustments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public WorkflowSessionEntity Session { get; set; } = null!;
}

public sealed class PromptVersionEntity
{
    public Guid VersionId { get; set; }

    public string AgentName { get; set; } = string.Empty;

    public int VersionNumber { get; set; }

    public string PromptTemplate { get; set; } = string.Empty;

    public string? Variables { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }
}

public sealed class ErrorLogEntity
{
    public Guid LogId { get; set; }

    public Guid? SessionId { get; set; }

    public Guid? ExecutionId { get; set; }

    public string ErrorType { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    public string? Context { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public WorkflowSessionEntity? Session { get; set; }

    public AgentExecutionEntity? Execution { get; set; }
}

public sealed class SlowQueryEntity
{
    public Guid QueryId { get; set; }
    public string DatabaseId { get; set; } = string.Empty;
    public string DatabaseType { get; set; } = string.Empty;
    public string SqlFingerprint { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string OriginalSql { get; set; } = string.Empty;
    public string QueryType { get; set; } = string.Empty;
    public string Tables { get; set; } = string.Empty;
    public TimeSpan AvgExecutionTime { get; set; }
    public TimeSpan MaxExecutionTime { get; set; }
    public int ExecutionCount { get; set; }
    public long TotalRowsExamined { get; set; }
    public long TotalRowsSent { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LatestAnalysisSessionId { get; set; }
}
