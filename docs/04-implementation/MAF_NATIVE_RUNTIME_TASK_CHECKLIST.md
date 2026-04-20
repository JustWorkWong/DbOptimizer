# MAF Native Runtime Refactor Task Checklist

## Execution Status (2026-04-21)

- [x] PR-A completed
- [x] PR-B completed
- [x] PR-C completed
- [x] PR-D completed
- [x] PR-E completed
- [x] PR-F completed
- [ ] Follow-up governance items (feature-flag defaults, concurrency policy, legacy session tooling) remain for later dedicated tasks

> 基于 [MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md](./MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md) 与当前代码现状整理  
> 更新时间：2026-04-20  
> 适用范围：`DbOptimizer` 当前 MAF runtime 主链路重构

## 1. 当前状态快照

### 1.1 已完成

- [x] `PR-0` 基线升级已完成
  - [x] `Microsoft.Agents.AI.Workflows` 已升级到 `1.1.0`
  - [x] 相关 `Microsoft.Extensions.*` 依赖已对齐
  - [x] `DbOptimizer.Infrastructure` / `DbOptimizer.API` 已验证可构建
- [x] `PR-1` 最小 MAF 原生互操作验证已完成
  - [x] 已有 `MafNativeWorkflowInteropTests`
  - [x] 已验证 `PendingRequests -> ExternalResponse -> Ended`
  - [x] 已验证 `CheckpointInfo -> ResumeStreamingAsync -> Ended`
  - [x] 范围限定：以上仅证明隔离测试中的原生互操作可用，不代表当前业务主链路已切到原生 checkpoint/request-response/resume
- [x] workflow graph 和 executor 仍然是 MAF 原生构建
  - [x] `MafWorkflowFactory` 继续使用 `WorkflowBuilder`
  - [x] SQL / Config executors 未被推倒重写

### 1.2 未完成但文档中已规划

- [ ] runtime 仍未切换到 MAF 原生 run 生命周期
  - [ ] `MafSqlWorkflowStarter` 仍通过 `Task.Run` 后台执行
  - [ ] `MafConfigWorkflowStarter` 仍通过 `Task.Run` 后台执行
  - [ ] `MafWorkflowRuntime.ResumeAsync` 仍是占位实现
- [ ] human review 仍不是 MAF 原生 request/response
  - [ ] `SqlHumanReviewGateExecutor` 仍通过 `WorkflowSuspendedException` 挂起
  - [ ] `ConfigHumanReviewGateExecutor` 仍通过 `WorkflowSuspendedException` 挂起
  - [ ] `ReviewApi` 提交审核后仍走自定义 response message，而不是 `ExternalResponse`
- [ ] checkpoint 仍不是 `CheckpointManager` 中心化接入
  - [ ] 当前主链路仍未在 `RunAsync/RunStreamingAsync` 中传入 checkpoint manager
  - [ ] `MafCheckpointStore` / `MafRunStateStore` 仍是项目自定义语义
  - [ ] `workflow_sessions.engine_checkpoint_ref` 已有字段，但不是由原生 checkpoint 生命周期驱动
- [ ] 事件与日志仍以自定义 runtime/starter 为主
  - [ ] `WorkflowWaitingReview` 等事件仍由 starter 手工发布
  - [ ] 未统一消费 MAF run event / superstep / request event
  - [ ] 当前日志无法稳定回答具体卡在哪个 request/checkpoint/superstep

### 1.3 明显不一致项

- [ ] `README.md` 中“人工审核 + MAF Request/Response”“MAF Workflow checkpoint”已完成的描述，需要与真实代码状态重新对齐
- [ ] 若近期要按 checklist 执行，应先约定：以代码现状为准，不以 README 叙述为准

## 2. 总体执行策略

- [ ] 以“小步 PR”推进，不做一次性大改
- [ ] 每个阶段结束都保留可运行基线
- [ ] 优先打通 SQL workflow 的 review + checkpoint + resume 真闭环
- [ ] Config workflow 采用同构复制，不先并行做双线大改
- [ ] 前端 API / SSE 协议尽量保持兼容

## 3. 阶段 0：基线固化与差异校准

### 目标

- [ ] 固化当前行为，防止后续重构时“以为没变、其实已经变了”

### Checklist

- [ ] 补一份“当前主链路现状说明”
  - [ ] SQL workflow 当前启动路径
  - [ ] Config workflow 当前启动路径
  - [ ] 当前 suspend / resume 的真实行为
  - [ ] 当前 checkpoint 字段实际写入时机
- [ ] 记录当前已知偏差
  - [ ] `ResumeAsync` 不是原生恢复
  - [ ] review gate 依赖异常挂起
  - [ ] starter 依赖 `Task.Run`
  - [ ] 审核通过后被直接标记 `completed`
- [ ] 补齐或确认基线测试
  - [ ] SQL: `start -> completed`
  - [ ] SQL: `start -> waiting review`
  - [ ] Config: `start -> completed`
  - [ ] Config: `start -> waiting review`
  - [ ] review submit 后当前旧行为的验证测试
- [ ] 固化基线日志样例
  - [ ] workflow started
  - [ ] waiting review
  - [ ] failed
  - [ ] review submit

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/*`

### 验收

- [ ] 能用测试和日志证明“当前系统实际上怎么工作”
- [ ] 后续每个 PR 都能对比重构前后的行为变化

## 4. 阶段 1：接入原生 CheckpointManager

### 目标

- [ ] 让 checkpoint 真正以 MAF `CheckpointManager` 为中心

### Checklist

- [ ] 设计 checkpoint 适配层
  - [ ] 明确继续保留哪些现有接口
  - [ ] 明确哪些接口降级为兼容层
- [ ] 为 MAF 1.1.0 选定正式接入方式
  - [ ] 评估 `CheckpointManager.CreateJson(...)` 或等价正式入口
  - [ ] 明确底层使用 PostgreSQL / Redis 的映射方式
- [ ] 新建或改造 checkpoint adapter
  - [ ] 让 MAF checkpoint store 只负责存取原生 checkpoint 数据
  - [ ] 不再自己定义额外 checkpoint 生命周期
- [ ] 改造 workflow 启动入口
  - [ ] SQL workflow 启动路径传入 checkpoint manager
  - [ ] Config workflow 启动路径传入 checkpoint manager
- [ ] 明确 session 存储边界
  - [ ] `workflow_sessions.state` 只存业务快照
  - [ ] `engine_run_id` 保留 run 关联
  - [ ] `engine_checkpoint_ref` 保留 checkpoint 引用
  - [ ] 不再把引擎内部执行语义塞进 `state`
- [ ] 处理兼容层
  - [ ] 评估 `IMafRunStateStore` 是否保留
  - [ ] 若保留，降级为索引/映射层
  - [ ] 清理 `MafCheckpointStore` 中基于 `runId` 反推 `sessionId` 的假设

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

### 测试

- [ ] 新增 checkpoint manager 接入测试
- [ ] 验证 checkpoint 可以被保存并取回
- [ ] 验证 checkpoint 缺失时抛出明确错误
- [ ] 验证 SQL/Config workflow 在 checkpoint 开启后不回退旧行为

### 验收

- [ ] 每次 workflow 执行都能拿到真实 `CheckpointInfo`
- [ ] 代码不再依赖“手工保存 runId + checkpointRef 的影子状态”才能恢复

## 5. 阶段 2：去掉 starter 主导的 fire-and-forget 外壳

### 目标

- [ ] 从“starter 管生命周期”切到“runtime/facade 管生命周期”

### Checklist

- [ ] 设计新的执行入口
  - [ ] 明确是否引入 `MafExecutionFacade`
  - [ ] 或让 `MafWorkflowRuntime` 直接承担 facade 角色
- [ ] 收敛 starter 职责
  - [ ] 只保留 session 初始化辅助逻辑
  - [ ] 只保留输入组装辅助逻辑
  - [ ] 删除生命周期决策责任
- [ ] 移除核心主路径 `Task.Run`
  - [ ] SQL 启动路径去掉后台 fire-and-forget
  - [ ] Config 启动路径去掉后台 fire-and-forget
  - [ ] Resume 路径去掉后台 fire-and-forget
- [ ] 切换到受管执行模型
  - [ ] 评估 `RunAsync(...)` vs `RunStreamingAsync(...)`
  - [ ] 若需要实时投影，优先统一到 `RunStreamingAsync(...)`
  - [ ] API 仍保持“快速返回 + 异步观察”语义
- [ ] 状态同步收敛
  - [ ] session 状态由 MAF `RunStatus` 推导
  - [ ] 删除 starter 中重复的状态写入分支

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowApplicationService.cs`
- [ ] `src/DbOptimizer.API/Api/WorkflowApi.cs`

### 测试

- [ ] 启动接口仍然快速返回
- [ ] workflow 不再依赖裸 `Task.Run`
- [ ] start/status/cancel 基本路径不回归

### 验收

- [ ] runtime 成为唯一执行入口
- [ ] starter 不再负责 workflow 生命周期编排

## 6. 阶段 3：把 review gate 改成原生 ExternalRequest/ExternalResponse

### 目标

- [ ] 用 MAF request/response 替代异常挂起与伪恢复

### Checklist

- [ ] 重写 SQL review gate
  - [ ] `SqlHumanReviewGateExecutor` 不再抛 `WorkflowSuspendedException`
  - [ ] 改为创建 `ExternalRequest`
  - [ ] payload 使用统一结构
- [ ] 重写 Config review gate
  - [ ] `ConfigHumanReviewGateExecutor` 不再抛 `WorkflowSuspendedException`
  - [ ] 改为创建 `ExternalRequest`
- [ ] 设计统一 request payload
  - [ ] `sessionId`
  - [ ] `workflowType`
  - [ ] `taskId`
  - [ ] `stage`
  - [ ] `resultType`
  - [ ] `draftResult`
  - [ ] `allowAdjustments`
  - [ ] `createdAt`
- [ ] 设计统一 response payload
  - [ ] `action = approve/reject/adjust`
  - [ ] `comment`
  - [ ] `adjustments`
  - [ ] `reviewedAt`
- [ ] 改造 review task 映射关系
  - [ ] `review_tasks` 明确成为 request 映射表
  - [ ] 保存 `request_id`
  - [ ] 保存 `engine_run_id`
  - [ ] 保存 `engine_checkpoint_ref`
  - [ ] 评估是否新增 `request_payload` JSONB
- [ ] 改造 review submit 流程
  - [ ] `ReviewApi` / `ReviewApplicationService` 构造 `ExternalResponse`
  - [ ] 通过 `Run.ResumeAsync(...)` 或等价原生恢复入口继续执行
  - [ ] 不再在审核通过后直接把 session 改成 `completed`
- [ ] 处理多 pending request 设计
  - [ ] 主路径先支持单 request
  - [ ] 表结构和 API 关联按多 request 兼容设计

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/IWorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`
- [ ] `src/DbOptimizer.API/Api/ReviewApi.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`

### 测试

- [ ] SQL: `waiting review -> approve -> completed`
- [ ] SQL: `waiting review -> reject -> failed`
- [ ] SQL: `waiting review -> adjust -> completed`
- [ ] Config: `waiting review -> approve -> completed`
- [ ] Config: `waiting review -> reject -> failed`
- [ ] requestId 不匹配时报错明确
- [ ] 重复提交 review 时返回冲突

### 验收

- [ ] workflow 在审核点表现为 `PendingRequests`
- [ ] 审核提交后从 checkpoint 继续执行，而不是伪完成

## 7. 阶段 4：统一事件投影

### 目标

- [x] 让业务事件来源从手写 runtime/starter 迁到 MAF run event 投影

### Checklist

- [ ] 明确事件映射表
  - [ ] `RequestInfoEvent -> WorkflowWaitingReview`
  - [ ] `ExecutorCompletedEvent -> ExecutorCompleted`
  - [ ] `SuperStepCompletedEvent -> 进度/日志投影`
  - [ ] `RunStatus.Ended -> WorkflowCompleted`
  - [ ] `失败/异常 -> WorkflowFailed`
- [ ] 统一投影入口
  - [ ] `WorkflowProjectionWriter` 只消费统一事件源
  - [x] SSE / history / timeline 继续使用现有业务协议
- [ ] 删除重复手工发布
  - [ ] starter 中 `WorkflowStarted/WaitingReview/Completed/Failed` 的手工发布逻辑
  - [ ] runtime 中与 run event 重复的发布逻辑
- [ ] 对齐前端兼容性
- [x] 前端事件协议不变
  - [ ] 事件 payload 保持兼容

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Workflows/Events/*`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Projection/*`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafExecutorInstrumentation.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`

### 测试

- [ ] SSE 仍能正确推送 started/running/waiting review/completed/failed
- [ ] history/timeline 数据不回归
- [ ] 同一执行过程不再产生重复事件

### 验收

- [x] 业务事件明确来自 MAF run event 投影
- [ ] starter/runtime 手工补事件显著减少或消失

## 8. 阶段 5：统一日志与可观测性

### 目标

- [x] 能从日志直接定位卡点、request、checkpoint、superstep

### Checklist

- [x] 为每个 run 统一日志字段
  - [ ] `SessionId`
  - [ ] `RunId`
  - [ ] `WorkflowType`
  - [ ] `CheckpointId`
  - [ ] `RequestId`
  - [ ] `SuperStep`
- [x] 记录 superstep 级日志
  - [ ] activated executors
  - [ ] pending messages
  - [ ] pending requests
  - [ ] checkpoint id
- [x] 记录 external request/response 日志
  - [ ] request payload type
  - [ ] request id
  - [ ] task id
  - [ ] review action
- [ ] 保留 provider/MCP 细节日志
  - [ ] 不覆盖现有 provider 级排障信息

### 主要涉及文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafExecutorInstrumentation.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] 各 executor / provider 日志相关文件

### 验收

- [x] 能回答“卡在哪个 executor / superstep / request / checkpoint”
- [ ] 不再出现只有 `session stuck` 但看不到引擎内部状态的情况

## 9. 阶段 6：清理旧路径与文档回写

### 目标

- [ ] 移除旧 runtime 语义，避免后续偷偷回退

### Checklist

- [ ] 删除 resume 中的占位实现
  - [ ] 删除 `TODO: 真正的 checkpoint 恢复`
  - [ ] 删除审核后直接标记 `completed` 的逻辑
- [ ] 删除异常挂起路径
  - [ ] 删除 `WorkflowSuspendedException`
  - [ ] 删除依赖该异常的 catch 分支
- [ ] 删除 starter 主路径中的 `Task.Run`
- [ ] 删除不再需要的旧接口 / 旧参数 / 旧映射
- [ ] 回写文档
  - [ ] 更新 `README.md`
  - [ ] 更新 `docs/MAF-GUIDE.md`
  - [ ] 更新 refactor plan 的实施状态

### 验收

- [ ] 整条 workflow 生命周期只保留一套语义
- [ ] 文档与代码一致

## 10. Feature Flag 与回滚 Checklist

### Checklist

- [ ] 引入配置项
  - [ ] `WorkflowExecution:UseNativeMafCheckpointing`
  - [ ] `WorkflowExecution:UseNativeMafReviewRequests`
  - [ ] `WorkflowExecution:UseNativeMafEventProjection`
  - [ ] `WorkflowExecution:UseUpgradedMafPackages`
- [ ] 每个阶段都支持开关回滚
  - [ ] checkpoint 接入异常可关闭 native checkpointing
  - [ ] review gate 改造异常可关闭 native review requests
  - [ ] event projection 改造异常可关闭 native projection
- [ ] 处理存量 session
  - [ ] 标记 legacy suspended session
  - [ ] 旧 suspended session 不强行迁移到新语义
  - [ ] 必要时提供只读提示或重新发起流程

## 11. 并发控制 Checklist

> 该项建议与阶段 2 同步设计，最迟不晚于阶段 3 落地

- [ ] 明确实例内并发上限
- [ ] 明确同一 `databaseId` 的冲突策略
- [ ] 配置项预留
  - [ ] `WorkflowExecution:MaxConcurrentRuns`
  - [ ] `WorkflowExecution:MaxConcurrentSqlRuns`
  - [ ] `WorkflowExecution:MaxConcurrentConfigRuns`
- [ ] 第一版至少提供“限流 + 拒绝 + 日志”
- [ ] 若后续拒绝率高，再评估队列化

## 12. 建议 PR 拆分

### PR-A：状态校准与文档对齐

- [ ] 固化现状测试
- [ ] 标注文档与代码差异
- [ ] 补充实施说明

#### 建议改动文件

- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/MafWorkflowRuntimeTests.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/MafNativeWorkflowInteropTests.cs`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`
- [ ] `README.md`

#### 验收重点

- [ ] 已完成项和未完成项边界清晰
- [ ] 后续 PR 不会重复做已经落地的 `PR-0 / PR-1`
- [ ] README 不再宣称 review request/response 主链路已完成

### PR-B：Native CheckpointManager 接入

- [ ] checkpoint manager adapter
- [ ] SQL/Config 启动接入 checkpoint
- [ ] session 字段边界清理

#### 建议改动文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafCheckpointStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafRunStateStore.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/*Checkpoint*.cs`

#### 验收重点

- [ ] workflow 执行时能拿到真实 checkpoint
- [ ] checkpoint 缺失或损坏时错误可观察
- [ ] 现有 SQL/Config 启动路径不回归

### PR-C：Runtime/Facade 收口

- [ ] 去掉 starter 主导生命周期
- [ ] 去掉核心 `Task.Run`
- [ ] 建立统一执行入口

#### 建议改动文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Application/WorkflowApplicationService.cs`
- [ ] `src/DbOptimizer.API/Api/WorkflowApi.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/MafWorkflowRuntimeTests.cs`

#### 验收重点

- [ ] 核心主路径不再依赖 `Task.Run`
- [ ] API 仍保持快速返回
- [ ] runtime 成为统一入口

### PR-D：Review Gate 原生化

- [ ] SQL review gate 改为 `ExternalRequest/ExternalResponse`
- [ ] Config review gate 同步改造
- [ ] review submit 改为真正 resume

#### 建议改动文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/IWorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewTaskGateway.cs`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Review/WorkflowReviewResponseFactory.cs`
- [ ] `src/DbOptimizer.API/Api/ReviewApi.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs`
- [ ] `tests/DbOptimizer.API.Tests/ReviewApplicationServiceTests.cs`
- [ ] `tests/DbOptimizer.Infrastructure.Tests/Maf/*Review*.cs`

#### 验收重点

- [ ] workflow 审核点表现为 `PendingRequests`
- [ ] 审核提交后从 checkpoint 继续，不再伪完成
- [ ] approve/reject/adjust 三种路径都稳定

### PR-E：事件投影与日志统一

- [x] run event 投影
- [x] 日志字段统一
- [x] SSE / history 验证

#### 建议改动文件

- [ ] `src/DbOptimizer.Infrastructure/Workflows/Events/*`
- [ ] `src/DbOptimizer.Infrastructure/Workflows/Projection/*`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafExecutorInstrumentation.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `tests/DbOptimizer.BackendE2ETests/Workflows/*`

#### 验收重点

- [x] 前端事件协议不变
- [x] SSE/history/timeline 数据不重复不丢失
- [x] 日志能看到 request/checkpoint/superstep

### PR-F：旧路径清理与文档收尾

- [ ] 删旧实现
- [ ] 开关默认切换
- [ ] README / GUIDE / plan 回写

#### 建议改动文件

- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntime.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafSqlWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/Runtime/MafConfigWorkflowStarter.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlHumanReviewGateExecutor.cs`
- [ ] `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- [ ] `README.md`
- [ ] `docs/MAF-GUIDE.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_REFACTOR_PLAN.md`
- [ ] `docs/04-implementation/MAF_NATIVE_RUNTIME_TASK_CHECKLIST.md`

#### 验收重点

- [ ] 旧 suspend/resume 伪语义已删除
- [ ] 默认路径切到原生 MAF 方案
- [ ] 文档与代码最终一致

## 13. 最小优先闭环

- [ ] 先打通 SQL workflow
- [ ] 先覆盖 review gate
- [ ] 先实现 checkpoint/resume 真恢复
- [ ] Config workflow 在 SQL 路径稳定后复制落地

### 推荐实施顺序

1. `PR-A`：先把现状说清楚，避免误判进度。
2. `PR-B`：先把 checkpoint 真正接进主链路，因为它是后续原生 resume 的前提。
3. `PR-C`：再收 runtime，去掉 `Task.Run` 外壳，统一执行入口。
4. `PR-D`：然后替换 review gate 语义，打通 `ExternalRequest/ExternalResponse` 真闭环。
5. `PR-E`：最后再收事件投影和日志，不和核心语义改造同时爆炸。
6. `PR-F`：等新链路稳定后删旧路径和回写文档。

### 不建议调整的顺序

- [ ] 不建议先做 `PR-D` 再做 `PR-B`
  - [ ] review request/response 依赖 checkpoint/resume 真语义
- [ ] 不建议把 `PR-B + PR-C + PR-D` 合成一个大 PR
  - [ ] 风险定位困难
  - [ ] 回滚粒度太粗
- [ ] 不建议 SQL / Config 两条线并行改 review gate
  - [ ] 先用 SQL 打穿主链路更稳

## 14. 完成定义

- [ ] SQL/Config 两条 workflow 都支持
  - [ ] `start -> completed`
  - [ ] `start -> waiting review`
  - [ ] `waiting review -> approve -> completed`
  - [ ] `waiting review -> reject -> failed`
  - [ ] provider error -> failed
- [ ] checkpoint 存在时可恢复
- [ ] checkpoint 丢失时报错明确
- [ ] requestId 不匹配时报错明确
- [x] 日志可定位卡点
- [ ] 前端协议不回归
- [ ] README 与实现状态一致

## 15. 执行时的优先级标签

### P0：阻塞主链路

- [ ] `PR-B` Native CheckpointManager 接入
- [ ] `PR-C` Runtime/Facade 收口
- [ ] `PR-D` Review Gate 原生化

### P1：强烈建议同轮完成

- [ ] `PR-A` 状态校准与文档对齐
- [x] `PR-E` 事件投影与日志统一

### P2：收尾与治理

- [ ] `PR-F` 旧路径清理与文档收尾
- [ ] 并发控制增强
- [ ] 存量 legacy session 管理脚本
