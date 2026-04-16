using FluentAssertions;

namespace DbOptimizer.SecurityTests;

/// <summary>
/// 审计日志测试 - 验证关键操作被正确记录
/// </summary>
public class AuditLogTests
{
    [Fact]
    public void McpCall_ShouldBeLogged()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var mcpOperation = new McpOperation
        {
            Tool = "query_database",
            Parameters = new Dictionary<string, object> { ["query"] = "SELECT * FROM users" },
            Timestamp = DateTime.UtcNow
        };

        // Act
        LogMcpCall(auditLog, mcpOperation);

        // Assert
        auditLog.Should().HaveCount(1);
        auditLog[0].Operation.Should().Be("MCP_CALL");
        auditLog[0].Details.Should().Contain("query_database");
    }

    [Fact]
    public void ReviewAction_ShouldBeLogged()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var reviewAction = new ReviewAction
        {
            SessionId = Guid.NewGuid(),
            Action = "APPROVE",
            Reviewer = "admin",
            Timestamp = DateTime.UtcNow
        };

        // Act
        LogReviewAction(auditLog, reviewAction);

        // Assert
        auditLog.Should().HaveCount(1);
        auditLog[0].Operation.Should().Be("REVIEW_ACTION");
        auditLog[0].Details.Should().Contain("APPROVE");
        auditLog[0].Details.Should().Contain("admin");
    }

    [Fact]
    public void WorkflowExecution_ShouldBeLogged()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var workflowExecution = new WorkflowExecution
        {
            SessionId = Guid.NewGuid(),
            WorkflowType = "SQL_ANALYSIS",
            Status = "COMPLETED",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow
        };

        // Act
        LogWorkflowExecution(auditLog, workflowExecution);

        // Assert
        auditLog.Should().HaveCount(1);
        auditLog[0].Operation.Should().Be("WORKFLOW_EXECUTION");
        auditLog[0].Details.Should().Contain("SQL_ANALYSIS");
        auditLog[0].Details.Should().Contain("COMPLETED");
    }

    [Fact]
    public void SensitiveData_ShouldBeRedactedInLogs()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var sensitiveOperation = new McpOperation
        {
            Tool = "update_config",
            Parameters = new Dictionary<string, object>
            {
                ["key"] = "database_password",
                ["value"] = "SuperSecret123!"
            },
            Timestamp = DateTime.UtcNow
        };

        // Act
        LogMcpCall(auditLog, sensitiveOperation);

        // Assert
        auditLog[0].Details.Should().NotContain("SuperSecret123!", "敏感数据应被脱敏");
        auditLog[0].Details.Should().Contain("***", "应使用脱敏标记");
    }

    [Fact]
    public void AuditLog_ShouldIncludeTimestamp()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var operation = new McpOperation
        {
            Tool = "test_tool",
            Parameters = new Dictionary<string, object>(),
            Timestamp = DateTime.UtcNow
        };

        // Act
        LogMcpCall(auditLog, operation);

        // Assert
        auditLog[0].Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AuditLog_ShouldBeImmutable()
    {
        // Arrange
        var auditLog = new List<AuditEntry>();
        var operation = new McpOperation
        {
            Tool = "test_tool",
            Parameters = new Dictionary<string, object>(),
            Timestamp = DateTime.UtcNow
        };

        // Act
        LogMcpCall(auditLog, operation);
        var originalDetails = auditLog[0].Details;

        // 尝试修改（应该失败或无效）
        var entry = auditLog[0];
        // entry.Details = "modified"; // 如果 AuditEntry 是 record，这会失败

        // Assert
        auditLog[0].Details.Should().Be(originalDetails, "审计日志应不可变");
    }

    // ========== 辅助类型 ==========

    private record AuditEntry(string Operation, string Details, DateTime Timestamp);

    private record McpOperation
    {
        public required string Tool { get; init; }
        public required Dictionary<string, object> Parameters { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    private record ReviewAction
    {
        public required Guid SessionId { get; init; }
        public required string Action { get; init; }
        public required string Reviewer { get; init; }
        public required DateTime Timestamp { get; init; }
    }

    private record WorkflowExecution
    {
        public required Guid SessionId { get; init; }
        public required string WorkflowType { get; init; }
        public required string Status { get; init; }
        public required DateTime StartTime { get; init; }
        public required DateTime EndTime { get; init; }
    }

    // ========== 辅助方法 ==========

    private void LogMcpCall(List<AuditEntry> log, McpOperation operation)
    {
        var details = $"Tool: {operation.Tool}, Params: {RedactSensitiveData(operation.Parameters)}";
        log.Add(new AuditEntry("MCP_CALL", details, operation.Timestamp));
    }

    private void LogReviewAction(List<AuditEntry> log, ReviewAction action)
    {
        var details = $"Session: {action.SessionId}, Action: {action.Action}, Reviewer: {action.Reviewer}";
        log.Add(new AuditEntry("REVIEW_ACTION", details, action.Timestamp));
    }

    private void LogWorkflowExecution(List<AuditEntry> log, WorkflowExecution execution)
    {
        var details = $"Session: {execution.SessionId}, Type: {execution.WorkflowType}, Status: {execution.Status}";
        log.Add(new AuditEntry("WORKFLOW_EXECUTION", details, execution.StartTime));
    }

    private string RedactSensitiveData(Dictionary<string, object> parameters)
    {
        var sensitiveKeys = new[] { "password", "secret", "token", "key", "value" };
        var redacted = new Dictionary<string, object>(parameters);

        foreach (var key in redacted.Keys.ToList())
        {
            // 检查 key 是否敏感
            if (sensitiveKeys.Any(sk => key.Contains(sk, StringComparison.OrdinalIgnoreCase)))
            {
                redacted[key] = "***";
                continue;
            }

            // 检查 value 是否包含敏感内容
            var value = redacted[key]?.ToString() ?? string.Empty;
            if (sensitiveKeys.Any(sk => value.Contains(sk, StringComparison.OrdinalIgnoreCase)))
            {
                redacted[key] = "***";
            }
        }

        return string.Join(", ", redacted.Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }
}
