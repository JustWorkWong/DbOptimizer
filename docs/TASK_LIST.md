# DbOptimizer 任务清单、验收标准与实测方案

**项目**：DbOptimizer（v1）  
**依据文档**：`docs/REQUIREMENTS.md`、`docs/WORKFLOW_DESIGN.md`、`docs/DATA_MODEL.md`、`docs/API_SPEC.md`、`docs/P0_P1_DESIGN.md`  
**目标**：形成可执行任务分解 + 明确验收口径 + 最终真实联调与全流程实测  
**当前执行约束（已确认）**：
- Aspire 统一托管/汇聚 MAF 日志，不再做手动日志管理
- AI 接入采用配置文件驱动（endpoint/model/apiKey 均从配置读取，禁止在代码中写死）
- SSE 仅用于 AI 相关流程事件推送，非 AI 步骤通过普通 API 查询
- 联调验收数据库使用 Aspire 创建的 PostgreSQL + MySQL 测试库（固定端口/账号/密码由测试环境配置文件统一管理，不在业务代码中写死；脚本初始化数据）

**当前里程碑进展（2026-04-16）**：
- [x] M0-03 可观测性与日志治理（requestId/sessionId/executionId 已落地）
- [x] M0-04 AI Provider 接入配置（endpoint/model/apiKey/requestTimeout/maxTokens 配置化，支持本地密钥覆盖）
- [x] M0-05 测试数据库编排与初始化（PostgreSQL + MySQL 编排与初始化脚本已接入）

---

## 1. 任务分解总览（P0 / P1）

### P0（必须实现，v1 发布门槛）

#### M0-基础设施与工程骨架
1. **M0-01 Aspire 编排落地**
   - 内容：AppHost 编排 API / AgentRuntime / Web / PostgreSQL / Redis
   - 依赖：无
   - 交付：可一键启动全栈开发环境
2. **M0-02 配置体系与密钥管理**
   - 内容：连接串、AI Key、MCP 配置、环境变量规范化
   - 依赖：M0-01
   - 交付：本地/测试环境可稳定加载配置
3. **M0-03 可观测性与日志治理（Aspire 托管）**
   - 内容：使用 Aspire 对 API/AgentRuntime 的日志进行统一采集与查看；保留 requestId/sessionId/executionId 结构化字段
   - 依赖：M0-01
   - 交付：可按 Workflow 全链路追踪日志（无需手动维护独立日志链路）
4. **M0-04 AI Provider 接入配置（配置驱动）**
   - 内容：统一封装 AI 客户端配置（endpoint/model/apiKey/requestTimeout/maxTokens），默认开发配置可用，支持环境变量覆盖；禁止在业务代码中硬编码 endpoint/model；API Key 写入开发环境本地配置文件（dev json）并确保不提交到 git
   - 依赖：M0-02
   - 交付：切换模型或端点无需改代码，仓库与日志不落明文密钥

5. **M0-05 测试数据库编排与初始化（Aspire）**
   - 内容：通过 Aspire 创建 PostgreSQL + MySQL 测试库，端口/账号/密码由测试环境配置文件统一管理（不在业务代码中写死）；通过初始化脚本构建测试数据（支持 SQL 批量造数）
   - 依赖：M0-01
   - 交付：联调环境可一键获得双库测试数据

#### M1-数据层与持久化
4. **M1-01 核心表与迁移脚本**
   - 内容：`workflow_sessions`、`agent_executions`、`tool_calls`、`agent_messages`、`decision_records`、`review_tasks`、`prompt_versions`、`error_logs`
   - 依赖：M0-01
   - 交付：迁移可重复执行，结构与文档一致
5. **M1-02 JSONB 结构与索引策略**
   - 内容：Checkpoint `state`、证据链 `evidence`、Token 消耗等 JSONB 字段落库 + 必要索引
   - 依赖：M1-01
   - 交付：关键查询（状态/时间/会话）性能达标
6. **M1-03 Checkpoint 双层存储**
   - 内容：PostgreSQL 持久化 + Redis 热缓存；保存/读取/删除
   - 依赖：M1-01
   - 交付：进程重启后可恢复 Running 会话

#### M2-MCP 集成与容错（联调验收覆盖 PostgreSQL + MySQL 双库）
7. **M2-01 MySQL/PostgreSQL MCP 客户端封装**
   - 内容：统一接口、工具调用（query/describe/explain/show_indexes）
   - 依赖：M0-02
   - 交付：两类数据库 MCP 均可稳定调用
8. **M2-02 超时、重试、降级策略（配置驱动）**
   - 内容：MCP 超时、重试次数、退避策略、降级开关统一配置化（按环境可覆盖）；MCP 异常时 fallback（直连查询）受配置开关与审计控制
   - 依赖：M2-01
   - 交付：MCP 不稳定时流程不中断且可观测，无需改代码即可调参

#### M3-SQL 调优 Workflow（核心主线）
9. **M3-01 Workflow 基础框架**
   - 内容：WorkflowContext、Executor 接口、状态机、事件发布
   - 依赖：M1-03
   - 交付：可编排串行执行并可恢复
10. **M3-02 SqlParserExecutor**
    - 内容：解析 SQL（表/字段/JOIN/WHERE）并写入上下文
    - 依赖：M3-01
11. **M3-03 ExecutionPlanExecutor**
    - 内容：通过 MCP 获取执行计划并分析性能瓶颈
    - 依赖：M2-01、M3-02
12. **M3-04 IndexAdvisorExecutor**
    - 内容：生成索引建议（DDL、预估收益、证据引用）
    - 依赖：M3-03
13. **M3-05 CoordinatorExecutor**
    - 内容：汇总建议、给出置信度/原因/证据链
    - 依赖：M3-04
14. **M3-06 HumanReviewExecutor**
    - 内容：进入待审核态，阻塞等待人工动作
    - 依赖：M3-05
15. **M3-07 RegenerationExecutor（驳回回流）**
    - 内容：结合驳回意见重生成，回流次数上限从配置文件读取（默认 3 次）
    - 依赖：M3-06

#### M4-实例调优 Workflow
16. **M4-01 ConfigCollectorExecutor**
    - 内容：收集配置、资源、负载指标
    - 依赖：M3-01
17. **M4-02 ConfigAnalyzerExecutor**
    - 内容：输出参数建议、风险提示、收益预估
    - 依赖：M4-01
18. **M4-03 Coordinator + HumanReview 接入**
    - 内容：配置优化同样走人工审核闭环
    - 依赖：M4-02、M3-06

#### M5-API 与 SSE
19. **M5-01 Workflow API**
    - 内容：创建/查询/取消/恢复 Workflow
    - 依赖：M3、M4
20. **M5-02 Review API**
    - 内容：待审核列表、详情、提交审核（approve/reject/adjust）
    - 依赖：M3-06
21. **M5-03 Dashboard/History API**
    - 内容：总览统计、历史筛选、详情
    - 依赖：M1、M3、M4
22. **M5-04 AI 相关 SSE 事件流**
    - 内容：仅 AI 相关 executor 事件推送（如 AI 解析/建议生成/协调结论）；非 AI 步骤不走 SSE
    - 依赖：M3-01

#### M6-前端页面与交互
23. **M6-01 前端基础骨架（Vue3 + Pinia + Router）**
    - 依赖：M0-01
24. **M6-02 SQL 调优页**
    - 内容：SQL 编辑、数据库选择、执行进度、建议卡片
    - 依赖：M5-01、M5-04
25. **M6-03 实例调优页**
    - 内容：实例参数输入与优化建议展示
    - 依赖：M5-01
26. **M6-04 审核工作台**
    - 内容：同意/驳回/调整、审核意见、历史记录（v1 验收口径：只需有人点击完成审核动作，不引入用户体系）
    - 依赖：M5-02
27. **M6-05 历史任务页**
    - 内容：筛选、详情、版本演进
    - 依赖：M5-03
28. **M6-06 运行回放页**
    - 内容：时间线、Executor 详情、Tool 调用与证据
    - 依赖：M5-04、M5-03

#### M7-慢 SQL 自动抓取
29. **M7-01 慢查询采集任务**
    - 内容：MySQL slow log / PostgreSQL pg_stat_statements 定时抓取
    - 依赖：M2-01
30. **M7-02 自动触发分析 Workflow**
    - 内容：采集结果进入 SQL 分析闭环
    - 依赖：M7-01、M3

#### M8-测试与质量门
31. **M8-01 单元测试（核心组件）**
32. **M8-02 集成测试（API + DB + Redis + MCP Stub）**
33. **M8-03 E2E 测试（前端关键路径）**
34. **M8-04 真实联调验收（启动前后端，走全流程，查日志+数据库）**

---

### P1（应该实现，提升体验与稳定性）

1. **P1-01 PromptVersion 管理 UI（启停、版本回滚）**  
2. **P1-02 慢 SQL 趋势图与告警阈值管理**  
3. **P1-03 数据归档任务（90/30 天策略）**  
4. **P1-04 Workflow 失败自愈增强（更细粒度重试策略）**  
5. **P1-05 运营指标看板（Token 成本、错误率、平均分析耗时）**

---

## 2. 子 Agent 配置与职责分工（建议）

> 原则：按职责拆分，独立任务并行，主 Agent 只做编排与集成验收。

### A. 规划与架构
1. **planner**
   - 负责：每个里程碑的细化计划、依赖检查、风险清单
   - 对应模块：M0~M8 总体拆解
2. **architect**
   - 负责：Workflow 边界、模块依赖、恢复机制一致性评审
   - 对应模块：M1-03、M3、M4、M5

### B. 实现质量与测试
3. **tdd-guide**
   - 负责：新增功能测试先行策略（RED/GREEN/REFACTOR）
   - 对应模块：M3、M4、M5、M6、M7、M8
4. **code-reviewer**
   - 负责：每轮改动后的质量审查（可维护性、复杂度、坏味道）
   - 对应模块：全模块（强制）
5. **csharp-reviewer**
   - 负责：.NET/C# 代码规范、异步模型、性能与空引用安全
   - 对应模块：M0~M5、M7、M8
6. **security-reviewer**
   - 负责：密钥管理、输入边界、日志脱敏、数据库访问安全
   - 对应模块：M0-02、M0-04、M2、M5、M8
7. **e2e-runner**
   - 负责：前端关键流程 E2E 与联调回归
   - 对应模块：M6、M8-03、M8-04

### C. 问题专项
8. **build-error-resolver**
   - 负责：编译失败、类型错误、构建异常快速修复
   - 触发条件：dotnet build / 前端 build 失败
9. **database-reviewer**
   - 负责：PostgreSQL 模型、索引、查询、迁移方案审查
   - 对应模块：M1、M7、M8 数据核对 SQL

### D. 文档同步
10. **doc-updater**
   - 负责：架构变更后同步更新 docs 与 CLAUDE.md（如触发架构级变更）
   - 对应模块：M0~M8

### 并行编排建议（可直接执行）
- **波次 1（基础）**：planner + architect + database-reviewer
- **波次 2（实现）**：tdd-guide + csharp-reviewer + security-reviewer（并行）
- **波次 3（收口）**：code-reviewer + e2e-runner + doc-updater

---

## 3. 里程碑计划（建议）

- **里程碑 A（第 1-2 周）**：M0 + M1 + M2
- **里程碑 B（第 3-4 周）**：M3 + M5（先后端可跑）
- **里程碑 C（第 5 周）**：M6（前端全页面打通）
- **里程碑 D（第 6 周）**：M4 + M7（补齐配置调优和自动抓取）
- **里程碑 E（第 7 周）**：M8（测试、性能、安全、发布验收）

---

## 4. 统一验收标准（DoD）

### 4.1 功能验收
- [ ] 手工 SQL 分析可生成建议（含置信度、原因、证据）
- [ ] 慢 SQL 可定时抓取并自动分析
- [ ] 数据库配置优化可生成参数建议与风险提示
- [ ] 审核支持同意/驳回/调整（v1 口径：仅需有人点击触发审核动作）
- [ ] 驳回后可回流重跑，次数上限遵循配置文件（默认 3 次）
- [ ] 历史任务可筛选并查看详情
- [ ] 运行时间线可展示执行全过程

### 4.2 性能验收
- [ ] 单条 SQL 分析 < 30 秒
- [ ] AI 相关 SSE 推送延迟 < 500ms
- [ ] 3 并发稳定运行（持续）
- [ ] 10 并发冒烟通过（短时）

### 4.3 安全验收
- [ ] 连接字符串加密存储
- [ ] 日志无密码/Token 等敏感信息
- [ ] 输入校验可阻断明显 SQL 注入输入
- [ ] 单次分析 Token 上限控制在 50k 内

### 4.4 可维护性验收
- [ ] 结构化日志字段齐全（sessionId/executorName/status/durationMs）
- [ ] Workflow 失败可从 Checkpoint 恢复
- [ ] 单元+集成+E2E 覆盖率汇总 >= 80%

---

## 5. 最终“真实联调”测试方案（必须执行）

> 该部分是你强调的重点：**真实启动前后端，在前端走全流程，同时检查日志和数据库。**

### 5.1 测试前准备

1. 启动 Aspire（含 API/Web/PostgreSQL/Redis）
2. 准备目标数据库（Aspire 创建 PostgreSQL + MySQL 测试库，端口/账号/密码由测试环境配置文件统一管理，并通过脚本初始化测试数据）
3. 准备测试 SQL 样本
   - Case A：明显慢查询（无索引过滤）
   - Case B：JOIN 顺序不佳
   - Case C：SELECT * + 大表
4. 准备审核测试策略
   - 第 1 次驳回（给出明确理由）
   - 第 2 次调整
   - 第 3 次同意

### 5.2 前端全流程测试路径（逐项打勾）

> 说明：SSE 仅验证 AI 相关执行阶段；非 AI 阶段通过普通 API 轮询/查询确认状态。

#### Flow-1：手工 SQL 调优闭环
- [ ] 进入 SQL 调优页，提交 SQL
- [ ] 页面实时显示 AI 相关 Executor 进度（SSE）
- [ ] 收到建议卡片（索引/重写/证据）
- [ ] 跳转审核工作台并处理（approve/reject/adjust）
- [ ] 若 reject，系统回流并再次生成
- [ ] 最终任务进入 Completed

#### Flow-2：实例配置调优闭环
- [ ] 进入实例调优页，输入参数
- [ ] 生成配置建议（当前值/建议值/原因/风险）
- [ ] 审核通过并形成历史记录

#### Flow-3：历史与回放
- [ ] 历史页可按时间/状态/数据库筛选
- [ ] 可查看同一任务多版本建议演进
- [ ] 回放页可看到 executor.started/completed、tool 调用、checkpoint

#### Flow-4：慢 SQL 自动抓取
- [ ] 定时任务采集到慢 SQL
- [ ] 自动创建分析任务
- [ ] 在前端总览/历史中可见

### 5.3 日志检查项（必须）

在 API 与 AgentRuntime 日志中核对：
- [ ] 每个 Workflow 都有唯一 sessionId
- [ ] 每个 Executor 有 started/completed + durationMs
- [ ] 审核动作日志包含 reviewer action/comment
- [ ] 驳回回流日志有 iteration 次数
- [ ] MCP 调用异常时有 timeout/fallback 标记
- [ ] 日志中不出现 password/token/完整连接串

### 5.4 数据库核对项（必须）

以 `session_id` 为主键链路做落库一致性检查：
- [ ] `workflow_sessions`：状态流转正确（Running → WaitingForReview → Completed/Failed）
- [ ] `agent_executions`：Executor 执行记录完整
- [ ] `tool_calls`：工具调用与结果/错误完整
- [ ] `agent_messages`：消息序列完整
- [ ] `decision_records`：置信度与证据链有数据
- [ ] `review_tasks`：审核动作与评论可追溯
- [ ] `error_logs`：异常场景可追踪

### 5.5 关键 SQL 检查模板（验收时执行）

```sql
-- 1) 查看某会话主状态
SELECT session_id, workflow_type, status, created_at, updated_at
FROM workflow_sessions
WHERE session_id = :session_id;

-- 2) 查看执行链完整性
SELECT executor_name, status, started_at, completed_at
FROM agent_executions
WHERE session_id = :session_id
ORDER BY started_at;

-- 3) 查看审核记录
SELECT task_id, status, reviewer_comment, reviewed_at
FROM review_tasks
WHERE session_id = :session_id
ORDER BY created_at DESC;

-- 4) 查看证据链
SELECT decision_type, confidence, evidence
FROM decision_records dr
JOIN agent_executions ae ON dr.execution_id = ae.execution_id
WHERE ae.session_id = :session_id;

-- 5) 查看错误日志（若有）
SELECT error_type, error_message, created_at
FROM error_logs
WHERE session_id = :session_id
ORDER BY created_at DESC;
```

---

## 6. 测试通过判定（Release Gate）

满足以下全部条件，判定“通过”：

1. **功能全绿**：4.1 全部勾选  
2. **性能达标**：4.2 全部勾选  
3. **安全达标**：4.3 全部勾选  
4. **联调通过**：5.2 四条主流程全部跑通  
5. **可观测性通过**：5.3、5.4 全部勾选  
6. **测试覆盖率通过**：>= 80%

---

## 7. 建议的执行顺序（最小风险路径）

1. 先做 M0/M1（基础与数据）
2. 再做 M3 + M5（先把后端工作流闭环打通）
3. 接着做 M6（前端全流程可操作）
4. 然后做 M4/M7（补齐配置优化与自动抓取）
5. 最后做 M8（真实联调 + 性能安全验收）

---

## 8. 交付物清单

- [ ] 任务分解清单（本文档）
- [ ] 各模块实现代码与迁移脚本
- [ ] 测试报告（单元/集成/E2E）
- [ ] 真实联调验收报告（含日志截图 + 数据库查询结果）
- [ ] 发布前检查表（Release Gate）

---

## 9. 执行纪律（提交与推送）

- 每完成一个可独立验收任务（如 Mx-yy），必须在同一工作批次内完成：
  1. 相关测试执行并通过（至少覆盖本任务影响范围）
  2. 代码审查完成（涉及安全边界时追加安全审查）
  3. 创建 commit（commit message 必须包含任务编号）
  4. push 到远端分支
- 禁止多任务堆积后一次性提交与推送（紧急修复除外，需在提交说明中标注原因）
- Release Gate 前必须完成任务到提交的追溯映射：`Task -> Commit -> Push` 可核对

