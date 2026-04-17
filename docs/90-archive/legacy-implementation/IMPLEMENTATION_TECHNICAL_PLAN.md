# DbOptimizer 技术实施方案

**目的**: 定义本轮补齐工作的技术路线，说明每个目标用什么技术实现、核心点是什么、要注意什么。  
**约束**: 基于当前仓库已有实现增量推进，不先引入大规模重构。

---

## 1. 当前技术判断

### 1.1 当前可复用的基础

- 后端已有工作流执行框架
- SQL 调优主链已有基础实现
- 配置调优已有执行器原型
- 慢 SQL 已有采集与落库
- 前端已有单页工作台和基础 API 封装

### 1.2 当前最主要的技术问题

1. 工作流结果模型按 SQL 专用类型硬编码
2. 配置调优工作流虽存在，但状态/历史/审核没有打通
3. 慢 SQL 采集与 SQL 分析工作流是断开的
4. SQL Rewrite 只有数据结构，没有执行逻辑
5. 前端文档中的技术栈并未真正落地，短期不适合按文档直接重建

---

## 2. 技术路线总原则

1. **先打通闭环**
   - 先做“能用”
   - 再做“更好看”“更智能”

2. **复用现有架构**
   - 继续用现有 Minimal API、EF Core、WorkflowRunner、Vue 单页
   - 不在 P0 阶段引入 MAF、OpenAI SDK、大型状态管理重构

3. **结果模型统一抽象**
   - 所有工作流结果都通过统一外壳返回
   - 具体 payload 按 workflowType 分流

4. **任务边界小而清晰**
   - 每次只补一个闭环缺口
   - 控制单任务修改文件数，避免 AI 上下文膨胀

---

## 3. 目标一：实例调优闭环

## 3.1 要实现什么

- 前端可发起 `DbConfigOptimization`
- 后端能返回配置调优结果
- 审核页能审核配置调优建议
- 历史页能查看配置调优任务详情

## 3.2 用什么技术实现

后端:
- 继续使用现有 `ConfigCollectorExecutor / ConfigAnalyzerExecutor / ConfigCoordinatorExecutor / ConfigReviewExecutor`
- 使用现有 Minimal API 路由
- 用统一结果封装替代 `OptimizationReport` 硬编码

前端:
- 继续使用现有 `App.vue + api.ts`
- 第一阶段不引入 Router / Pinia / Element Plus
- 以现有工作台增加一个 `db-config` 视图即可

## 3.3 核心设计点

### A. 统一工作流结果封装

建议形态:

```csharp
public sealed record WorkflowResultEnvelope(
    string ResultType,
    JsonElement Payload);
```

推荐 `ResultType`:

- `SqlOptimizationReport`
- `ConfigOptimizationReport`

核心原因:
- 避免 API、审核、历史都写死为 `OptimizationReport`
- 保证未来再加结果类型时不继续扩散硬编码

### B. 工作流元数据集中定义

建议新增统一定义:

```csharp
WorkflowType -> Steps -> ResultType -> DisplayName
```

至少覆盖:
- `SqlAnalysis`
- `DbConfigOptimization`

用途:
- 统一计算进度
- 统一返回结果类型
- 前端统一显示流程名

## 3.4 要注意什么

- 不要把配置调优也强行转换成 SQL 优化报告
- 不要在前端用“猜字段”的方式兼容两种结果
- 不要为了这个任务直接重写整个前端结构

---

## 4. 目标二：慢 SQL 自动分析闭环

## 4.1 要实现什么

- 慢 SQL 定时采集后自动创建 SQL 分析任务
- 可查询最近慢 SQL
- 能从慢 SQL 追踪到对应分析任务

## 4.2 用什么技术实现

- 保留现有 `SlowQueryCollectionService`
- 保留 `SlowQueryRepository`
- 抽出工作流提交接口供 Infrastructure 层调用
- 复用已有 `ScheduleSqlAnalysisAsync` 对应能力，不新建第二套分析工作流

## 4.3 核心设计点

### A. 触发点放在采集服务后半段

流程建议:

1. Collect raw slow query
2. Normalize
3. Save to `slow_queries`
4. Check whether analysis is needed
5. Submit SQL analysis workflow

### B. 必须有去重和冷却窗口

至少按以下维度去重:

- `query_hash`
- `database_id`
- 时间窗口

否则一个热点慢 SQL 会不断刷分析任务。

### C. 提交接口应位于可复用应用层

不建议:
- `SlowQueryCollectionService` 直接引用 API 路由类

建议:
- 抽象 `IWorkflowSubmissionService`

## 4.4 要注意什么

- 自动分析不能影响采集主链稳定性
- 自动分析失败时，采集服务不能整体失败
- 任务去重要清晰，否则会制造噪音

---

## 5. 目标三：SQL Rewrite 能力

## 5.1 要实现什么

- 对常见低质量 SQL 输出可执行的 rewrite 建议

## 5.2 用什么技术实现

- 第一版使用规则引擎
- 输入复用 `ParsedSqlResult + ExecutionPlanResult`
- 输出写入现有 `SqlRewriteSuggestions`

## 5.3 核心设计点

第一版建议覆盖以下规则:

1. `SELECT *`
2. 函数包裹索引列
3. 非必要排序
4. 无前置过滤的大 JOIN
5. `LIKE '%xxx'`

建议新增组件:

- `ISqlRewriteRecommendationGenerator`
- `SqlRewriteRecommendationGenerator`

由 `CoordinatorExecutor` 统一汇总到最终结果。

## 5.4 要注意什么

- 第一版不要追求“自动重写 SQL”
- 只给建议，不直接改写原 SQL
- 每条建议应附理由和置信度

---

## 6. 目标四：Dashboard 与趋势能力

## 6.1 要实现什么

- 慢 SQL 趋势图
- 基础阈值告警

## 6.2 用什么技术实现

- 基于现有 `slow_queries` 表做聚合
- 增加新的 Dashboard API
- 前端先用现有页面结构承载

## 6.3 核心设计点

推荐新增 API:

- `GET /api/slow-queries`
- `GET /api/dashboard/slow-query-trend`
- `GET /api/dashboard/slow-query-alerts`

建议先不引入 ECharts，第一阶段可以先输出列表或简单图块；如果后面确认要追齐文档，再上图表库。

## 6.4 要注意什么

- 不要因为文档写了 ECharts 就先把可视化框架改掉
- 先把数据 API 做稳定
- 再决定是否增强图表能力

---

## 7. 目标五：文档与实现收敛

## 7.1 要实现什么

- 让文档描述与实际交付阶段保持一致

## 7.2 用什么方式实现

- 将“已实现 / 当前实现 / 规划中”分层标注
- 对尚未实现的 MAF、AI Provider、多 Agent、完整前端栈做阶段性降级说明

## 7.3 核心设计点

- 文档不是愿景墙，要能指导开发
- 短期不做的能力必须标清楚“非本轮目标”

---

## 8. 跨任务通用注意事项

## 8.1 后端注意事项

- 结果模型不要继续在 API 各处重复判断
- 任何新增 API 都尽量复用已有 Query/Application Service
- 不要把 UI 专用逻辑塞回领域模型

## 8.2 前端注意事项

- P0/P1 先保留单页模式
- 单任务尽量只改 `App.vue + api.ts`
- 等闭环跑通后，再考虑组件拆分

## 8.3 Agent / AI 执行注意事项

- 每个任务只读取必要文件，避免上下文过大
- 做完必须写摘要:
  - 修改了哪些文件
  - 当前完成了什么
  - 如何验证
  - 下一步建议做什么
- 如果任务超过 4-6 个文件，先做中间总结再继续

## 8.4 质量注意事项

- 每个任务必须有对应验证动作
- 没跑到的验证必须明确写出
- 不要在一个任务里同时做“重构 + 新功能 + 文档收敛”

---

## 9. 推荐阶段策略

### P0 策略

- 只做闭环
- 不做大重构
- 不做大技术栈切换

### P1 策略

- 在闭环可用基础上增强能力

### P2 策略

- 再考虑 AI / 多 Agent / UI 框架补齐

---

## 10. 本轮不建议立即做的事

- 不建议先引入 Microsoft Agent Framework
- 不建议先接 Azure OpenAI / Claude 正式能力
- 不建议先按文档重建为 Router + Pinia + Element Plus + Monaco + ECharts
- 不建议先拆全量前端文件结构

原因:
- 这些都不是当前最短闭环路径
- 会显著增加 AI 上下文、改动面和回归风险
