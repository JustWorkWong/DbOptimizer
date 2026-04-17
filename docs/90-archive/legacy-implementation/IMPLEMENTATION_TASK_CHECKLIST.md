# DbOptimizer 实施任务清单

**目的**: 将目标拆成可直接执行的任务卡片。  
**使用方式**: 一次只执行一个任务；每个任务结束后必须输出摘要、修改文件列表、验证结果。

---

## 1. 使用规则

1. 一次只领取一个任务
2. 每个任务都要写“已修改文件”
3. 每个任务都要做最小验证
4. 每个任务完成后要写简短 handoff 摘要

---

## 2. Sprint-A: 实例调优闭环

### TASK-A1 统一工作流结果模型

目标:
- 支持 SQL 调优与配置调优返回不同结果类型

技术:
- C#
- Minimal API
- 统一结果封装 DTO

核心点:
- API 层不再写死 `OptimizationReport`
- `workflowType` 与 `resultType` 一一对应

要修改的文件:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Api/ReviewApi.cs`
- 可新增 `src/DbOptimizer.API/Api/WorkflowResultEnvelope.cs`

实现功能:
- `SqlAnalysis` 返回 SQL 优化结果
- `DbConfigOptimization` 返回配置调优结果

验证方式:
- 查询 SQL 调优 session
- 查询配置调优 session
- 查询 review/history 接口

完成标准:
- 配置调优结果不再被当成 SQL 优化报告解析

依赖:
- 无

### TASK-A2 修正工作流进度定义

目标:
- 不同工作流按不同步骤数计算进度

技术:
- C#
- 工作流元数据映射

核心点:
- SQL 调优 6 步
- 配置调优 4 步

要修改的文件:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- 可新增 `src/DbOptimizer.Infrastructure/Workflows/Core/WorkflowDefinitions.cs`

实现功能:
- 进度准确反映工作流真实执行阶段

验证方式:
- 构造不同阶段 checkpoint
- 检查 progress 计算结果

完成标准:
- `DbConfigOptimization` 不再按 6 步显示进度

依赖:
- TASK-A1 推荐先完成

### TASK-A3 前端新增配置调优 API 与类型

目标:
- 前端可调用配置调优工作流

技术:
- TypeScript
- Vue API 封装

核心点:
- 类型定义清晰
- 与后端请求契约对齐

要修改的文件:
- `src/DbOptimizer.Web/src/api.ts`

实现功能:
- 新增 `createDbConfigOptimization()`
- 新增配置调优 payload 和 result 类型

验证方式:
- TypeScript 编译通过
- 请求体字段与后端一致

完成标准:
- 前端具备配置调优接口能力

依赖:
- TASK-A1

### TASK-A4 前端新增实例调优视图

目标:
- 在当前工作台中加入实例调优入口

技术:
- Vue 3 Composition API
- 继续使用当前单页结构

核心点:
- 先闭环，不先重构整个前端架构

要修改的文件:
- `src/DbOptimizer.Web/src/App.vue`

实现功能:
- 新增 `db-config` 视图
- 支持输入 `databaseId`、`databaseType`
- 可发起配置调优任务

验证方式:
- UI 中可进入实例调优区域
- 点击提交能创建任务

完成标准:
- 用户可从前端发起配置调优

依赖:
- TASK-A3

### TASK-A5 审核页兼容配置调优结果

目标:
- 配置调优建议能被审核

技术:
- Vue 3
- 条件渲染

核心点:
- 按 `taskType` 或 `resultType` 分支渲染
- 不用 SQL 的 UI 模型硬套配置建议

要修改的文件:
- `src/DbOptimizer.Web/src/App.vue`
- 如需拆分可新增组件

实现功能:
- 配置调优建议列表可读
- 支持 approve / reject / adjust

验证方式:
- 创建待审核配置任务
- 在 review 视图中打开并提交审核

完成标准:
- 审核流程可完整执行

依赖:
- TASK-A1
- TASK-A4

### TASK-A6 历史页兼容配置调优结果

目标:
- 历史页能正确展示配置调优任务

技术:
- C# 查询层
- Vue 条件展示

核心点:
- 推荐数量统计不能只看索引建议
- 历史详情渲染要区分工作流类型

要修改的文件:
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.Web/src/App.vue`

实现功能:
- 配置调优任务可在历史列表和详情页查看

验证方式:
- 完成一个配置调优任务
- 在历史页查看详情

完成标准:
- 历史页不会错误展示配置调优结果

依赖:
- TASK-A1

---

## 3. Sprint-B: 慢 SQL 自动分析闭环

### TASK-B1 抽出工作流提交接口

目标:
- 让慢 SQL 服务可复用 SQL 分析任务创建能力

技术:
- C#
- 应用服务抽象

核心点:
- Infrastructure 不直接依赖 API 路由层

要修改的文件:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Program.cs`
- 可新增 `src/DbOptimizer.Infrastructure/Workflows/Services/WorkflowSubmissionService.cs`

实现功能:
- 提供统一 `SubmitSqlAnalysisAsync()`

验证方式:
- 单独调用该服务能创建 session

完成标准:
- 慢 SQL 自动分析具备可调用入口

依赖:
- TASK-A1 推荐先完成

### TASK-B2 慢 SQL 采集后自动触发分析

目标:
- 慢 SQL 采集后自动创建分析任务

技术:
- C#
- BackgroundService
- 去重策略

核心点:
- 自动分析不影响采集主链稳定性
- 要有冷却窗口

要修改的文件:
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs`
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryRepository.cs`
- 如需扩展则修改 `SlowQueryEntity`

实现功能:
- 慢 SQL 自动进入现有 SQL 调优工作流

验证方式:
- 造慢 SQL
- 等待采集
- 检查 `slow_queries` 与 `workflow_sessions`

完成标准:
- 不会无休止重复创建分析任务

依赖:
- TASK-B1

### TASK-B3 新增慢 SQL 查询 API

目标:
- 前端可查询最近慢 SQL

技术:
- Minimal API
- EF Core 查询

核心点:
- 返回结构稳定
- 支持按数据库和时间排序

要修改的文件:
- 新增 `src/DbOptimizer.API/Api/SlowQueryApi.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryRepository.cs`

实现功能:
- 返回最近慢 SQL 列表

验证方式:
- 调用 API 并检查返回数据

完成标准:
- API 可稳定查询慢 SQL 数据

依赖:
- 无

---

## 4. Sprint-C: SQL Rewrite 与 Dashboard 增强

### TASK-C1 新增 SQL Rewrite 规则引擎

目标:
- 输出真实 SQL Rewrite 建议

技术:
- C#
- 规则引擎

核心点:
- 先规则版，不先 AI 化
- 输出建议 + 原因 + 置信度

要修改的文件:
- 新增 `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Domain/SqlRewrite/SqlRewriteRecommendationGenerator.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Executors/CoordinatorExecutor.cs`

实现功能:
- 结果中出现 `SqlRewriteSuggestions`

验证方式:
- 用 `SELECT *`
- 用 `LIKE '%abc'`
- 用列函数过滤

完成标准:
- 至少 3 类规则能稳定输出建议

依赖:
- 无

### TASK-C2 新增慢 SQL 趋势 API

目标:
- Dashboard 拿到趋势数据

技术:
- Minimal API
- 聚合查询

核心点:
- 先做数据 API
- 图表渲染放在前端任务中处理

要修改的文件:
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- 如需新增查询类则新增服务文件

实现功能:
- 返回慢 SQL 趋势点

验证方式:
- 插入多时段样本数据
- 调用趋势接口

完成标准:
- API 数据结构稳定可消费

依赖:
- TASK-B3 推荐先完成

### TASK-C3 前端增加慢 SQL 趋势展示

目标:
- 前端可展示慢 SQL 趋势

技术:
- Vue 3
- 当前页面结构

核心点:
- 先展示数据，不强依赖图表库

要修改的文件:
- `src/DbOptimizer.Web/src/App.vue`
- `src/DbOptimizer.Web/src/api.ts`

实现功能:
- dashboard 区域展示慢 SQL 趋势

验证方式:
- 打开页面能看到趋势数据

完成标准:
- API 数据能拉取并展示

依赖:
- TASK-C2

---

## 5. Sprint-D: 文档与实现收敛

### TASK-D1 文档收敛

目标:
- 将文档标成可执行版本，而不是愿景版本

技术:
- Markdown 文档整理

核心点:
- 区分“已实现 / 当前实现 / 规划中”

要修改的文件:
- `docs/REQUIREMENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/FRONTEND_ARCHITECTURE.md`
- `docs/TASK_LIST.md`
- 本轮新增实施文档

实现功能:
- 文档与代码边界一致

验证方式:
- 再次做 docs vs code 对照

完成标准:
- 文档不再把未交付能力写成已交付

依赖:
- 前面阶段完成后执行最佳

---

## 6. 推荐执行顺序

1. TASK-A1
2. TASK-A2
3. TASK-A3
4. TASK-A4
5. TASK-A5
6. TASK-A6
7. TASK-B1
8. TASK-B2
9. TASK-B3
10. TASK-C1
11. TASK-C2
12. TASK-C3
13. TASK-D1

---

## 7. 每个任务完成后的固定输出

每次完成任务后，必须输出：

1. 修改文件列表
2. 完成了什么功能
3. 如何验证
4. 实际验证结果
5. 未完成事项
6. 推荐下一个任务
