# DbOptimizer 实施文档索引

**目的**: 将“文档 vs 代码差异分析”拆分为可执行的多份文档，方便后续由人或 AI 分阶段完成全部目标。  
**适用范围**: `DbOptimizer` 当前代码基线与 `docs/` 中的目标能力补齐工作。

---

## 1. 最终目标

本轮实施以“把当前项目从概念版推进到可验收 v1”为目标，优先补齐以下能力：

1. SQL 调优闭环稳定可用
2. 实例配置调优闭环真正打通
3. 慢 SQL 采集后能自动进入分析闭环
4. SQL Rewrite 从占位变成真实能力
5. Dashboard 具备慢 SQL 趋势与基础告警
6. 文档、代码、AI 执行方式三者边界统一

---

## 2. 文档地图

### 2.1 总体入口

- [IMPLEMENTATION_TECHNICAL_PLAN.md](./IMPLEMENTATION_TECHNICAL_PLAN.md)
  - 说明要实现什么
  - 用什么技术实现
  - 核心设计点、风险点、注意事项是什么

- [IMPLEMENTATION_TASK_CHECKLIST.md](./IMPLEMENTATION_TASK_CHECKLIST.md)
  - 按任务列出要改哪些文件
  - 每个任务实现什么功能
  - 每个任务如何验证
  - 适合作为 AI 派单清单

- [IMPLEMENTATION_ACCEPTANCE_PLAN.md](./IMPLEMENTATION_ACCEPTANCE_PLAN.md)
  - 定义最终验收口径
  - 覆盖功能、API、数据库、构建、回归检查

- [AI_EXECUTION_PLAYBOOK.md](./AI_EXECUTION_PLAYBOOK.md)
  - 规范 AI / Agent 如何消费任务
  - 如何控制上下文
  - 如何写摘要、交接、阶段总结

### 2.2 参考背景

- [DOCS_VS_CODE_GAP_ANALYSIS.md](./DOCS_VS_CODE_GAP_ANALYSIS.md)
  - 保留本次差异分析的大盘参考
  - 不作为直接执行入口

---

## 3. 建议使用方式

### 如果由人主导开发

顺序建议：

1. 先读 `IMPLEMENTATION_TECHNICAL_PLAN.md`
2. 再按 `IMPLEMENTATION_TASK_CHECKLIST.md` 逐任务执行
3. 每完成一个任务，按 `IMPLEMENTATION_ACCEPTANCE_PLAN.md` 做局部验证
4. 每个阶段收口时，按 `AI_EXECUTION_PLAYBOOK.md` 的摘要模板写阶段总结

### 如果由 AI 主导执行

顺序建议：

1. 先给 AI `IMPLEMENTATION_INDEX.md`
2. 再给 AI 当前要做的任务段落
3. 要求 AI 只做一个任务，不跨多个 Sprint
4. 每次结束必须产出摘要、修改文件列表、验证结果和下一步建议

---

## 4. 分阶段实施顺序

### Sprint-A: 实例调优闭环

目标:
- 让 `DbConfigOptimization` 从“后端原型”变成“前后端可用闭环”

### Sprint-B: 慢 SQL 自动分析闭环

目标:
- 让慢 SQL 从“采集”升级为“采集 + 自动分析 + 可追踪”

### Sprint-C: SQL Rewrite 与 Dashboard 增强

目标:
- 让 SQL 优化结果更接近文档承诺

### Sprint-D: 文档与实现收敛

目标:
- 决定哪些规划能力立即实现，哪些保留为后续阶段

---

## 5. 本轮实施原则

1. 优先打通闭环，不先追求“大而全”
2. 优先复用现有工作流、API、前端页，不轻易新造体系
3. 结果模型与工作流类型必须解耦
4. 慢 SQL 自动分析必须复用现有 SQL 调优工作流
5. AI 任务执行必须带摘要和交接信息，防止上下文丢失

---

## 6. 阅读建议

### 只想了解方案

直接读:
- `IMPLEMENTATION_TECHNICAL_PLAN.md`

### 只想派任务

直接读:
- `IMPLEMENTATION_TASK_CHECKLIST.md`
- `AI_EXECUTION_PLAYBOOK.md`

### 只想做验收

直接读:
- `IMPLEMENTATION_ACCEPTANCE_PLAN.md`

---

## 7. 执行入口建议

后续如果要直接开始编码，建议从以下任务开始：

1. `TASK-A1` 统一工作流结果模型
2. `TASK-A2` 修正配置调优进度计算
3. `TASK-A3` 前端新增配置调优 API
4. `TASK-A4` 前端新增实例调优视图

这四个任务完成后，项目会从“只有 SQL 页能动”变成“实例调优也能真正跑起来”。
