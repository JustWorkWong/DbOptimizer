# MAF 迁移完成方案

**创建日期**: 2026-04-17  
**目标**: 完成 MAF Workflow 引擎迁移，移除 Legacy engine，实现统一架构

---

## 1. 背景与目标

### 1.1 当前状态

- ✅ MAF 包已引入（`Microsoft.Agents.AI.Workflows 1.0.0-rc4`）
- ✅ MAF 接口已定义（Runtime/Factory/StateStore）
- ✅ MAF Executors 已创建（SQL 6个 + Config 5个）
- ✅ MAF Messages 已定义（完整消息契约）
- ❌ **MAF Runtime 核心方法未实现**（抛出 NotImplementedException）
- ❌ **MAF Factory 未连接到 graph builder**
- ❌ **Legacy engine 仍在使用**（WorkflowScheduler/QueryService）

### 1.2 目标状态

- ✅ MAF Runtime 完整实现
- ✅ MAF Factory 构建可执行 workflow graph
- ✅ Legacy engine 完全移除
- ✅ 所有 workflow 通过 MAF 执行
- ✅ 测试覆盖率达到 80%+

---

## 2. 技术方案

### 2.1 MAF Workflow Graph 构建

MAF 使用 **Graph-based orchestration**，需要将 executors 连接成有向无环图（DAG）。

#### SQL Analysis Workflow Graph

```
SqlAnalysisWorkflowCommand
    ↓
SqlInputValidationExecutor
    ↓
SqlParserMafExecutor
    ↓
ExecutionPlanMafExecutor
    ↓
[并行执行]
    ├─→ IndexAdvisorMafExecutor
    └─→ SqlRewriteMafExecutor
    ↓
SqlCoordinatorMafExecutor (生成 draft)
    ↓
[条件门控: RequireHumanReview]
    ├─→ true: SqlHumanReviewGateExecutor (挂起)
    │         ↓
    │   ReviewDecisionResponseMessage
    │         ↓
    │   SqlHumanReviewGateExecutor (恢复)
    └─→ false: 直接输出
    ↓
SqlOptimizationCompletedMessage
```

#### DB Config Workflow Graph

```
DbConfigWorkflowCommand
    ↓
DbConfigInputValidationExecutor
    ↓
ConfigCollectorMafExecutor
    ↓
ConfigAnalyzerMafExecutor
    ↓
ConfigCoordinatorMafExecutor (生成 draft)
    ↓
[条件门控: RequireHumanReview]
    ├─→ true: ConfigHumanReviewGateExecutor (挂起)
    │         ↓
    │   ReviewDecisionResponseMessage
    │         ↓
    │   ConfigHumanReviewGateExecutor (恢复)
    └─→ false: 直接输出
    ↓
DbConfigOptimizationCompletedMessage
```

### 2.2 MAF Runtime 实现要点

#### 2.2.1 Workflow 启动

```csharp
public async Task<WorkflowStartResponse> StartSqlAnalysisAsync(
    SqlAnalysisWorkflowCommand command,
    CancellationToken cancellationToken = default)
{
    // 1. 创建 workflow session
    var session = await CreateSessionAsync(command, cancellationToken);
    
    // 2. 从 factory 获取 workflow graph
    var workflow = _factory.BuildSqlAnalysisWorkflow();
    
    // 3. 创建 MAF execution context
    var context = new WorkflowExecutionContext
    {
        SessionId = session.Id,
        WorkflowType = "sql-analysis",
        InitialMessage = command
    };
    
    // 4. 启动 MAF workflow
    var runId = await workflow.StartAsync(context, cancellationToken);
    
    // 5. 保存 MAF run state
    await _stateStore.SaveAsync(new MafCheckpointEnvelope
    {
        SessionId = session.Id,
        RunId = runId,
        CheckpointRef = null // 初始无 checkpoint
    }, cancellationToken);
    
    // 6. 返回响应
    return new WorkflowStartResponse
    {
        SessionId = session.Id,
        Status = "running",
        EngineType = "maf",
        EngineRunId = runId
    };
}
```

#### 2.2.2 Workflow 恢复

```csharp
public async Task<WorkflowResumeResponse> ResumeAsync(
    Guid sessionId,
    CancellationToken cancellationToken = default)
{
    // 1. 从 state store 读取 checkpoint
    var checkpoint = await _stateStore.GetAsync(sessionId, cancellationToken);
    if (checkpoint == null)
        throw new InvalidOperationException($"No checkpoint found for session {sessionId}");
    
    // 2. 从 factory 获取 workflow graph
    var workflowType = await GetWorkflowTypeAsync(sessionId, cancellationToken);
    var workflow = workflowType switch
    {
        "sql-analysis" => _factory.BuildSqlAnalysisWorkflow(),
        "db-config-optimization" => _factory.BuildDbConfigWorkflow(),
        _ => throw new NotSupportedException($"Unknown workflow type: {workflowType}")
    };
    
    // 3. 恢复 MAF workflow
    await workflow.ResumeAsync(checkpoint.RunId, checkpoint.CheckpointRef, cancellationToken);
    
    // 4. 返回响应
    return new WorkflowResumeResponse
    {
        SessionId = sessionId,
        Status = "resumed"
    };
}
```

#### 2.2.3 Checkpoint 持久化

MAF 在以下时机自动触发 checkpoint：
- Executor 执行完成后
- Review gate 挂起前
- 错误发生时

```csharp
// MAF 内部会调用 ICheckpointStore.SaveAsync
public async Task SaveCheckpointAsync(
    string runId,
    string checkpointRef,
    byte[] checkpointData,
    CancellationToken cancellationToken = default)
{
    // 1. 查找对应的 session
    var sessionId = await GetSessionIdByRunIdAsync(runId, cancellationToken);
    
    // 2. 保存到 PostgreSQL
    await _dbContext.WorkflowSessions
        .Where(s => s.Id == sessionId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(x => x.EngineCheckpointRef, checkpointRef)
            .SetProperty(x => x.EngineState, checkpointData)
            .SetProperty(x => x.UpdatedAt, DateTime.UtcNow),
            cancellationToken);
    
    // 3. 同步到 Redis（可选，用于快速恢复）
    await _redis.StringSetAsync(
        $"checkpoint:{sessionId}",
        checkpointData,
        TimeSpan.FromHours(24));
}
```

### 2.3 MAF Factory 实现

```csharp
public class MafWorkflowFactory : IMafWorkflowFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public Workflow BuildSqlAnalysisWorkflow()
    {
        var builder = new WorkflowBuilder();
        
        // 1. 注册所有 executors
        builder.AddExecutor<SqlInputValidationExecutor>();
        builder.AddExecutor<SqlParserMafExecutor>();
        builder.AddExecutor<ExecutionPlanMafExecutor>();
        builder.AddExecutor<IndexAdvisorMafExecutor>();
        builder.AddExecutor<SqlRewriteMafExecutor>();
        builder.AddExecutor<SqlCoordinatorMafExecutor>();
        builder.AddExecutor<SqlHumanReviewGateExecutor>();
        
        // 2. 定义消息流
        builder
            .On<SqlAnalysisWorkflowCommand>()
            .Execute<SqlInputValidationExecutor>()
            .Then<SqlParserMafExecutor>()
            .Then<ExecutionPlanMafExecutor>()
            .Parallel(p => p
                .Branch<IndexAdvisorMafExecutor>()
                .Branch<SqlRewriteMafExecutor>())
            .Then<SqlCoordinatorMafExecutor>()
            .Gate<SqlHumanReviewGateExecutor>(
                condition: ctx => ctx.GetOption<bool>("RequireHumanReview"),
                onSuspend: ctx => ctx.EmitEvent("review.requested"))
            .Complete();
        
        // 3. 配置 checkpoint 策略
        builder.ConfigureCheckpoints(options =>
        {
            options.SaveAfterEachExecutor = true;
            options.SaveBeforeSuspend = true;
        });
        
        return builder.Build();
    }
    
    public Workflow BuildDbConfigWorkflow()
    {
        var builder = new WorkflowBuilder();
        
        builder.AddExecutor<DbConfigInputValidationExecutor>();
        builder.AddExecutor<ConfigCollectorMafExecutor>();
        builder.AddExecutor<ConfigAnalyzerMafExecutor>();
        builder.AddExecutor<ConfigCoordinatorMafExecutor>();
        builder.AddExecutor<ConfigHumanReviewGateExecutor>();
        
        builder
            .On<DbConfigWorkflowCommand>()
            .Execute<DbConfigInputValidationExecutor>()
            .Then<ConfigCollectorMafExecutor>()
            .Then<ConfigAnalyzerMafExecutor>()
            .Then<ConfigCoordinatorMafExecutor>()
            .Gate<ConfigHumanReviewGateExecutor>(
                condition: ctx => ctx.GetOption<bool>("RequireHumanReview"))
            .Complete();
        
        builder.ConfigureCheckpoints(options =>
        {
            options.SaveAfterEachExecutor = true;
            options.SaveBeforeSuspend = true;
        });
        
        return builder.Build();
    }
}
```

### 2.4 Review Response Bridge

Review 提交后需要恢复 workflow：

```csharp
// ReviewApi.cs
[HttpPost("review-tasks/{taskId}/submit")]
public async Task<IActionResult> SubmitReview(
    Guid taskId,
    [FromBody] SubmitReviewRequest request)
{
    // 1. 读取 review task
    var task = await _reviewTaskRepository.GetByIdAsync(taskId);
    
    // 2. 构建 response message
    var responseMessage = new ReviewDecisionResponseMessage
    {
        TaskId = taskId,
        Decision = request.Decision,
        ReviewerComments = request.Comments,
        Adjustments = request.Adjustments
    };
    
    // 3. 通过 MAF Runtime 恢复 workflow
    await _mafRuntime.ResumeWithMessageAsync(
        task.SessionId,
        responseMessage,
        cancellationToken);
    
    return Ok();
}
```

---

## 3. 实施任务清单

### Phase 1: MAF Runtime 核心实现（Week 1）

#### TASK-MAF-1: 实现 MafWorkflowRuntime 核心方法
- [ ] 实现 `StartSqlAnalysisAsync`
- [ ] 实现 `StartDbConfigOptimizationAsync`
- [ ] 实现 `ResumeAsync`
- [ ] 实现 `CancelAsync`
- [ ] 实现 session 创建和状态管理
- [ ] 集成 `IMafRunStateStore`
- [ ] 移除 `[Obsolete]` 属性

**验证**:
- [ ] 可启动 SQL workflow
- [ ] 可启动 Config workflow
- [ ] 可恢复挂起的 workflow
- [ ] 可取消运行中的 workflow

---

#### TASK-MAF-2: 实现 MafWorkflowFactory graph builder
- [ ] 实现 `BuildSqlAnalysisWorkflow` graph
- [ ] 实现 `BuildDbConfigWorkflow` graph
- [ ] 配置并行执行节点（Index + Rewrite）
- [ ] 配置条件门控（RequireHumanReview）
- [ ] 配置 checkpoint 策略

**验证**:
- [ ] Graph 可构建成功
- [ ] Executors 按顺序执行
- [ ] 并行节点正确执行
- [ ] 条件门控正确触发

---

#### TASK-MAF-3: 实现 MafRunStateStore checkpoint 持久化
- [ ] 实现 `SaveAsync` 到 PostgreSQL
- [ ] 实现 `GetAsync` 从 PostgreSQL
- [ ] 实现 `DeleteAsync` 清理
- [ ] 集成 Redis 缓存（可选）
- [ ] 实现 checkpoint 压缩（如果数据过大）

**验证**:
- [ ] Checkpoint 可保存到数据库
- [ ] Checkpoint 可从数据库恢复
- [ ] 进程重启后可恢复 workflow

---

### Phase 2: Review Bridge 实现（Week 1）

#### TASK-MAF-4: 实现 Review Response Bridge
- [ ] 修改 `ReviewApi.SubmitReview` 调用 MAF Runtime
- [ ] 实现 `ResumeWithMessageAsync` 方法
- [ ] 处理 Approve 场景
- [ ] 处理 Reject 场景
- [ ] 处理 Adjust 场景
- [ ] 更新 review task 状态

**验证**:
- [ ] Approve 后 workflow 继续执行
- [ ] Reject 后 workflow 标记为 failed
- [ ] Adjust 后应用修改并继续执行

---

### Phase 3: WorkflowApplicationService 切换到 MAF（Week 2）

#### TASK-MAF-5: 切换 WorkflowApplicationService 到 MAF Runtime
- [ ] 修改 `StartSqlAnalysisAsync` 调用 MAF Runtime
- [ ] 修改 `StartDbConfigOptimizationAsync` 调用 MAF Runtime
- [ ] 修改 `GetAsync` 从 MAF state 读取
- [ ] 修改 `ResumeAsync` 调用 MAF Runtime
- [ ] 修改 `CancelAsync` 调用 MAF Runtime
- [ ] 保持 API 契约不变

**验证**:
- [ ] API 端点行为不变
- [ ] 前端无需修改
- [ ] SSE 事件正常推送

---

#### TASK-MAF-6: 实现 MAF Event Adapter
- [ ] 监听 MAF 内部事件
- [ ] 转换为业务事件
- [ ] 推送到 SSE
- [ ] 更新 `workflow_sessions` 状态
- [ ] 调用 `WorkflowProjectionWriter`

**验证**:
- [ ] SSE 可接收到 workflow 进度事件
- [ ] `workflow_sessions` 状态实时更新
- [ ] History 查询返回正确状态

---

### Phase 4: 移除 Legacy Engine（Week 2）

#### TASK-MAF-7: 移除 Legacy Workflow Scheduler
- [ ] 删除 `IWorkflowScheduler` 接口
- [ ] 删除 `WorkflowScheduler` 实现
- [ ] 删除 `IWorkflowQueryService` 接口
- [ ] 删除 `WorkflowQueryService` 实现
- [ ] 删除 Legacy DTO（`LegacyWorkflowStartResponse` 等）
- [ ] 清理 `Program.cs` 中的 Legacy 注册

**验证**:
- [ ] 构建通过
- [ ] 无 Legacy 代码残留
- [ ] 所有测试通过

---

### Phase 5: 测试覆盖（Week 3）

#### TASK-MAF-8: 单元测试
- [ ] `MafWorkflowRuntime` 单元测试（80%+ 覆盖）
- [ ] `MafWorkflowFactory` 单元测试
- [ ] `MafRunStateStore` 单元测试
- [ ] 所有 Executors 单元测试
- [ ] Review bridge 单元测试

**目标**: 单元测试覆盖率 80%+

---

#### TASK-MAF-9: 集成测试
- [ ] SQL workflow 端到端测试
- [ ] Config workflow 端到端测试
- [ ] Review approve 流程测试
- [ ] Review reject 流程测试
- [ ] Checkpoint 恢复测试
- [ ] 并发执行测试

**目标**: 关键路径 100% 覆盖

---

#### TASK-MAF-10: E2E 测试
- [ ] 前端提交 SQL 分析
- [ ] 前端查看实时进度
- [ ] 前端提交审核
- [ ] 前端查看历史记录
- [ ] 慢查询自动触发分析

**目标**: 核心用户流程 100% 覆盖

---

### Phase 6: 文档与部署（Week 3）

#### TASK-MAF-11: 更新文档
- [ ] 更新 `ARCHITECTURE.md` 移除 Legacy 引用
- [ ] 更新 `MAF_WORKFLOW_ARCHITECTURE.md` 补充实现细节
- [ ] 更新 `SYSTEM_SCOPE_AND_STATUS.md` 标记 MAF 为"已完成"
- [ ] 创建 `MAF_TROUBLESHOOTING.md` 故障排查指南
- [ ] 更新 `README.md` 部署说明

---

#### TASK-MAF-12: 性能测试与优化
- [ ] 测试 1000 条慢查询处理时间
- [ ] 测试 SSE 并发连接数（目标 100+）
- [ ] 测试 checkpoint 恢复时间（目标 <1s）
- [ ] 优化 PostgreSQL 查询性能
- [ ] 优化 Redis 缓存策略

---

#### TASK-MAF-13: 生产部署准备
- [ ] 配置生产环境 MAF Runtime options
- [ ] 配置只读数据库连接字符串（MCP fallback）
- [ ] 配置 checkpoint 清理策略
- [ ] 配置监控告警（Token 成本、失败率）
- [ ] 准备回滚方案

---

## 4. 风险与缓解

### 4.1 技术风险

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|---------|
| MAF 1.0.0-rc4 API 不稳定 | HIGH | MEDIUM | 锁定版本，准备降级方案 |
| Checkpoint 数据过大 | MEDIUM | LOW | 实现压缩，限制上下文大小 |
| 并发执行死锁 | HIGH | LOW | 充分测试，添加超时保护 |
| Review 恢复失败 | HIGH | MEDIUM | 添加重试机制，记录详细日志 |

### 4.2 进度风险

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|---------|
| MAF API 学习曲线陡峭 | MEDIUM | HIGH | 提前阅读官方文档，寻求社区支持 |
| 测试编写耗时超预期 | MEDIUM | MEDIUM | 优先核心路径，P2 测试可延后 |
| Legacy 代码耦合难以移除 | LOW | LOW | 逐步解耦，保留接口兼容层 |

---

## 5. 验收标准

### 5.1 功能验收

- [ ] 所有 workflow 通过 MAF 执行
- [ ] Legacy engine 完全移除
- [ ] Review 流程完整可用
- [ ] Checkpoint 恢复成功率 100%
- [ ] SSE 事件实时推送

### 5.2 质量验收

- [ ] 单元测试覆盖率 ≥ 80%
- [ ] 集成测试覆盖关键路径 100%
- [ ] E2E 测试覆盖核心用户流程 100%
- [ ] 无 CRITICAL/HIGH 安全问题
- [ ] 代码审查通过

### 5.3 性能验收

- [ ] 1000 条慢查询处理时间 < 10 分钟
- [ ] SSE 并发连接数 ≥ 100
- [ ] Checkpoint 恢复时间 < 1 秒
- [ ] API 响应时间 P95 < 500ms

---

## 6. 时间估算

| Phase | 任务数 | 预计工时 | 日历时间 |
|-------|--------|---------|---------|
| Phase 1: MAF Runtime 核心 | 3 | 40h | 5 天 |
| Phase 2: Review Bridge | 1 | 8h | 1 天 |
| Phase 3: 切换到 MAF | 2 | 16h | 2 天 |
| Phase 4: 移除 Legacy | 1 | 8h | 1 天 |
| Phase 5: 测试覆盖 | 3 | 40h | 5 天 |
| Phase 6: 文档与部署 | 3 | 16h | 2 天 |
| **总计** | **13** | **128h** | **16 天** |

**建议排期**: 3 周（含 buffer）

---

## 7. 下一步行动

1. **立即开始**: TASK-MAF-1（实现 MafWorkflowRuntime）
2. **并行准备**: 阅读 MAF 官方文档，理解 graph builder API
3. **风险预案**: 如果 MAF API 不符合预期，准备降级到 Legacy + 标记技术债

---

## 8. 参考资料

- [MAF Workflow Architecture](../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md)
- [Workflow Context and Checkpoints](../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md)
- [SQL Analysis Workflow Design](../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md)
- [DB Config Workflow Design](../03-design/workflow/DB_CONFIG_WORKFLOW_DESIGN.md)
- Microsoft Agents Framework 官方文档（待补充链接）

---

**文档版本**: v1.0  
**最后更新**: 2026-04-17  
**负责人**: Architecture Team
