# DbOptimizer 文档 vs 代码差异分析与补齐方案

> 执行入口请优先使用 [IMPLEMENTATION_INDEX.md](./IMPLEMENTATION_INDEX.md)。  
> 本文档保留为“差异分析总表”和背景参考，不再作为唯一实施文档。

**项目**: DbOptimizer  
**更新时间**: 2026-04-17  
**分析范围**: `docs/` 与 `src/` 当前实现  
**结论**: 当前代码整体更接近“SQL 调优后端骨架 + 单页前端 + 部分配置调优原型”，与文档描述的完整产品版仍有明显差距。

---

## 1. 分析结论摘要

### 1.1 当前完成度判断

- **SQL 调优主链**: 已有可运行的简化版后端实现
- **数据库实例调优**: 仅完成后端部分原型，前端未落地，结果类型未打通，不能视为完整实现
- **慢 SQL 自动抓取**: 已实现采集与落库，未实现自动进入分析闭环
- **前端产品化能力**: 文档描述显著超前于代码
- **AI / MAF / 多 Agent 能力**: 文档描述显著超前于代码

### 1.2 最关键差异

1. 文档中的“实例调优页”在前端并不存在。
2. 后端虽然有 `db-config-optimization` 工作流入口，但状态查询、历史查询、审核接口仍按 SQL 优化结果模型处理，配置调优链路没有真正打通。
3. 慢 SQL 目前只做“采集”，没有做“自动分析”。
4. SQL Rewrite 在文档中是正式能力，但代码中只有模型和开关，没有实际生成逻辑。
5. 文档中的前端栈和 AI 栈比当前代码实现超前很多。

---

## 2. 差异清单表

| 模块 | 文档承诺 | 代码现状 | 差异判断 | 关键证据 |
|---|---|---|---|---|
| 数据库实例调优页面 | 有独立“实例调优页”与路由 | 前端只有 `sql / review / history / replay` 四个视图，没有实例调优入口 | 未实现 | `docs/TASK_LIST.md:139` `docs/FRONTEND_ARCHITECTURE.md:279` `src/DbOptimizer.Web/src/App.vue:29` |
| 配置调优前端 API | 前端可发起 `db-config-optimization` 工作流 | 前端只封装了 `createSqlAnalysis()`，没有创建配置调优任务 API | 未实现 | `docs/API_SPEC.md:101` `src/DbOptimizer.Web/src/api.ts:260` |
| 配置调优后端主链 | `ConfigCollector -> ConfigAnalyzer -> Coordinator -> Review` | 后端工作流、执行器、注册均存在 | 部分实现 | `docs/TASK_LIST.md:108` `src/DbOptimizer.API/Api/WorkflowApi.cs:199` `src/DbOptimizer.API/Api/WorkflowApi.cs:243` |
| 配置调优结果展示 | 配置优化结果应在状态页、历史页、审核页正常展示 | 这些接口仍把结果类型写死为 `OptimizationReport`，实际配置链路产出是 `ConfigOptimizationReport` | 后端未打通 | `src/DbOptimizer.API/Api/WorkflowApi.cs:145` `src/DbOptimizer.API/Api/WorkflowApi.cs:534` `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs:147` `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs:552` `src/DbOptimizer.API/Api/ReviewApi.cs:97` `src/DbOptimizer.Infrastructure/Workflows/ConfigOptimization/ConfigOptimizationModels.cs:232` |
| 配置调优审核 | 配置优化也走人工审核闭环 | 审核流程存在，但审核读写模型仍按 SQL 报告处理 | 部分实现 | `docs/TASK_LIST.md:116` `src/DbOptimizer.API/Api/ReviewApi.cs:97` `src/DbOptimizer.API/Api/ReviewApi.cs:485` |
| 配置调优进度 | 各工作流应按各自步骤展示进度 | 进度计算固定按 SQL 工作流 6 步，配置调优实际只有 4 步 | 实现不正确 | `src/DbOptimizer.API/Api/WorkflowApi.cs:199` `src/DbOptimizer.API/Api/WorkflowApi.cs:519` `src/DbOptimizer.API/Api/WorkflowApi.cs:576` |
| SQL Rewrite | 文档包含 SQL 重写建议 | 只有模型和开关，无实际生成逻辑 | 未实现 | `docs/REQUIREMENTS.md:76` `src/DbOptimizer.API/Api/WorkflowApi.cs:132` `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Models/OptimizationReportModels.cs:12` `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Executors/CoordinatorExecutor.cs:74` |
| 慢 SQL 自动分析闭环 | 慢 SQL 自动抓取后自动进入 SQL 分析工作流 | 当前仅做采集、标准化、落库，没有触发分析 | 部分实现 | `docs/REQUIREMENTS.md:86` `docs/TASK_LIST.md:157` `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs:13` `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs:85` |
| 慢 SQL 趋势图和告警 | Dashboard 有慢 SQL 趋势与阈值管理 | 后端仅有通用 dashboard stats，无慢 SQL 趋势/告警 API；前端也无对应页面 | 未实现 | `docs/TASK_LIST.md:171` `docs/FRONTEND_DETAIL_DESIGN.md:206` `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs:1` |
| PromptVersion 管理 | 有 PromptVersion UI、启停、回滚 | 数据库表存在，但没有 API、业务服务、前端入口 | 仅表结构存在 | `docs/TASK_LIST.md:170` `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs:26` `src/DbOptimizer.Infrastructure/Persistence/DbOptimizerDbContext.cs:208` |
| 前端技术栈 | Element Plus、Pinia、Vue Router、Monaco、ECharts | 实际前端依赖只有 `vue` 与 Vite 基础工具链 | 文档超前 | `docs/REQUIREMENTS.md:40` `docs/FRONTEND_ARCHITECTURE.md:122` `src/DbOptimizer.Web/package.json:11` |
| AI / MAF / 多 Agent | MAF、Azure OpenAI、Claude、多 Agent 协作 | 当前项目依赖中未见对应 SDK 或框架接入 | 文档超前 | `docs/REQUIREMENTS.md:14` `docs/ARCHITECTURE.md:145` `src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj:9` |
| SQL 输入安全校验 | 文档要求阻断明显 SQL 注入输入 | 当前仅校验 SQL 非空，没有明显危险语句或输入边界校验 | 未达到文档口径 | `docs/REQUIREMENTS.md:263` `src/DbOptimizer.API/Api/WorkflowApi.cs:214` |

---

## 3. 差异分类

### 3.1 已有后端骨架，但前后端未打通

- 数据库实例调优工作流
- 配置调优审核展示
- 配置调优状态与历史查询

### 3.2 已做基础能力，但未形成闭环

- 慢 SQL 自动抓取
- 回放与历史查询

### 3.3 文档已承诺，但代码尚未开始

- 实例调优前端页面
- SQL Rewrite 生成逻辑
- 慢 SQL 趋势图与告警
- PromptVersion 管理 UI 与接口
- 规范化前端架构栈

### 3.4 文档技术方案明显超前

- Microsoft Agent Framework
- Azure OpenAI / Anthropic Claude 正式接入
- 多 Agent 协作链路
- Element Plus / Pinia / Vue Router / Monaco / ECharts 完整前端架构

---

## 4. 推荐实现顺序

建议按以下优先级推进，优先补齐“用户可感知闭环”，再补“增强能力”和“文档超前项”。

### P0: 先打通实例调优闭环

目标:
- 后端配置调优工作流能正常运行
- 前端能发起配置调优任务
- 配置调优结果能在状态页、审核页、历史页正确展示

### P1: 再补慢 SQL 自动分析闭环

目标:
- 慢 SQL 采集后自动创建 SQL 分析任务
- 前端可查看慢 SQL 采集结果与分析结果

### P2: 再补 SQL Rewrite 与 Dashboard 能力

目标:
- SQL 重写建议形成真实输出
- Dashboard 支持慢 SQL 趋势与基础告警

### P3: 最后处理文档超前项

目标:
- 决定是否真的引入 MAF / AI Provider / 多 Agent / 完整前端栈
- 若短期不做，则回写文档，降低承诺范围

---

## 5. 实现要点

本节给出后续补齐工作时应坚持的实现要点，避免出现“功能补上了，但结构继续发散”的情况。

### 5.1 统一结果模型，不要继续按 SQL 专用模型硬编码

- 当前最大问题不是没有配置调优执行器，而是 API、审核、历史查询都把结果当成 `OptimizationReport`
- 后续必须把“工作流类型”与“结果类型”绑定起来
- 推荐做法是建立统一工作流结果外壳，例如:
  - `WorkflowResultEnvelope`
  - `WorkflowResultType`
  - `Payload`
- API 层只负责分发和序列化，不再假设所有结果都是 SQL 优化报告

### 5.2 工作流元数据要集中管理

- 当前 SQL 与配置调优的步骤数、执行器顺序、进度计算分散在多处
- 后续应引入统一元数据定义，例如:
  - `WorkflowDefinition`
  - `WorkflowStepDefinition`
- 至少需要集中管理:
  - 工作流类型
  - 执行器顺序
  - 总步数
  - 默认展示名称
  - 结果类型

### 5.3 前端先补“闭环”，后补“美化”

- 当前最缺的是可用闭环，不是 UI 框架
- 前端第一阶段不必先引入 Element Plus / Pinia / Router 重构
- 优先做:
  - 能发起配置调优任务
  - 能查看任务进度
  - 能审核
  - 能查历史

### 5.4 慢 SQL 先接入现有 SQL 分析主链，不另造新链路

- 已有 `ScheduleSqlAnalysisAsync()` 和 SQL 分析工作流
- 慢 SQL 自动分析应复用现有入口
- 避免再建第二套“慢 SQL 专用分析工作流”

### 5.5 SQL Rewrite 先做规则版，再决定是否引入 AI

- 当前代码没有 OpenAI / Claude / MAF 正式依赖
- 因此 SQL Rewrite 第一版建议采用规则引擎
- 优先覆盖高价值规则:
  - `SELECT *`
  - 非必要排序
  - 大表扫描下的宽列读取
  - 可提前过滤的 JOIN
  - 非 SARGable 条件

### 5.6 文档口径要与代码同步收敛

- 如果短期不引入 MAF / Azure OpenAI / Claude / 多 Agent / Element Plus / Monaco / ECharts
- 就应同步回写文档，标注为“规划中”或“P2/P3”
- 否则后续 AI 和人都会按错误边界继续开发

---

## 6. 技术方案

本节给出推荐技术方案，重点覆盖 P0/P1 任务，确保后续 AI 可按统一思路推进。

## 6.1 配置调优闭环技术方案

### 方案目标

- 保留现有 `DbConfigOptimization` 执行器链
- 打通 API 返回、审核展示、历史展示、前端发起入口
- 不重做工作流引擎

### 方案设计

#### A. 引入统一工作流结果封装

建议新增统一返回结构:

```csharp
public sealed record WorkflowResultEnvelope(
    string ResultType,
    JsonElement Payload);
```

或更强类型的泛型外壳:

```csharp
public sealed record WorkflowResultEnvelope<TPayload>(
    string ResultType,
    TPayload Payload);
```

建议的 `ResultType` 值:

- `SqlOptimizationReport`
- `ConfigOptimizationReport`

### B. API 层按 workflowType 做反序列化分发

涉及接口:

- `GET /api/workflows/{sessionId}`
- `GET /api/history/{sessionId}`
- `GET /api/reviews/{taskId}`
- `GET /api/reviews`

做法:

- 从 `workflowType` 或 `taskType` 推断结果模型
- SQL 任务按 `OptimizationReport` 解析
- 配置调优任务按 `ConfigOptimizationReport` 解析

### C. 前端按结果类型切换视图

建议新增统一前端结果结构:

```ts
type WorkflowResult =
  | { resultType: 'SqlOptimizationReport'; payload: OptimizationReport }
  | { resultType: 'ConfigOptimizationReport'; payload: ConfigOptimizationReport }
```

前端渲染逻辑:

- SQL 任务展示索引建议、SQL rewrite、证据链
- 配置任务展示参数名、当前值、建议值、影响级别、是否需重启

### D. 进度定义按工作流类型分开

建议新增静态定义表:

```csharp
SqlAnalysis = 6 steps
DbConfigOptimization = 4 steps
```

通过 `workflowType -> totalSteps` 统一映射，不再写死为 6 步。

---

## 6.2 慢 SQL 自动分析闭环技术方案

### 方案目标

- 保留当前 `slow_queries` 落库逻辑
- 在采集完成后自动复用现有 SQL 分析工作流
- 避免高频重复创建分析任务

### 方案设计

#### A. 抽出基础调度接口到 Infrastructure

当前调度入口在 API 层:

- `IWorkflowExecutionScheduler`

建议做法:

- 将“创建工作流”接口抽成可跨层调用的应用服务接口
- `SlowQueryCollectionService` 直接依赖这个抽象

推荐接口示意:

```csharp
public interface IWorkflowSubmissionService
{
    Task<Guid> SubmitSqlAnalysisAsync(
        string sqlText,
        string databaseId,
        string databaseEngine,
        CancellationToken cancellationToken = default);
}
```

#### B. 增加慢 SQL 去重与分析状态控制

建议新增字段或元数据:

- 最近分析时间
- 最近分析 sessionId
- 分析状态

实现思路:

- 同一 `query_hash + database_id` 在固定窗口内不重复触发
- 只有首次发现或超过冷却窗口才创建新分析任务

#### C. 自动分析的触发时机

放在 `SlowQueryCollectionService` 内:

1. 采集原始慢 SQL
2. 规范化
3. 保存到 `slow_queries`
4. 判断是否需要自动分析
5. 复用 `SubmitSqlAnalysisAsync()`

---

## 6.3 SQL Rewrite 技术方案

### 方案目标

- 不依赖 AI 也能输出第一版 rewrite 建议
- 与现有 `ParsedSqlResult`、`ExecutionPlanResult` 对齐

### 方案设计

建议新增:

- `ISqlRewriteRecommendationGenerator`
- `SqlRewriteRecommendationGenerator`

输入:

- `ParsedSqlResult`
- `ExecutionPlanResult`

输出:

- `List<SqlRewriteSuggestion>`

第一版规则建议:

- `SELECT *` -> 改为显式列
- `ORDER BY` 与筛选场景不匹配 -> 建议移除或改索引顺序
- `LIKE '%xxx'` -> 提示无法用普通索引
- 条件函数包裹列 -> 提示改为可走索引表达式
- JOIN 之前未做过滤 -> 提示先过滤后关联

接入点:

- `CoordinatorExecutor` 汇总结果时写入 `SqlRewriteSuggestions`

---

## 6.4 Dashboard 与趋势分析技术方案

### 方案目标

- 在现有 dashboard API 上做增量扩展
- 先做“慢 SQL 趋势”与“基础阈值告警”

### 方案设计

建议新增 API:

- `GET /api/slow-queries`
- `GET /api/dashboard/slow-query-trend`
- `GET /api/dashboard/slow-query-alerts`

数据来源:

- `slow_queries`

聚合维度:

- 按天
- 按小时
- 按数据库

基础告警规则:

- `avg_execution_time > threshold`
- `execution_count > threshold`
- `max_execution_time > threshold`

---

## 7. 开发计划

本节给出建议的开发计划，按“最小可验收闭环”组织，而不是按文档原始大而全承诺推进。

## 7.1 Sprint-A: 实例调优闭环

目标:

- 配置调优后端结果模型打通
- 前端新增实例调优入口
- 审核/历史能正确展示配置调优结果

建议输出:

- 用户可从前端发起配置调优任务
- 配置调优任务能完整走到审核与历史

## 7.2 Sprint-B: 慢 SQL 自动分析闭环

目标:

- 慢 SQL 采集后自动创建 SQL 分析任务
- 可查询慢 SQL 列表

建议输出:

- 慢 SQL 自动分析形成闭环

## 7.3 Sprint-C: SQL Rewrite 与 Dashboard 增强

目标:

- SQL Rewrite 从占位变为真实输出
- Dashboard 增加慢 SQL 趋势与基础告警

建议输出:

- SQL 优化结果更接近文档承诺
- Dashboard 开始具备“运维视角”能力

## 7.4 Sprint-D: 文档与实现收敛

目标:

- 决定是否引入 MAF / AI Provider / 完整前端栈
- 将未做功能标注为规划中或后续阶段

---

## 8. 任务列表与 Checklist

本节用于后续 AI 排任务。建议每个任务单独执行、单独验证、单独提交，减少跨任务耦合。

## 8.1 Sprint-A 任务列表

### TASK-A1: 统一工作流结果模型

目标:

- 为 SQL 调优与配置调优提供统一结果封装

需要做什么:

- 设计统一结果 DTO 或结果封装
- 调整工作流状态查询接口结果结构
- 保证 API 层能按工作流类型处理不同结果

主要修改文件:

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Api/ReviewApi.cs`
- 如需要可新增:
  - `src/DbOptimizer.API/Api/WorkflowResultEnvelope.cs`

实现的功能:

- `SqlAnalysis` 与 `DbConfigOptimization` 都能返回正确结果类型

如何验证:

- 构造一个 SQL 调优 session，调用 `GET /api/workflows/{sessionId}`
- 构造一个配置调优 session，调用同接口
- 确认两者结果结构都正确
- 确认历史与审核接口不再错误反序列化

完成标准:

- 配置调优结果不再按 `OptimizationReport` 强制解析
- 历史接口和审核接口可返回正确结果

依赖:

- 无

### TASK-A2: 修正配置调优进度计算

目标:

- 让不同工作流使用不同总步数

需要做什么:

- 提取工作流步骤配置
- 修改 `CalculateProgress`
- 为配置调优设置 4 步

主要修改文件:

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- 如需要可新增:
  - `src/DbOptimizer.Infrastructure/Workflows/Core/WorkflowDefinitions.cs`

实现的功能:

- 配置调优任务进度准确显示

如何验证:

- 模拟配置调优任务执行到第 1、2、3、4 步
- 检查进度分别接近 25、50、75、100

完成标准:

- SQL 调优进度不回归
- 配置调优进度不再按 6 步计算

依赖:

- TASK-A1 推荐先完成，但不是强依赖

### TASK-A3: 前端新增配置调优 API

目标:

- 前端能够创建配置调优任务

需要做什么:

- 在 `api.ts` 中新增 `createDbConfigOptimization()`
- 定义 `CreateDbConfigOptimizationPayload`
- 定义配置调优结果类型

主要修改文件:

- `src/DbOptimizer.Web/src/api.ts`

实现的功能:

- 前端有配置调优任务创建能力

如何验证:

- 前端代码类型检查通过
- 调用新 API 时请求体结构与后端一致

完成标准:

- `api.ts` 能支持配置调优主链

依赖:

- TASK-A1

### TASK-A4: 前端新增实例调优视图

目标:

- 用户可在 UI 中发起实例调优任务

需要做什么:

- 给 `App.vue` 增加 `db-config` 视图
- 增加表单:
  - `databaseId`
  - `databaseType`
- 发起配置调优任务
- 展示执行中与结果态

主要修改文件:

- `src/DbOptimizer.Web/src/App.vue`

实现的功能:

- 前端具备实例调优操作入口

如何验证:

- 打开页面能看到实例调优入口
- 点击提交可创建 `DbConfigOptimization` 任务

完成标准:

- 配置调优任务能从前端发起

依赖:

- TASK-A3

### TASK-A5: 审核页兼容配置调优结果

目标:

- 审核页能正确展示配置调优建议

需要做什么:

- 根据任务类型切换审核详情渲染
- SQL 任务显示索引建议
- 配置任务显示参数建议列表

主要修改文件:

- `src/DbOptimizer.Web/src/App.vue`
- 如需要可拆分组件:
  - `src/DbOptimizer.Web/src/components/ConfigRecommendationList.vue`
  - `src/DbOptimizer.Web/src/components/SqlRecommendationList.vue`

实现的功能:

- 配置调优审核可视化

如何验证:

- 创建一个配置调优待审核任务
- 在 review 视图打开
- 检查参数建议是否正确显示
- 提交 approve / reject / adjust

完成标准:

- 配置调优任务能完整审核

依赖:

- TASK-A1
- TASK-A4

### TASK-A6: 历史页兼容配置调优结果

目标:

- 历史页可查看配置调优任务

需要做什么:

- 根据 `workflowType` 切换结果渲染模板
- 修正推荐数量统计逻辑
- 避免配置调优任务显示为 0 条索引建议后失真

主要修改文件:

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.Web/src/App.vue`

实现的功能:

- 配置调优历史可查询、可展示

如何验证:

- 发起配置调优任务并完成审核
- 到 history 视图查看详情

完成标准:

- 配置调优历史详情可读

依赖:

- TASK-A1

---

## 8.2 Sprint-B 任务列表

### TASK-B1: 抽出工作流提交接口

目标:

- 让慢 SQL 采集服务可复用提交 SQL 分析工作流的能力

需要做什么:

- 抽出跨层可调用的工作流提交接口
- 避免 `SlowQueryCollectionService` 直接依赖 API 路由层

主要修改文件:

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Program.cs`
- 如需要新增:
  - `src/DbOptimizer.Infrastructure/Workflows/Services/WorkflowSubmissionService.cs`

实现的功能:

- Infrastructure 可触发 SQL 分析工作流

如何验证:

- 单独调用新服务方法可创建 SQL 调优任务

完成标准:

- 慢 SQL 服务具备自动分析接入点

依赖:

- TASK-A1 推荐先完成

### TASK-B2: 慢 SQL 自动触发分析

目标:

- 采集后自动创建 SQL 分析任务

需要做什么:

- 在 `SlowQueryCollectionService` 中增加自动提交逻辑
- 增加去重策略与冷却窗口

主要修改文件:

- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs`
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryRepository.cs`
- 如需要可扩展 `SlowQueryEntity`

实现的功能:

- 慢 SQL 自动进入现有 SQL 调优闭环

如何验证:

- 制造一条慢 SQL
- 等待采集服务执行
- 检查 `slow_queries`
- 检查 `workflow_sessions`

完成标准:

- 同一条慢 SQL 不会在短时间内无限重复建任务

依赖:

- TASK-B1

### TASK-B3: 新增慢 SQL 查询 API

目标:

- 前端可获取慢 SQL 列表

需要做什么:

- 新增 slow query 列表接口
- 支持按数据库和时间排序

主要修改文件:

- 新增:
  - `src/DbOptimizer.API/Api/SlowQueryApi.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryRepository.cs`

实现的功能:

- 前端可查询最近慢 SQL

如何验证:

- 调用 API 返回慢 SQL 列表
- 字段包含:
  - `databaseId`
  - `queryHash`
  - `avgExecutionTime`
  - `lastSeenAt`

完成标准:

- API 可稳定返回慢 SQL 数据

依赖:

- 无

---

## 8.3 Sprint-C 任务列表

### TASK-C1: 新增 SQL Rewrite 规则引擎

目标:

- 输出真实 SQL Rewrite 建议

需要做什么:

- 新增 rewrite 推荐接口与实现
- 补至少 3-5 类规则

主要修改文件:

- 新增:
  - `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Domain/SqlRewrite/SqlRewriteRecommendationGenerator.cs`
- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Infrastructure/Workflows/SqlOptimization/Executors/CoordinatorExecutor.cs`

实现的功能:

- SQL 分析结果开始包含 rewrite 建议

如何验证:

- 用 `SELECT *`
- 用 `LIKE '%abc'`
- 用列函数过滤
- 确认结果中出现 rewrite suggestions

完成标准:

- 至少一个典型 SQL 能输出 rewrite 建议

依赖:

- 无

### TASK-C2: 新增慢 SQL 趋势 API

目标:

- Dashboard 支持趋势数据

需要做什么:

- 从 `slow_queries` 聚合趋势点
- 增加 dashboard 趋势接口

主要修改文件:

- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- 如需要新增查询服务或 DTO 文件

实现的功能:

- 可返回按天或按小时趋势

如何验证:

- 插入多天样本数据
- 调用趋势接口
- 检查点位与聚合值正确

完成标准:

- 趋势接口返回稳定结构

依赖:

- TASK-B3 推荐先完成

### TASK-C3: 前端 Dashboard 增加慢 SQL 趋势展示

目标:

- 前端可视化慢 SQL 趋势

需要做什么:

- 在当前单页前端中补 dashboard 区域
- 展示趋势数据

主要修改文件:

- `src/DbOptimizer.Web/src/App.vue`
- `src/DbOptimizer.Web/src/api.ts`

实现的功能:

- 用户能查看慢 SQL 趋势

如何验证:

- 打开 dashboard
- 能看到趋势数据

完成标准:

- 趋势数据能从 API 拉取并正确显示

依赖:

- TASK-C2

---

## 8.4 Sprint-D 任务列表

### TASK-D1: 文档收敛与路线确认

目标:

- 判断哪些“文档承诺”保留，哪些降级为后续规划

需要做什么:

- 逐个核对:
  - MAF
  - Azure OpenAI / Claude
  - 多 Agent
  - Element Plus / Pinia / Router / Monaco / ECharts
- 标记为:
  - 已实现
  - 当前实现
  - 规划中

主要修改文件:

- `docs/REQUIREMENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/FRONTEND_ARCHITECTURE.md`
- `docs/TASK_LIST.md`
- 本文档

实现的功能:

- 文档与代码边界一致

如何验证:

- 再做一次 docs vs code 对照

完成标准:

- 文档不再描述明显不存在的已交付能力

依赖:

- 前面阶段完成后执行最佳

---

## 9. 任务执行建议

为方便 AI 排任务，建议遵循以下规则:

### 9.1 一次只做一个闭环任务

- 不要把“结果模型统一”和“前端页面重构”和“AI 接入”混成一个任务
- 每个任务都应能独立验证

### 9.2 每个任务都要写明修改范围

建议任务描述必须包含:

- 功能目标
- 修改文件
- 输出结果
- 验证方式
- 完成标准

### 9.3 优先做 P0 和 P1

推荐执行顺序:

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

## 10. AI 可直接使用的任务模板

后续可直接按以下模板给 AI 派发任务:

```markdown
任务ID: TASK-A1
目标: 统一工作流结果模型，打通 SQL 调优与配置调优结果返回

需要做什么:
- 设计统一结果封装
- 调整 workflow/history/review 三类接口的结果反序列化逻辑
- 保证 DbConfigOptimization 不再按 OptimizationReport 解析

修改文件:
- src/DbOptimizer.API/Api/WorkflowApi.cs
- src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs
- src/DbOptimizer.API/Api/ReviewApi.cs

实现功能:
- SQL 与配置调优接口都能返回正确结果结构

验证方式:
- 创建 SQL 调优 session 并查询
- 创建配置调优 session 并查询
- 检查 history/review 接口返回结构

完成标准:
- 配置调优结果展示链路打通
- 无错误反序列化
```

---

## 11. 详细实现步骤

## 5.1 阶段一：补齐实例调优工作流闭环

### Step 1. 定义统一结果模型

目标:
- 解决 `OptimizationReport` 与 `ConfigOptimizationReport` 类型不兼容问题

建议做法:
- 引入统一的工作流结果 DTO，例如 `WorkflowResultDto`
- 或在 `WorkflowStatusResponse` / `HistoryDetailResponse` / `ReviewDetailResponse` 中增加 `resultType`
- 按工作流类型分别序列化和反序列化

涉及位置:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.API/Api/ReviewApi.cs`

验收标准:
- `SqlAnalysis` 与 `DbConfigOptimization` 两类任务都能正确返回结果
- 不再出现配置调优结果按 SQL 报告模型解析的情况

### Step 2. 修正配置调优进度计算

目标:
- 按工作流类型使用不同步骤数

建议做法:
- `SqlAnalysis` 使用 6 步
- `DbConfigOptimization` 使用 4 步
- 进度计算逻辑改为按 `workflowType` 选择步骤定义

涉及位置:
- `src/DbOptimizer.API/Api/WorkflowApi.cs`

验收标准:
- 配置调优任务执行到第 2 步时进度应约为 50%
- SQL 调优进度行为不受影响

### Step 3. 前端增加实例调优入口

目标:
- 用户可在 UI 中发起配置调优任务

建议做法:
- 在 `App.vue` 增加 `db-config` 视图或拆分独立组件
- 在 `api.ts` 中增加 `createDbConfigOptimization()`
- 增加数据库类型、数据库 ID、参数输入表单

涉及位置:
- `src/DbOptimizer.Web/src/App.vue`
- `src/DbOptimizer.Web/src/api.ts`

验收标准:
- UI 能创建配置调优任务
- 能看到进行中的执行状态
- 任务进入待审核后可打开审核详情

### Step 4. 审核与历史页兼容配置调优结果

目标:
- 配置调优任务在审核页、历史页可查看

建议做法:
- 审核列表中展示 `TaskType`
- 当 `TaskType == ConfigOptimization` 时按配置建议卡片渲染
- 历史详情页根据 `workflowType` 切换结果展示模板

涉及位置:
- `src/DbOptimizer.Web/src/App.vue`
- 如后续拆分视图，则分别落到 Review / History 组件

验收标准:
- `ConfigOptimization` 任务能在审核页正常展示
- 审核通过、驳回、调整后状态正确
- 历史页可查看配置建议详情

---

## 5.2 阶段二：补齐慢 SQL 自动分析闭环

### Step 5. 在慢 SQL 采集后自动创建 SQL 分析任务

目标:
- 采集到慢 SQL 后自动进入现有 SQL 分析工作流

建议做法:
- 在 `SlowQueryCollectionService` 中注入工作流调度服务
- 采集成功后对未分析或分析过期的慢 SQL 创建 `SqlAnalysis` 任务
- 增加去重策略，避免同一条指纹高频重复创建任务

涉及位置:
- `src/DbOptimizer.Infrastructure/SlowQuery/SlowQueryCollectionService.cs`
- 可能需要抽出跨层调度接口，避免直接依赖 API 层实现

验收标准:
- 采集到慢 SQL 后，数据库中新增对应 workflow session
- 历史页能看到自动创建的 SQL 分析任务

### Step 6. 补充慢 SQL 查询 API

目标:
- 前端能查看慢 SQL 数据

建议做法:
- 增加 `/api/slow-queries` 列表接口
- 支持按数据库、时间范围、执行时间排序

涉及位置:
- 新增 API 文件
- 复用 `ISlowQueryRepository`

验收标准:
- 能查询最近慢 SQL 列表
- 前端可展示执行次数、平均执行时间、最近出现时间

---

## 5.3 阶段三：补齐 SQL Rewrite 能力

### Step 7. 新增 SQL Rewrite 分析器

目标:
- 不再只输出索引建议，开始输出真实 SQL 改写建议

建议做法:
- 基于 `ParsedSqlResult + ExecutionPlanResult` 添加规则引擎
- 先覆盖几类高价值规则:
  - `SELECT *`
  - 非必要排序
  - 可提前过滤的 JOIN
  - 模糊查询未命中索引

涉及位置:
- 新增 `SqlRewriteAdvisor` 或同类组件
- `CoordinatorExecutor` 汇总其输出

验收标准:
- 对典型示例 SQL 能输出至少 1 条 rewrite 建议
- 前端结果页可展示 rewrite 建议

---

## 5.4 阶段四：补齐 Dashboard 与趋势分析

### Step 8. 增加慢 SQL 趋势 API

目标:
- Dashboard 具备基础趋势图能力

建议做法:
- 基于 `slow_queries` 统计按天或按小时聚合趋势
- 新增 `/api/dashboard/slow-query-trend`

验收标准:
- 返回趋势点数据
- 前端能渲染趋势区域

### Step 9. 增加基础阈值告警

目标:
- 对高频或高耗时慢 SQL 发出提示

建议做法:
- 先做最简版告警规则:
  - 平均执行时间超过阈值
  - 同指纹一小时内执行次数超过阈值

验收标准:
- Dashboard 能显示告警数量
- 可查看告警明细

---

## 12. 验收步骤

## 12.1 功能验收

### Flow-1: SQL 调优闭环

1. 进入 SQL 调优页面
2. 输入一条典型慢 SQL
3. 提交分析
4. 观察工作流进度
5. 查看结果卡片
6. 提交审核
7. 审核通过或驳回
8. 到历史页确认任务状态与结果

通过标准:
- 能成功创建任务
- 能看到执行进度
- 能看到建议结果
- 审核后任务状态变化正确

### Flow-2: 实例调优闭环

1. 进入实例调优页面
2. 选择数据库 ID 与数据库类型
3. 发起配置调优
4. 查看配置建议结果
5. 提交审核动作
6. 到历史页查看配置调优详情

通过标准:
- 配置调优任务能成功创建
- 状态页能显示配置调优结果
- 审核页能正确渲染配置建议
- 历史页能正常查看配置调优任务

### Flow-3: 慢 SQL 自动分析闭环

1. 确保慢 SQL 采集服务启用
2. 在目标数据库制造一条明显慢查询
3. 等待定时采集触发
4. 检查 `slow_queries` 落库
5. 检查是否自动创建 SQL 分析任务
6. 到历史页或 Dashboard 查看分析结果

通过标准:
- 慢 SQL 被采集到
- 自动创建分析任务
- 分析任务结果可查询

---

## 12.2 API 验收

重点验收接口:

- `POST /api/workflows/sql-analysis`
- `POST /api/workflows/db-config-optimization`
- `GET /api/workflows/{sessionId}`
- `GET /api/reviews`
- `GET /api/reviews/{taskId}`
- `POST /api/reviews/{taskId}/submit`
- `GET /api/history`
- `GET /api/history/{sessionId}`
- `GET /api/workflows/{sessionId}/events`
- 后续新增:
  - `GET /api/slow-queries`
  - `GET /api/dashboard/slow-query-trend`

验收标准:
- SQL 调优与配置调优都返回正确结果结构
- 配置调优结果不再误反序列化为 `OptimizationReport`
- 历史与审核接口能区分不同工作流类型

---

## 12.3 数据库验收

建议重点检查以下表:

- `workflow_sessions`
- `agent_executions`
- `tool_calls`
- `review_tasks`
- `error_logs`
- `slow_queries`

建议检查项:

1. SQL 调优任务创建后，`workflow_sessions` 状态正确流转
2. 配置调优任务创建后，`workflow_type` 为 `DbConfigOptimization`
3. `review_tasks.task_type` 能正确区分 `SqlOptimization` 与 `ConfigOptimization`
4. 慢 SQL 采集后，`slow_queries` 正常入库
5. 自动分析启用后，慢 SQL 能关联创建新的 workflow session

---

## 12.4 最小构建验收

建议执行:

```powershell
dotnet build src\DbOptimizer.API\DbOptimizer.API.csproj
dotnet build src\DbOptimizer.AppHost\DbOptimizer.AppHost.csproj
```

```powershell
cd src\DbOptimizer.Web
npm run build
```

如后续补测试，建议执行:

```powershell
dotnet test
```

通过标准:
- API 项目构建通过
- Web 项目构建通过
- 新增接口和前端交互无编译错误

---

## 13. 建议里程碑

### 里程碑 A: 配置调优闭环打通

交付内容:
- 配置调优结果模型打通
- 前端可发起实例调优
- 审核页、历史页可展示配置调优结果

### 里程碑 B: 慢 SQL 自动分析闭环

交付内容:
- 慢 SQL 自动触发 SQL 分析
- 可查询慢 SQL 列表与分析结果

### 里程碑 C: SQL Rewrite 与 Dashboard 增强

交付内容:
- SQL Rewrite 真实建议
- 慢 SQL 趋势图
- 基础阈值告警

### 里程碑 D: 文档与实现对齐

交付内容:
- 对未实现的 MAF / 多 Agent / AI 栈做决策
- 要么实现，要么收缩文档承诺

---

## 14. 建议的验收口径

建议把验收标准分成两层:

### 第一层: 可用闭环

- 用户能发起 SQL 调优
- 用户能发起实例调优
- 用户能审核建议
- 用户能查看历史
- 慢 SQL 能自动进入分析闭环

### 第二层: 增强能力

- 有 SQL Rewrite
- 有慢 SQL 趋势图
- 有告警
- 有 PromptVersion 管理
- 有 AI / MAF / 多 Agent 正式接入

---

## 15. 最终建议

当前最合理的推进方式不是一次性追平所有文档，而是先把以下三件事做成：

1. **实例调优闭环打通**
2. **慢 SQL 自动分析闭环打通**
3. **SQL Rewrite 从占位变为真实能力**

这三项补齐后，`DbOptimizer` 才能从“文档概念版”进入“可演示、可验收的 v1”。

如果短期内不准备引入 MAF、Azure OpenAI、Claude、Element Plus、Pinia、Monaco、ECharts 等能力，建议同步回写文档，避免继续扩大“文档领先代码”的范围。
