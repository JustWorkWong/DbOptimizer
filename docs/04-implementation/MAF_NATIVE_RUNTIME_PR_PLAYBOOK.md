# MAF Native Runtime PR Playbook

> 配套文档：`MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`  
> 目的：把 refactor checklist 再细化为可直接执行的 PR 剧本

## 1. 推荐顺序

1. `PR-A`：校准现状与文档
2. `PR-B`：接入 Native `CheckpointManager`
3. `PR-C`：收口 runtime/facade，去掉 `Task.Run`
4. `PR-D`：review gate 改为 `ExternalRequest/ExternalResponse`
5. `PR-E`：统一事件投影与日志
6. `PR-F`：删除旧路径并回写文档

## 2. PR-A

### 目标

- 对齐计划文档、README、当前代码状态
- 固化现状测试，作为后续重构基线

### 开发步骤

1. [ ] 审阅 runtime/review/checkpoint 当前实现
2. [ ] 明确哪些能力已经落地，哪些仍是计划项
3. [ ] 补充或整理基线测试
4. [ ] 回写 `MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md` 实施状态
5. [ ] 修正 `README.md` 中超前于代码的描述
6. [ ] 更新 `MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`

### 重点文件

- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`
- [ ] `README.md`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/MafWorkflowRuntimeTests.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/MafNativeWorkflowInteropTests.cs`

### 完成门槛

- [ ] 文档状态与当前代码一致
- [ ] `PR-0 / PR-1` 已完成项被明确标注
- [ ] review request/response 主链路未再被误写为“已完成”

### 建议提交信息

- [ ] `docs: align maf native runtime status with current implementation`

### 建议测试

- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter Maf`

## 3. PR-B

### 目标

- 让 checkpoint 真正进入 MAF 原生执行路径

### 开发步骤

1. [ ] 确认 MAF 1.1.0 下 checkpoint manager 的正式接入方式
2. [ ] 设计 `CheckpointManager` 与 PostgreSQL/Redis 的映射
3. [ ] 改造 `MafCheckpointStore`
4. [ ] 调整 `MafRunStateStore` 职责，降级为 run/session/checkpoint 索引层
5. [ ] 在 SQL workflow 启动路径接入 checkpoint manager
6. [ ] 在 Config workflow 启动路径接入 checkpoint manager
7. [ ] 明确 `workflow_sessions.state` / `engine_checkpoint_ref` 的职责边界
8. [ ] 补 checkpoint 相关测试

### 重点文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

### 完成门槛

- [ ] 执行路径中已经拿到真实 checkpoint
- [ ] checkpoint 丢失/损坏时错误明确
- [ ] 暂时不要求 review gate 已原生化

### 建议提交信息

- [ ] `feat: wire maf checkpoint manager into workflow execution`

### 建议测试

- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter Checkpoint`
- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter Maf`
- [ ] `dotnet build src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`

## 4. PR-C

### 目标

- 把 workflow 生命周期从 starter 收到 runtime/facade
- 去掉主路径里的 `Task.Run`

### 开发步骤

1. [ ] 确定统一执行入口
2. [ ] 缩减或移除 `MafSqlWorkflowStarter` 的生命周期职责
3. [ ] 缩减或移除 `MafConfigWorkflowStarter` 的生命周期职责
4. [ ] 去掉 SQL 主路径 `Task.Run`
5. [ ] 去掉 Config 主路径 `Task.Run`
6. [ ] 去掉 `ResumeAsync` 中的 fire-and-forget
7. [ ] 统一由 runtime/facade 基于 `RunStatus` 写 session 状态
8. [ ] 保持 API “快速返回 + 异步观察”语义

### 重点文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowApplicationService.cs`
- [ ] `src/DbOptimizer.API/Api/WorkflowApi.cs`

### 完成门槛

- [ ] 核心主路径不再依赖 `Task.Run`
- [ ] API 仍快速返回
- [ ] 暂时允许 review gate 还是旧语义

### 建议提交信息

- [ ] `refactor: centralize maf workflow execution in runtime facade`

### 建议测试

- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter MafWorkflowRuntime`
- [ ] `dotnet test tests/DbOptimizer.API.Tests/DbOptimizer.API.Tests.csproj --filter Workflow`
- [ ] `dotnet build src/DbOptimizer.API/DbOptimizer.API.csproj`

## 5. PR-D

### 目标

- 用原生 `ExternalRequest/ExternalResponse` 替换异常挂起和伪恢复

### 开发步骤

1. [ ] 设计统一 review request/response payload
2. [ ] 调整 `review_tasks` 映射字段
3. [ ] 重写 `SqlHumanReviewGateExecutor`
4. [ ] 重写 `ConfigHumanReviewGateExecutor`
5. [ ] 改造 review task gateway，持久化 `taskId/requestId/checkpointRef`
6. [ ] 改造 `ReviewApi` / `ReviewApplicationService`
7. [ ] 用原生 resume 恢复 SQL workflow
8. [ ] 用原生 resume 恢复 Config workflow
9. [ ] 删除审核后直接标记 `completed` 的兜底逻辑
10. [ ] 补 approve/reject/adjust 三条链路测试

### 重点文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/IWorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`
- [ ] `src/DbOptimizer.API/Api/ReviewApi.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

### 完成门槛

- [ ] 审核点表现为 `PendingRequests`
- [ ] 审核提交流程能真实恢复执行
- [ ] approve/reject/adjust 都稳定

### 建议提交信息

- [ ] `feat: migrate workflow review gate to native maf external requests`

### 建议测试

- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter Review`
- [ ] `dotnet test tests/DbOptimizer.API.Tests/DbOptimizer.API.Tests.csproj --filter Review`
- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter MafNativeWorkflowInterop`
- [ ] `dotnet build src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`

## 6. PR-E

### 目标

- 统一事件源与日志字段，支撑 SSE/history/timeline 与排障

### 开发步骤

1. [x] 明确 MAF run event 到业务事件的映射
2. [x] 优先从 run stream 消费事件
3. [x] 改造 projection writer，使其只消费统一事件源
4. [x] 增加 `runId/requestId/checkpointId/superstep` 日志字段
5. [x] 删掉 starter/runtime 中重复的手工事件发布
6. [x] 验证 SSE/history/timeline 不回归

### 重点文件

- [x] `src/DbOptimizer.Infrastructure/Workflows/Events/*`
- [x] `src/DbOptimizer.Infrastructure/Workflows/Projection/*`
- [x] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafExecutorInstrumentation.cs`
- [x] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`

### 完成门槛

- [x] 前端事件协议不变
- [x] SSE/history/timeline 不重复不丢失
- [x] 日志可以定位 run/request/checkpoint/superstep

### 建议提交信息

- [x] `refactor: project maf run events into workflow timeline and logs`

### 建议测试

- [x] `dotnet test tests/DbOptimizer.API.Tests/DbOptimizer.API.Tests.csproj --filter "WorkflowEventHubTests|HistoryQueryServiceTests|ReviewApplicationServiceTests"`
- [x] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj --filter "WorkflowProjectionWriterTests|MafWorkflowRuntimeTests|MafNativeWorkflowInteropTests"`
- [x] `dotnet build src/DbOptimizer.API/DbOptimizer.API.csproj`

## 7. PR-F

### 目标

- 删除旧 suspend/resume 路径
- 切默认配置到新实现
- 回写所有文档

### 开发步骤

1. [ ] 删除 `WorkflowSuspendedException` 与关联 catch
2. [ ] 删除 `ResumeAsync` 中的占位与伪完成逻辑
3. [ ] 删除 runtime/starter 中不再使用的旧接口、旧参数
4. [ ] 切 feature flag 默认值到新路径
5. [ ] 更新 `README.md`
6. [ ] 更新 `docs/MAF-GUIDE.md`
7. [ ] 更新 `MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md`
8. [ ] 更新 `MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`

### 重点文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- [ ] `README.md`
- [ ] `docs/MAF-GUIDE.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`

### 完成门槛

- [ ] 旧 runtime 语义已从主路径移除
- [ ] 默认配置已转向新路径
- [ ] 文档与代码最终一致

### 建议提交信息

- [ ] `chore: remove legacy maf runtime paths and finalize docs`

### 建议测试

- [ ] `dotnet test tests/DbOptimizer.Infrastructure.Tests/DbOptimizer.Infrastructure.Tests.csproj`
- [ ] `dotnet test tests/DbOptimizer.API.Tests/DbOptimizer.API.Tests.csproj`
- [ ] `dotnet build src/DbOptimizer.API/DbOptimizer.API.csproj`
- [ ] `dotnet build src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`
