# Human-in-the-Loop 审核机制

**面试重点**：展示对 HITL、request/response 模式、审核驳回回流的深度理解

---

## 一、核心问题

### Q1: 为什么需要 Human-in-the-loop？

**标准答案**：

AI 生成的优化建议不能直接应用到生产环境，原因：

1. **安全风险**：错误的索引可能导致性能下降
2. **业务约束**：AI 不了解业务规则（如：某些表不允许加索引）
3. **成本控制**：索引会占用存储空间，需要人工评估
4. **合规要求**：生产变更必须经过审批流程

**示例场景**：

```
AI 建议: CREATE INDEX idx_users_email ON users(email);

DBA 审核发现:
- users 表有 1 亿行数据
- 创建索引需要 2 小时，会锁表
- 应该在业务低峰期（凌晨 2-4 点）执行
- 需要先在从库验证，再应用到主库

→ 驳回，要求 AI 补充执行计划和风险评估
```

---

## 二、HITL 架构设计

### 整体流程

```
┌─────────────────────────────────────────────────────────────┐
│                    SQL Analysis Workflow                     │
└─────────────────────────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlCoordinatorMafExecutor              │
        │  生成 draft 结果                         │
        └─────────────────────────────────────────┘
                              ↓
        ┌─────────────────────────────────────────┐
        │  SqlHumanReviewGateExecutor             │
        │  1. 创建 review task                     │
        │  2. 生成 review request                  │
        │  3. 挂起 workflow                        │
        └─────────────────────────────────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
            ┌───────────────┐   ┌───────────────┐
            │  前端展示      │   │  DBA 审核     │
            │  draft 结果    │   │  评估风险     │
            └───────────────┘   └───────────────┘
                              ↓
                    ┌─────────┴─────────┐
                    ↓                   ↓
            ┌───────────────┐   ┌───────────────┐
            │   Approved    │   │   Rejected    │
            │   通过审核     │   │   驳回重做     │
            └───────────────┘   └───────────────┘
                    ↓                   ↓
            ┌───────────────┐   ┌───────────────┐
            │  Completed    │   │ Regeneration  │
            │  workflow 完成 │   │  回到 advisor │
            └───────────────┘   └───────────────┘
```

---

## 三、Review Gate Executor 实现

### 3.1 创建 Review Task

```csharp
public class SqlHumanReviewGateExecutor : IExecutor<SqlOptimizationDraftReadyMessage, ReviewDecisionResponseMessage>
{
    private readonly IWorkflowReviewTaskGateway _reviewGateway;
    private readonly IWorkflowStateStore _stateStore;
    private readonly ILogger<SqlHumanReviewGateExecutor> _logger;
    
    public async ValueTask<ReviewDecisionResponseMessage> HandleAsync(
        SqlOptimizationDraftReadyMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 1. 检查是否需要人工审核
        var options = context.Get<SqlAnalysisOptions>("options");
        if (!options.RequireHumanReview)
        {
            _logger.LogInformation(
                "跳过人工审核，SessionId: {SessionId}",
                state.SessionId);
            
            return new ReviewDecisionResponseMessage
            {
                Status = ReviewStatus.AutoApproved,
                Decision = "approved",
                Reason = "自动通过（配置为不需要审核）"
            };
        }
        
        // 2. 生成 review request ID（用于关联）
        var requestId = Guid.NewGuid().ToString();
        var runId = context.Get<string>("maf_run_id");
        var checkpointRef = context.Get<string>("maf_checkpoint_ref");
        
        // 3. 创建 review task
        var reviewTask = await _reviewGateway.CreateReviewTaskAsync(
            new CreateReviewTaskRequest
            {
                SessionId = state.SessionId,
                RequestId = requestId,
                EngineRunId = runId,
                EngineCheckpointRef = checkpointRef,
                WorkflowType = "SqlAnalysis",
                DraftResult = input.DraftResult,
                CreatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);
        
        _logger.LogInformation(
            "创建 review task，TaskId: {TaskId}, RequestId: {RequestId}",
            reviewTask.TaskId,
            requestId);
        
        // 4. 保存 pending request 到 context
        context.Set("pending_review_request_id", requestId);
        context.Set("pending_review_task_id", reviewTask.TaskId);
        
        // 5. 返回 request message（MAF 会自动挂起 workflow）
        return new ReviewDecisionResponseMessage
        {
            Status = ReviewStatus.Pending,
            RequestId = requestId,
            TaskId = reviewTask.TaskId
        };
    }
}
```

### 3.2 Review Task Gateway

```csharp
public interface IWorkflowReviewTaskGateway
{
    Task<ReviewTaskEntity> CreateReviewTaskAsync(
        CreateReviewTaskRequest request,
        CancellationToken cancellationToken);
    
    Task<ReviewTaskEntity?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken);
}

public class WorkflowReviewTaskGateway : IWorkflowReviewTaskGateway
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    private readonly IWorkflowResultSerializer _serializer;
    
    public async Task<ReviewTaskEntity> CreateReviewTaskAsync(
        CreateReviewTaskRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        var entity = new ReviewTaskEntity
        {
            TaskId = Guid.NewGuid(),
            SessionId = request.SessionId,
            RequestId = request.RequestId,  // 关键：用于恢复关联
            EngineRunId = request.EngineRunId,
            EngineCheckpointRef = request.EngineCheckpointRef,
            WorkflowType = request.WorkflowType,
            Status = "pending",
            Recommendations = JsonSerializer.Serialize(request.DraftResult),
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.CreatedAt
        };
        
        db.ReviewTasks.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        
        return entity;
    }
    
    public async Task<ReviewTaskEntity?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        
        return await db.ReviewTasks
            .FirstOrDefaultAsync(t => t.RequestId == requestId, cancellationToken);
    }
}
```

---

## 四、审核提交与 Workflow 恢复

### 4.1 Review API

```csharp
[ApiController]
[Route("api/reviews")]
public class ReviewApi : ControllerBase
{
    private readonly IWorkflowReviewTaskGateway _reviewGateway;
    private readonly IMafWorkflowRuntime _workflowRuntime;
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    
    [HttpPost("{taskId:guid}/submit")]
    public async Task<IActionResult> SubmitReviewAsync(
        Guid taskId,
        [FromBody] SubmitReviewRequest request,
        CancellationToken cancellationToken)
    {
        // 1. 查询 review task
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.ReviewTasks.FindAsync(new object[] { taskId }, cancellationToken);
        
        if (task == null)
        {
            return NotFound(new { error = "Review task not found" });
        }
        
        if (task.Status != "pending")
        {
            return BadRequest(new { error = "Review task already processed" });
        }
        
        // 2. 更新 review task 状态
        task.Status = request.Decision;  // "approved" / "rejected"
        task.ReviewerComments = request.Comments;
        task.ReviewedAt = DateTimeOffset.UtcNow;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        
        if (request.Decision == "rejected")
        {
            task.RejectionReason = request.RejectionReason;
            task.RequiredAdjustments = JsonSerializer.Serialize(request.RequiredAdjustments);
        }
        
        await db.SaveChangesAsync(cancellationToken);
        
        // 3. 构造 response message
        var responseMessage = new ReviewDecisionResponseMessage
        {
            Status = request.Decision == "approved" ? ReviewStatus.Approved : ReviewStatus.Rejected,
            RequestId = task.RequestId,
            TaskId = task.TaskId,
            Decision = request.Decision,
            Reason = request.Decision == "approved" ? request.Comments : request.RejectionReason,
            RequiredAdjustments = request.RequiredAdjustments
        };
        
        // 4. 恢复 workflow（发送 response message）
        var resumeResult = await _workflowRuntime.ResumeWithResponseAsync(
            task.SessionId,
            task.RequestId,
            responseMessage,
            cancellationToken);
        
        return Ok(new
        {
            taskId = task.TaskId,
            sessionId = task.SessionId,
            decision = request.Decision,
            workflowResumed = resumeResult.Success
        });
    }
}
```

### 4.2 Workflow Runtime Resume

```csharp
public class MafWorkflowRuntime : IMafWorkflowRuntime
{
    public async Task<WorkflowResumeResponse> ResumeWithResponseAsync(
        Guid sessionId,
        string requestId,
        ReviewDecisionResponseMessage responseMessage,
        CancellationToken cancellationToken)
    {
        // 1. 从数据库读取 checkpoint
        var checkpoint = await _stateStore.GetCheckpointAsync(sessionId, cancellationToken);
        if (checkpoint == null)
        {
            return WorkflowResumeResponse.NotFound(sessionId);
        }
        
        // 2. 验证 request ID 匹配
        var pendingRequest = checkpoint.PendingRequests
            .FirstOrDefault(r => r.RequestId == requestId);
        
        if (pendingRequest == null)
        {
            return WorkflowResumeResponse.InvalidRequest(sessionId, requestId);
        }
        
        // 3. 重建 workflow 实例
        var workflow = checkpoint.WorkflowType switch
        {
            "SqlAnalysis" => _factory.BuildSqlAnalysisWorkflow(),
            "DbConfigOptimization" => _factory.BuildDbConfigWorkflow(),
            _ => throw new InvalidOperationException($"Unknown workflow type: {checkpoint.WorkflowType}")
        };
        
        // 4. 恢复 workflow context
        var context = new WorkflowContext();
        context.Set("maf_run_id", checkpoint.RunId);
        context.Set("maf_checkpoint_ref", checkpoint.CheckpointRef);
        
        // 反序列化 shared state
        foreach (var kvp in checkpoint.SharedState.EnumerateObject())
        {
            context.Set(kvp.Name, kvp.Value);
        }
        
        // 5. 注入 response message
        context.Set($"response_{requestId}", responseMessage);
        
        // 6. 恢复执行
        var result = await workflow.ResumeAsync(context, cancellationToken);
        
        // 7. 保存新的 checkpoint
        await SaveCheckpointAsync(sessionId, context, cancellationToken);
        
        return WorkflowResumeResponse.Success(sessionId, result.Status);
    }
}
```

---

## 五、审核驳回回流机制

### 5.1 驳回后的处理逻辑

```csharp
public class SqlHumanReviewGateExecutor : IExecutor<SqlOptimizationDraftReadyMessage, ReviewDecisionResponseMessage>
{
    public async ValueTask<ReviewDecisionResponseMessage> HandleAsync(
        SqlOptimizationDraftReadyMessage input,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // ... 创建 review task ...
        
        // 等待 response（MAF 会自动挂起）
        var requestId = context.Get<string>("pending_review_request_id");
        var response = await context.WaitForResponseAsync<ReviewDecisionResponseMessage>(requestId, cancellationToken);
        
        // 处理审核结果
        if (response.Status == ReviewStatus.Approved)
        {
            _logger.LogInformation(
                "审核通过，SessionId: {SessionId}",
                context.Get<SqlAnalysisWorkflowState>("state").SessionId);
            
            // 标记为最终结果
            var state = context.Get<SqlAnalysisWorkflowState>("state");
            state.FinalResult = state.DraftResult;
            context.Set("state", state);
            
            return response;
        }
        else if (response.Status == ReviewStatus.Rejected)
        {
            _logger.LogWarning(
                "审核驳回，SessionId: {SessionId}, Reason: {Reason}",
                context.Get<SqlAnalysisWorkflowState>("state").SessionId,
                response.Reason);
            
            // 提取调整要求
            var adjustments = response.RequiredAdjustments;
            
            // 回流到 advisor executor
            return await RegenerateWithAdjustmentsAsync(
                context,
                adjustments,
                cancellationToken);
        }
        
        return response;
    }
    
    private async ValueTask<ReviewDecisionResponseMessage> RegenerateWithAdjustmentsAsync(
        IWorkflowContext context,
        Dictionary<string, string> adjustments,
        CancellationToken cancellationToken)
    {
        var state = context.Get<SqlAnalysisWorkflowState>("state");
        
        // 1. 构造调整后的 prompt
        var adjustmentPrompt = $"""
            之前的建议被驳回，原因：
            {string.Join("\n", adjustments.Select(kvp => $"- {kvp.Key}: {kvp.Value}"))}
            
            请根据以上反馈重新生成优化建议。
            """;
        
        context.Set("adjustment_prompt", adjustmentPrompt);
        context.Set("regeneration_count", context.Get<int>("regeneration_count") + 1);
        
        // 2. 触发重新生成（回到 IndexAdvisor 和 SqlRewrite）
        var regenerateMessage = new RegenerateOptimizationMessage
        {
            SessionId = state.SessionId,
            AdjustmentPrompt = adjustmentPrompt,
            PreviousDraft = state.DraftResult
        };
        
        // 3. 重新执行 advisor 和 coordinator
        // （MAF 会根据 workflow graph 自动路由）
        return new ReviewDecisionResponseMessage
        {
            Status = ReviewStatus.Regenerating,
            RequestId = Guid.NewGuid().ToString()
        };
    }
}
```

### 5.2 前端审核界面

```vue
<template>
  <div class="review-panel">
    <h2>SQL 优化建议审核</h2>
    
    <!-- 显示 draft 结果 -->
    <div class="draft-result">
      <h3>索引推荐</h3>
      <ul>
        <li v-for="idx in draftResult.indexRecommendations" :key="idx.indexName">
          <code>{{ idx.ddl }}</code>
          <span>预估收益: {{ idx.estimatedBenefit }}</span>
        </li>
      </ul>
      
      <h3>SQL 重写建议</h3>
      <ul>
        <li v-for="rewrite in draftResult.sqlRewriteSuggestions" :key="rewrite.id">
          <code>{{ rewrite.rewrittenSql }}</code>
          <span>原因: {{ rewrite.reason }}</span>
        </li>
      </ul>
    </div>
    
    <!-- 审核表单 -->
    <div class="review-form">
      <el-radio-group v-model="decision">
        <el-radio label="approved">通过</el-radio>
        <el-radio label="rejected">驳回</el-radio>
      </el-radio-group>
      
      <el-input
        v-if="decision === 'approved'"
        v-model="comments"
        type="textarea"
        placeholder="审核意见（可选）"
      />
      
      <div v-if="decision === 'rejected'">
        <el-input
          v-model="rejectionReason"
          type="textarea"
          placeholder="驳回原因（必填）"
        />
        
        <h4>需要调整的内容</h4>
        <el-form>
          <el-form-item label="索引建议">
            <el-input
              v-model="adjustments.indexRecommendations"
              type="textarea"
              placeholder="例如：不要在 user_id 上建索引，该字段区分度太低"
            />
          </el-form-item>
          
          <el-form-item label="SQL 重写">
            <el-input
              v-model="adjustments.sqlRewrite"
              type="textarea"
              placeholder="例如：不要使用子查询，改用 JOIN"
            />
          </el-form-item>
        </el-form>
      </div>
      
      <el-button type="primary" @click="submitReview">提交审核</el-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { submitReview as apiSubmitReview } from '@/api';

const props = defineProps<{
  taskId: string;
  draftResult: WorkflowResultEnvelope;
}>();

const decision = ref<'approved' | 'rejected'>('approved');
const comments = ref('');
const rejectionReason = ref('');
const adjustments = ref({
  indexRecommendations: '',
  sqlRewrite: ''
});

async function submitReview() {
  const payload = {
    decision: decision.value,
    comments: decision.value === 'approved' ? comments.value : undefined,
    rejectionReason: decision.value === 'rejected' ? rejectionReason.value : undefined,
    requiredAdjustments: decision.value === 'rejected' ? adjustments.value : undefined
  };
  
  await apiSubmitReview(props.taskId, payload);
  
  // 提交后跳转到 history 页面
  router.push(`/history/${props.taskId}`);
}
</script>
```

---

## 六、关键设计要点

### 6.1 Request/Response 关联

**问题**：进程重启后，如何知道哪个 review task 对应哪个 workflow？

**解决方案**：三重关联

```sql
-- review_tasks 表
CREATE TABLE review_tasks (
    task_id UUID PRIMARY KEY,
    session_id UUID NOT NULL,
    request_id VARCHAR(255) NOT NULL UNIQUE,  -- 关键：用于恢复关联
    engine_run_id VARCHAR(255) NOT NULL,
    engine_checkpoint_ref VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL,
    -- ...
);

-- 恢复流程
-- 1. 用户提交审核 → 根据 task_id 查询 review_tasks
-- 2. 读取 request_id、engine_run_id、engine_checkpoint_ref
-- 3. 根据 session_id 读取 checkpoint
-- 4. 验证 request_id 在 checkpoint.pendingRequests 中
-- 5. 恢复 workflow 并注入 response message
```

### 6.2 驳回回流的循环控制

**问题**：如果 DBA 连续驳回 10 次怎么办？

**解决方案**：限制重试次数

```csharp
public async ValueTask<ReviewDecisionResponseMessage> HandleAsync(
    SqlOptimizationDraftReadyMessage input,
    IWorkflowContext context,
    CancellationToken cancellationToken)
{
    var regenerationCount = context.Get<int>("regeneration_count");
    
    if (regenerationCount >= 3)
    {
        _logger.LogWarning(
            "达到最大重试次数，SessionId: {SessionId}",
            context.Get<SqlAnalysisWorkflowState>("state").SessionId);
        
        return new ReviewDecisionResponseMessage
        {
            Status = ReviewStatus.Failed,
            Reason = "已达到最大重试次数（3 次），请手动处理"
        };
    }
    
    // ... 正常审核流程 ...
}
```

### 6.3 审核超时处理

**问题**：如果 DBA 一直不审核怎么办？

**解决方案**：定时任务 + 自动过期

```csharp
public class ReviewTaskExpirationJob : IHostedService
{
    private readonly IDbContextFactory<DbOptimizerDbContext> _dbFactory;
    private readonly IMafWorkflowRuntime _workflowRuntime;
    private Timer? _timer;
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(CheckExpiredReviewTasks, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }
    
    private async void CheckExpiredReviewTasks(object? state)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        
        var expiredTasks = await db.ReviewTasks
            .Where(t => t.Status == "pending" && t.CreatedAt < DateTimeOffset.UtcNow.AddHours(-24))
            .ToListAsync();
        
        foreach (var task in expiredTasks)
        {
            // 自动标记为超时
            task.Status = "expired";
            task.UpdatedAt = DateTimeOffset.UtcNow;
            
            // 恢复 workflow 并标记为失败
            await _workflowRuntime.ResumeWithResponseAsync(
                task.SessionId,
                task.RequestId,
                new ReviewDecisionResponseMessage
                {
                    Status = ReviewStatus.Expired,
                    Reason = "审核超时（24 小时未处理）"
                },
                CancellationToken.None);
        }
        
        await db.SaveChangesAsync();
    }
    
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }
}
```

---

## 七、面试高频问题

### Q1: 如果进程重启，pending 的 review task 会丢失吗？

**答案**：不会。

- review task 持久化在 `review_tasks` 表
- checkpoint 持久化在 `workflow_sessions.engine_state`
- 通过 `request_id` 关联
- 进程重启后，用户提交审核时会触发恢复流程

### Q2: 如果 DBA 驳回后，AI 重新生成的结果还是不满意怎么办？

**答案**：支持多轮驳回。

- 每次驳回都会增加 `regeneration_count`
- 限制最大重试次数（如 3 次）
- 超过限制后标记为失败，需要人工介入

### Q3: 如果审核通过后，用户又想撤回怎么办？

**答案**：不支持撤回。

- 审核通过后 workflow 立即完成
- 如果需要修改，只能重新发起新的分析
- 原因：避免状态不一致

### Q4: 如何防止恶意用户频繁提交审核？

**答案**：限流 + 权限控制。

- API 层限流（每个用户每分钟最多 10 次）
- 审核权限控制（只有 DBA 角色可以审核）
- 审核日志记录（谁在什么时间审核了什么）

---

## 八、总结

### 核心设计原则

1. **持久化优先**：所有关键状态都持久化到数据库
2. **关联明确**：通过 `request_id` 建立 review task 与 workflow 的关联
3. **容错设计**：支持进程重启、审核超时、多轮驳回
4. **可观测性**：记录审核日志、驳回原因、调整要求

### 技术亮点

- ✅ MAF request/response 模式实现 HITL
- ✅ 三重关联保证恢复正确性
- ✅ 驳回回流支持多轮优化
- ✅ 审核超时自动处理
- ✅ 前后端状态同步

### 可扩展性

- 支持多种审核策略（自动审核、人工审核、混合审核）
- 支持多级审核（初审 + 终审）
- 支持审核委派（DBA A 委派给 DBA B）
- 支持审核模板（预定义审核规则）
