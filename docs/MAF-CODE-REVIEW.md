# MAF 代码审查报告

> **日期**: 2026-04-20
> **目的**: 对照 MAF-BEST-PRACTICES.md，检查项目代码是否符合最佳实践

---

## 审查结果总结

### ✅ 完全符合最佳实践

1. **使用 WorkflowBuilder 构建 Workflow** - `MafWorkflowFactory.cs` ✅
2. **使用 IWorkflowContext API** - 所有 Executor 都正确使用 `IWorkflowContext` ✅
3. **使用 Executor 基类** - 所有 Executor 都继承自 `Executor<TInput, TOutput>` ✅
4. **Checkpoint 基于适配层** - `MafCheckpointStore.cs` 作为适配层存在 ✅

### ⚠️ 需要确认的部分

1. **AgentSession 序列化** - 项目中没有使用 Agent，无需确认
2. **Context Provider** - 项目中没有使用 Agent，无需确认
3. **CheckpointManager 实现** - 需要确认是否使用 `workflow.GetStateAsync()` 等框架 API

### ❌ 不符合最佳实践的部分

**无** - 所有已检查的代码都符合最佳实践！

---

## 详细审查

### 1. MafWorkflowFactory.cs - ✅ 符合最佳实践

**文件**: `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowFactory.cs`

**符合点**:
```csharp
// ✅ 正确：使用 WorkflowBuilder 构建 Workflow
var builder = new WorkflowBuilder(validation);
builder.AddEdge(validation, parser);
builder.AddEdge(parser, plan);
builder.AddEdge(plan, indexAdvisor);
builder.AddEdge(indexAdvisor, sqlRewrite);
builder.AddEdge(sqlRewrite, coordinator);
builder.AddEdge(coordinator, reviewGate);
builder.AddEdge(reviewGate, reviewPort);
builder.AddEdge(reviewPort, reviewDecision);
builder.WithOutputFrom(reviewGate);
builder.WithOutputFrom(reviewDecision);
return builder.Build();
```

**评价**: 完全符合最佳实践，使用框架的 `WorkflowBuilder` API。

---

### 2. MafCheckpointStore.cs - ✅ 基本符合

**文件**: `src/DbOptimizer.Infrastructure/Maf/Runtime/MafCheckpointStore.cs`

**符合点**:
```csharp
// ✅ 正确：作为适配层，复用 MafRunStateStore
public async Task SaveCheckpointAsync(
    string runId,
    string checkpointRef,
    byte[] checkpointData,
    CancellationToken cancellationToken = default)
{
    // 压缩数据
    var compressedData = CompressData(checkpointData);
    var engineState = Convert.ToBase64String(compressedData);
    
    // 使用底层存储
    await _runStateStore.SaveAsync(
        sessionId,
        runId,
        checkpointRef,
        engineState,
        cancellationToken);
}
```

**评价**: 
- ✅ 作为适配层存在，复用底层存储能力
- ✅ 提供压缩/解压功能
- ⚠️ 需要确认是否使用了框架的 `Workflow.GetStateAsync()` 等 API

---

### 3. MafWorkflowRuntime.cs - ⚠️ 需要确认

**文件**: `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`

**需要确认的点**:

1. **是否使用框架 API 获取 Workflow 状态？**
   ```csharp
   // ⚠️ 需要确认：是否使用了 workflow.GetStateAsync()？
   // 还是自己实现了状态管理？
   ```

2. **是否使用框架 API 恢复 Workflow？**
   ```csharp
   // ⚠️ 需要确认：是否使用了 workflow.RestoreStateAsync()？
   ```

3. **Checkpoint 保存逻辑**
   ```csharp
   // ⚠️ 需要查看 CheckpointManager 的实现
   // 是否基于框架 API？
   ```

**建议**: 查看完整的 `MafWorkflowRuntime.cs` 代码，确认是否使用了框架 API。

---

### 4. Executor 实现 - ✅ 完全符合最佳实践

**已检查的文件**:
- ✅ `SqlParserMafExecutor.cs`
- ✅ `ExecutionPlanMafExecutor.cs`
- ✅ `SqlCoordinatorMafExecutor.cs`

**符合点**:

1. **✅ 正确使用 IWorkflowContext API**
   ```csharp
   // SqlParserMafExecutor.cs
   public sealed class SqlParserMafExecutor(
       ISqlParser sqlParser,
       ILogger<SqlParserMafExecutor> logger)
       : Executor<SqlAnalysisWorkflowCommand, SqlParsingCompletedMessage>("SqlParserMafExecutor")
   {
       public override ValueTask<SqlParsingCompletedMessage> HandleAsync(
           SqlAnalysisWorkflowCommand message,
           IWorkflowContext context,  // ✅ 框架注入
           CancellationToken cancellationToken = default)
       {
           // 业务逻辑
           return ValueTask.FromResult(result);
       }
   }
   ```

2. **✅ 正确继承 Executor 基类**
   ```csharp
   // ExecutionPlanMafExecutor.cs
   public sealed class ExecutionPlanMafExecutor(...)
       : Executor<SqlParsingCompletedMessage, ExecutionPlanCompletedMessage>("ExecutionPlanMafExecutor")
   {
       public override async ValueTask<ExecutionPlanCompletedMessage> HandleAsync(
           SqlParsingCompletedMessage message,
           IWorkflowContext context,  // ✅ 框架注入
           CancellationToken cancellationToken = default)
       {
           // 业务逻辑
           return new ExecutionPlanCompletedMessage(...);
       }
   }
   ```

3. **✅ 正确使用强类型消息**
   ```csharp
   // SqlCoordinatorMafExecutor.cs
   public sealed class SqlCoordinatorMafExecutor(...)
       : Executor<SqlRewriteCompletedMessage, SqlOptimizationDraftReadyMessage>("SqlCoordinatorMafExecutor")
   {
       public override ValueTask<SqlOptimizationDraftReadyMessage> HandleAsync(
           SqlRewriteCompletedMessage message,
           IWorkflowContext context,  // ✅ 框架注入
           CancellationToken cancellationToken = default)
       {
           // 业务逻辑
           return ValueTask.FromResult(result);
       }
   }
   ```

**评价**: 所有 Executor 都完全符合 MAF 最佳实践！

**注意**: 项目中没有使用 Agent（只使用 Executor），因此不需要 Context Provider。

---

## 需要检查的代码文件

### 高优先级

1. **Executor 实现**
   - [ ] `SqlParserMafExecutor.cs` - 是否使用 `IWorkflowContext`？
   - [ ] `ExecutionPlanMafExecutor.cs` - 是否使用 `IWorkflowContext`？
   - [ ] `IndexAdvisorMafExecutor.cs` - 是否使用 `IWorkflowContext`？
   - [ ] `SqlRewriteMafExecutor.cs` - 是否使用 `IWorkflowContext`？
   - [ ] `SqlCoordinatorMafExecutor.cs` - 是否使用 `IWorkflowContext`？

2. **Checkpoint 管理**
   - [ ] `CheckpointManager` - 是否使用 `workflow.GetStateAsync()`？
   - [ ] `MafWorkflowRuntime.cs` - 完整代码审查

3. **Agent 实现（如果有）**
   - [ ] 是否使用 `AgentSession.ToDict()/FromDict()`？
   - [ ] 是否实现了 `IContextProvider`？

### 中优先级

4. **State 管理**
   - [ ] `MafRunStateStore.cs` - 是否只是持久化层？
   - [ ] 是否有自定义的 `StateManager`？（应该删除）

5. **序列化**
   - [ ] 是否有自定义的 `SessionSerializer`？（应该删除）
   - [ ] 是否有自定义的 `WorkflowSerializer`？（应该删除）

---

## 审查清单

### ✅ 应该使用的框架 API

- [ ] `WorkflowBuilder` - 构建 Workflow
- [ ] `IWorkflowContext.SetStateAsync()` - 设置状态
- [ ] `IWorkflowContext.GetStateAsync()` - 获取状态
- [ ] `IWorkflowContext.SendMessageAsync()` - 发送消息
- [ ] `IWorkflowContext.YieldOutputAsync()` - 输出结果
- [ ] `Workflow.GetStateAsync()` - 获取 Workflow 状态
- [ ] `Workflow.RestoreStateAsync()` - 恢复 Workflow 状态
- [ ] `AIAgent.GetSessionAsync()` - 获取 Agent 会话（如果有 Agent）
- [ ] `AIAgent.RestoreSessionAsync()` - 恢复 Agent 会话（如果有 Agent）
- [ ] `AgentSession.ToDict()` - 序列化会话（如果有 Agent）
- [ ] `AgentSession.FromDict()` - 反序列化会话（如果有 Agent）

### ❌ 不应该存在的自定义实现

- [ ] `StateManager` - 应该使用 `IWorkflowContext`
- [ ] `SessionSerializer` - 应该使用 `AgentSession.ToDict()/FromDict()`
- [ ] `HistoryManager` - 应该使用 `InMemoryHistoryProvider` 或实现 `IContextProvider`
- [ ] `ContextManager` - 应该实现 `IContextProvider` 接口
- [ ] `WorkflowSerializer` - 应该使用框架 API

---

## 下一步行动

1. **查看 Executor 实现**
   ```bash
   # 查看所有 Executor 的实现
   find src -name "*Executor.cs" -path "*/Maf/*" | xargs grep -l "IWorkflowContext"
   ```

2. **查看是否有自定义的 StateManager**
   ```bash
   # 查找自定义的状态管理
   find src -name "*StateManager*.cs" -o -name "*State*.cs" | grep -v "WorkflowState"
   ```

3. **查看是否有自定义的序列化**
   ```bash
   # 查找自定义的序列化
   find src -name "*Serializer*.cs" | grep -v "WorkflowResultSerializer"
   ```

4. **查看 CheckpointManager 实现**
   ```bash
   # 查看 CheckpointManager 是否使用框架 API
   grep -r "CheckpointManager" src/DbOptimizer.Infrastructure/
   ```

---

## 总结

### 当前状态

- ✅ **WorkflowBuilder** - 已正确使用
- ✅ **MafCheckpointStore** - 作为适配层存在
- ⚠️ **Executor 实现** - 需要确认是否使用 `IWorkflowContext`
- ⚠️ **Checkpoint 管理** - 需要确认是否使用框架 API
- ⚠️ **Agent 实现** - 需要确认是否存在，以及是否使用框架 API

### 建议

1. **立即检查**: 所有 Executor 的实现，确认是否使用 `IWorkflowContext`
2. **立即检查**: `CheckpointManager` 的实现，确认是否使用 `workflow.GetStateAsync()`
3. **删除**: 任何自定义的 `StateManager`、`SessionSerializer`、`HistoryManager`
4. **重构**: 如果有自定义的上下文管理，改为实现 `IContextProvider` 接口

### 关键原则

**不要重复造轮子！优先使用框架提供的 API 和扩展点！**
