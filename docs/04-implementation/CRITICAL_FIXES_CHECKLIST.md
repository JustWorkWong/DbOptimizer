# Critical Fixes Checklist

**创建日期**: 2026-04-18  
**优先级**: P0（阻塞生产发布）  
**预计工期**: 3-5 天

---

## 排除项（不需要处理）

- ❌ E2E 测试失败 - 后续手动测试
- ❌ 慢查询自动触发 - 未来功能，当前仅支持手动 SQL

---

## CRITICAL 问题（必须立即修复）

### TASK-FIX-1: 实现 MAF Workflow 真正执行逻辑

**优先级**: P0  
**预计时间**: 8 小时  
**依赖**: 无

#### 问题描述

当前 `MafWorkflowRuntime` 的启动方法只是创建 session 并返回 "running" 状态，但没有真正执行 workflow。

**问题文件**:
- `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
  - 行 131: `StartSqlAnalysisAsync` 中的 TODO
  - 行 240: `StartDbConfigOptimizationAsync` 中的 TODO
  - 行 352: `ResumeSqlWorkflowAsync` 中的 TODO
  - 行 454: `ResumeConfigWorkflowAsync` 中的 TODO

**当前代码**:
```csharp
// TODO: 实际的 MAF workflow 执行需要通过 WorkflowHost 或类似机制
// 当前版本先返回 running 状态，实际执行逻辑待 MAF API 确认后补充
await Task.CompletedTask;
return true;
```

#### 修复步骤

1. **研究 MAF 1.0.0-rc4 Workflow 执行 API**（2小时）
   - [ ] 阅读 MAF 官方文档
   - [ ] 查看 `Microsoft.Agents.AI.Workflows` 包的 API
   - [ ] 确认如何启动和执行 Workflow 实例
   - [ ] 确认如何传递初始消息（SqlAnalysisWorkflowCommand）

2. **实现 SQL Workflow 执行逻辑**（3小时）
   - [ ] 在 `ExecuteSqlWorkflowAsync` 中实现真正的 workflow 启动
   - [ ] 从 `MafWorkflowFactory.BuildSqlAnalysisWorkflow()` 获取 workflow 实例
   - [ ] 调用 workflow 的 Start/Execute 方法
   - [ ] 传递 `SqlAnalysisWorkflowCommand` 作为初始消息
   - [ ] 处理 workflow 执行过程中的事件（ExecutorStarted, ExecutorCompleted）
   - [ ] 在 workflow 完成时更新 session 状态

3. **实现 Config Workflow 执行逻辑**（2小时）
   - [ ] 复用 SQL workflow 的执行模式
   - [ ] 实现 `ExecuteConfigWorkflowAsync`
   - [ ] 传递 `DbConfigWorkflowCommand` 作为初始消息

4. **实现 Resume 逻辑**（1小时）
   - [ ] 在 `ResumeSqlWorkflowAsync` 中实现真正的恢复
   - [ ] 从 checkpoint 加载 workflow 状态
   - [ ] 传递 `ReviewDecisionResponseMessage` 继续执行
   - [ ] 同样实现 `ResumeConfigWorkflowAsync`

#### 验证标准

- [ ] SQL workflow 启动后真正执行所有 executors
- [ ] Config workflow 启动后真正执行所有 executors
- [ ] Workflow 完成后 session 状态更新为 "completed"
- [ ] Review 批准后 workflow 能继续执行
- [ ] 手动测试：提交 SQL → 看到索引建议 → 审核通过 → 看到最终结果

#### 参考文件

- `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowFactory.cs` - 查看如何构建 workflow
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/*.cs` - 查看 executor 实现
- `docs/02-architecture/MAF_WORKFLOW_ARCHITECTURE.md` - 架构设计文档

---

### TASK-FIX-2: 修复 Task.Run 异常处理

**优先级**: P0  
**预计时间**: 2 小时  
**依赖**: 无

#### 问题描述

使用 `_ = Task.Run(async () => { ... })` 进行 fire-and-forget 异步执行，如果 Task.Run 本身抛出异常，会导致未观察到的异常，可能在 GC 时触发 UnobservedTaskException 事件，导致进程崩溃。

**问题文件**:
- `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
  - 行 120: `StartSqlAnalysisAsync`
  - 行 229: `StartDbConfigOptimizationAsync`
  - 行 343: `ResumeSqlWorkflowAsync`
  - 行 446: `ResumeConfigWorkflowAsync`
  - 行 546: `CancelAsync`

**当前代码**:
```csharp
_ = Task.Run(async () =>
{
    try
    {
        var success = await ExecuteSqlWorkflowAsync(session.Id, command, cancellationToken);
        // ...
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "SQL workflow execution failed");
        // ...
    }
}, cancellationToken);
```

#### 修复步骤

1. **添加 Task.Run 异常捕获**（1小时）
   - [ ] 为所有 `Task.Run` 添加 `.ContinueWith()` 处理
   - [ ] 捕获 `IsFaulted` 状态
   - [ ] 记录 CRITICAL 级别日志
   - [ ] 确保异常不会被忽略

2. **测试异常场景**（1小时）
   - [ ] 模拟 TaskScheduler 异常
   - [ ] 验证异常被正确记录
   - [ ] 验证进程不会崩溃

#### 修复代码模板

```csharp
Task.Run(async () =>
{
    try
    {
        var success = await ExecuteSqlWorkflowAsync(session.Id, command, cancellationToken);
        if (!success)
        {
            await UpdateSessionStatusAsync(session.Id, "failed", cancellationToken);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "SQL workflow execution failed for session {SessionId}", session.Id);
        await UpdateSessionStatusAsync(session.Id, "failed", cancellationToken);
    }
}, CancellationToken.None) // 使用 None 避免取消导致异常
.ContinueWith(task =>
{
    if (task.IsFaulted)
    {
        _logger.LogCritical(task.Exception, 
            "Unhandled exception in background workflow task for session {SessionId}", 
            session.Id);
    }
}, TaskScheduler.Default);
```

#### 验证标准

- [ ] 所有 `Task.Run` 都有 `.ContinueWith()` 处理
- [ ] 异常被记录为 CRITICAL 级别
- [ ] 手动测试：触发异常，验证日志记录，验证进程不崩溃

---

### TASK-FIX-3: 确认密码不会泄露到日志

**优先级**: P0  
**预计时间**: 2 小时  
**依赖**: 无

#### 问题描述

`AppHost.cs` 中使用环境变量传递数据库密码，虽然使用了 `secret: true` 参数，但需要确认 Aspire Dashboard 和容器日志不会泄露密码。

**问题文件**:
- `src/DbOptimizer.AppHost/AppHost.cs`
  - 行 34: `POSTGRES_PASSWORD`
  - 行 46: `MYSQL_ROOT_PASSWORD`

**当前代码**:
```csharp
.WithEnvironment("POSTGRES_PASSWORD", postgresPasswordValue)
.WithEnvironment("MYSQL_ROOT_PASSWORD", mySqlPasswordValue)
```

#### 修复步骤

1. **验证 Aspire secret 机制**（1小时）
   - [ ] 启动 Aspire Dashboard
   - [ ] 检查环境变量是否被掩码显示
   - [ ] 检查容器日志是否包含明文密码
   - [ ] 查看 Aspire 文档确认 `secret: true` 的行为

2. **添加日志过滤规则**（1小时）
   - [ ] 在 `Program.cs` 中配置日志过滤
   - [ ] 过滤包含 "PASSWORD" 的日志行
   - [ ] 添加单元测试验证过滤规则

#### 修复代码（如果需要）

```csharp
// 在 Program.cs 中添加日志过滤
builder.Logging.AddFilter((category, level) =>
{
    // 过滤包含敏感信息的日志
    return !category.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase);
});

// 或者使用自定义日志处理器
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "masked";
});
```

#### 验证标准

- [ ] Aspire Dashboard 不显示明文密码
- [ ] 容器日志不包含明文密码
- [ ] 应用日志不包含明文密码
- [ ] 文档中记录密码管理最佳实践

---

## HIGH 问题（合并前修复）

### TASK-FIX-4: 拆分超大文件

**优先级**: P1  
**预计时间**: 4 小时  
**依赖**: 无

#### 问题描述

以下文件超过 CLAUDE.md 规定的 800 行限制：

1. `LightweightSqlParser.cs` - 934 行（重复出现在两个位置）
2. `WorkflowExecutionAuditService.cs` - 783 行
3. `DashboardAndHistoryApi.cs` - 765 行
4. `MafWorkflowRuntime.cs` - 711 行

#### 修复步骤

**4.1 删除重复的 LightweightSqlParser**（30分钟）

- [ ] 删除 `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Domain/SqlAnalysis/LightweightSqlParser.cs`
- [ ] 统一使用 `src/DbOptimizer.Core/Models/LightweightSqlParser.cs`
- [ ] 更新所有引用

**4.2 拆分 DashboardAndHistoryApi.cs**（1小时）

- [ ] 创建 `DashboardApi.cs`（包含 stats, trends, alerts）
- [ ] 创建 `HistoryApi.cs`（包含 history, replay）
- [ ] 删除原文件
- [ ] 更新 `Program.cs` 注册

**4.3 拆分 MafWorkflowRuntime.cs**（1.5小时）

- [ ] 创建 `MafSqlWorkflowStarter.cs`（SQL workflow 启动逻辑）
- [ ] 创建 `MafConfigWorkflowStarter.cs`（Config workflow 启动逻辑）
- [ ] `MafWorkflowRuntime` 委托给 Starter 类
- [ ] 保持接口不变

**4.4 拆分 WorkflowExecutionAuditService.cs**（1小时）

- [ ] 创建 `WorkflowAuditQueryService.cs`（查询逻辑）
- [ ] `WorkflowExecutionAuditService` 仅保留写入逻辑
- [ ] 更新 DI 注册

#### 验证标准

- [ ] 所有文件 < 800 行
- [ ] 构建通过
- [ ] 测试通过
- [ ] 功能不受影响

---

### TASK-FIX-5: 完善错误持久化

**优先级**: P1  
**预计时间**: 3 小时  
**依赖**: 无

#### 问题描述

`MafGlobalErrorHandler.RecordExecutorErrorAsync` 只记录到日志，没有持久化到数据库，无法追溯和分析。

**问题文件**:
- `src/DbOptimizer.Infrastructure/Maf/Runtime/ErrorHandling/MafGlobalErrorHandler.cs`
  - 行 196-225

#### 修复步骤

1. **创建 executor_errors 表**（1小时）
   - [ ] 创建 `ExecutorErrorEntity.cs`
   - [ ] 字段：Id, SessionId, ExecutorName, ErrorMessage, StackTrace, ErrorCategory, RetryCount, CreatedAt
   - [ ] 在 `DbOptimizerDbContext` 中添加 `DbSet<ExecutorErrorEntity>`
   - [ ] 生成 EF Core migration

2. **实现错误持久化**（1小时）
   - [ ] 修改 `RecordExecutorErrorAsync` 保存到数据库
   - [ ] 添加错误查询接口 `IExecutorErrorRepository`
   - [ ] 实现 `ExecutorErrorRepository`

3. **添加错误统计 API**（1小时）
   - [ ] 创建 `GET /api/errors/stats` 端点
   - [ ] 返回错误统计（按类型、按 executor）
   - [ ] 创建 `GET /api/errors/{sessionId}` 端点
   - [ ] 返回指定 session 的所有错误

#### 验证标准

- [ ] 错误保存到 `executor_errors` 表
- [ ] 可通过 API 查询错误
- [ ] 错误统计正确
- [ ] 构建和测试通过

---

### TASK-FIX-6: 添加 API 输入验证

**优先级**: P1  
**预计时间**: 3 小时  
**依赖**: 无

#### 问题描述

`WorkflowApi.cs` 缺少输入验证，可能导致无效请求进入系统。

**问题文件**:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`
  - 行 27-46: `HandleCreateSqlAnalysisAsync`
  - 行 48-67: `HandleCreateDbConfigOptimizationAsync`

#### 修复步骤

1. **安装 FluentValidation**（15分钟）
   - [ ] 添加 `FluentValidation.AspNetCore` NuGet 包
   - [ ] 在 `Program.cs` 中注册

2. **创建验证器**（1.5小时）
   - [ ] 创建 `CreateSqlAnalysisWorkflowRequestValidator.cs`
     - SqlText 不能为空
     - SqlText 长度 < 100KB
     - DatabaseType 必须是 "mysql" 或 "postgresql"
   - [ ] 创建 `CreateDbConfigOptimizationWorkflowRequestValidator.cs`
     - DatabaseId 不能为空
     - DatabaseId 必须是有效的 GUID
   - [ ] 注册验证器到 DI

3. **添加验证中间件**（1小时）
   - [ ] 在 API 端点中调用验证器
   - [ ] 返回 400 Bad Request 和详细错误信息
   - [ ] 添加单元测试验证各种无效输入

4. **添加业务验证**（30分钟）
   - [ ] 在 `WorkflowApplicationService` 中验证 SessionId 不重复
   - [ ] 验证 DatabaseId 存在（如果需要）

#### 验证标准

- [ ] 空 SqlText 返回 400
- [ ] 过长 SqlText 返回 400
- [ ] 无效 DatabaseType 返回 400
- [ ] 错误信息清晰明确
- [ ] 单元测试覆盖所有验证规则

---

### TASK-FIX-7: 移除重复代码

**优先级**: P1  
**预计时间**: 30 分钟  
**依赖**: TASK-FIX-4.1

#### 问题描述

`LightweightSqlParser.cs` 完全相同的代码出现在两个位置。

#### 修复步骤

已包含在 TASK-FIX-4.1 中。

---

### TASK-FIX-8: 添加并发控制

**优先级**: P1  
**预计时间**: 2 小时  
**依赖**: 无

#### 问题描述

`ReviewApi.SubmitReviewAsync` 没有检查并发提交，可能导致同一个 review task 被多次提交。

**问题文件**:
- `src/DbOptimizer.API/Api/ReviewApi.cs`
  - 行 65-80

#### 修复步骤

1. **添加乐观锁**（1小时）
   - [ ] 在 `ReviewTaskEntity` 添加 `RowVersion` 字段（byte[]）
   - [ ] 配置为 `[Timestamp]` 或 `IsRowVersion()`
   - [ ] 生成 EF Core migration

2. **处理并发冲突**（1小时）
   - [ ] 在 `ReviewApplicationService.SubmitAsync` 中捕获 `DbUpdateConcurrencyException`
   - [ ] 返回 409 Conflict 错误
   - [ ] 添加单元测试模拟并发提交

#### 验证标准

- [ ] 并发提交返回 409 Conflict
- [ ] 第一个提交成功，第二个失败
- [ ] 错误信息清晰
- [ ] 单元测试通过

---

## MEDIUM 问题（可选，建议修复）

### TASK-FIX-9: 添加资源清理

**优先级**: P2  
**预计时间**: 1 小时  
**依赖**: 无

#### 问题描述

`MafWorkflowRuntime` 没有实现 `IDisposable`，无法清理 `CircuitBreaker` 和 `RetryPolicy` 资源。

#### 修复步骤

- [ ] 实现 `IDisposable` 接口
- [ ] 在 `Dispose()` 中清理 CircuitBreaker
- [ ] 在 `Program.cs` 中注册为 Scoped 或 Transient

---

### TASK-FIX-10: 调整日志级别

**优先级**: P2  
**预计时间**: 1 小时  
**依赖**: 无

#### 问题描述

某些关键决策点使用 `LogInformation`，应使用 `LogWarning` 或 `LogError`。

#### 修复步骤

- [ ] 审查所有 `LogInformation` 调用
- [ ] 错误场景改为 `LogError`
- [ ] 警告场景改为 `LogWarning`
- [ ] 正常流程保持 `LogInformation`

---

### TASK-FIX-11: 提取魔法数字到配置

**优先级**: P2  
**预计时间**: 1 小时  
**依赖**: 无

#### 问题描述

`MafWorkflowRuntime` 构造函数中有多个硬编码数字。

#### 修复步骤

- [ ] 创建 `MafWorkflowRuntimeOptions` 配置类
- [ ] 移动所有魔法数字到配置
- [ ] 在 `appsettings.json` 中配置默认值
- [ ] 在 `Program.cs` 中绑定配置

---

## 执行顺序建议

### Day 1（8小时）
1. TASK-FIX-1: 实现 MAF Workflow 执行逻辑（8小时）

### Day 2（8小时）
2. TASK-FIX-2: 修复 Task.Run 异常处理（2小时）
3. TASK-FIX-3: 确认密码不泄露（2小时）
4. TASK-FIX-4: 拆分超大文件（4小时）

### Day 3（8小时）
5. TASK-FIX-5: 完善错误持久化（3小时）
6. TASK-FIX-6: 添加 API 输入验证（3小时）
7. TASK-FIX-8: 添加并发控制（2小时）

### Day 4（可选，4小时）
8. TASK-FIX-9: 添加资源清理（1小时）
9. TASK-FIX-10: 调整日志级别（1小时）
10. TASK-FIX-11: 提取魔法数字（1小时）
11. 手动测试和验证（1小时）

---

## 验收标准

### 功能验收
- [ ] 提交 SQL 分析请求，能看到索引建议和 SQL 重写
- [ ] 提交配置优化请求，能看到配置建议
- [ ] 审核批准后，workflow 能继续执行到完成
- [ ] 审核拒绝后，workflow 标记为失败
- [ ] 取消 workflow 能正确停止

### 质量验收
- [ ] 所有 CRITICAL 问题已修复
- [ ] 所有 HIGH 问题已修复
- [ ] 构建通过（0 错误）
- [ ] 核心单元测试通过
- [ ] 代码符合 CLAUDE.md 规范

### 安全验收
- [ ] 密码不会泄露到日志
- [ ] API 输入已验证
- [ ] 并发冲突已处理
- [ ] 异常不会导致进程崩溃

---

## 提交规范

每个 TASK 完成后独立提交：

```bash
# TASK-FIX-1
git commit -m "feat: implement MAF workflow execution logic

- Add real workflow execution in MafWorkflowRuntime
- Execute SQL and Config workflows using MAF API
- Handle workflow events and update session status
- Support resume from checkpoint

Fixes: CRITICAL issue #1"

# TASK-FIX-2
git commit -m "fix: add exception handling for Task.Run

- Add ContinueWith to catch unhandled exceptions
- Log CRITICAL level errors
- Prevent process crash from background tasks

Fixes: CRITICAL issue #2"

# 其他类似...
```

---

## 完成后检查清单

- [ ] 所有 TASK-FIX-1 至 FIX-8 已完成
- [ ] 所有代码已提交并推送
- [ ] 手动测试通过
- [ ] 文档已更新（如果需要）
- [ ] CLAUDE.md 变更日志已更新
- [ ] 准备好发布到生产环境
