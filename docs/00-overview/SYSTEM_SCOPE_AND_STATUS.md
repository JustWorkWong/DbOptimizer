# System Scope And Status

## 系统目标

`DbOptimizer` 目标不是单点 SQL 建议器，而是一套面向数据库优化运维的工作流系统，至少覆盖：

1. 手工提交 SQL，得到结构化 SQL 调优建议。
2. 自动采集慢 SQL，并自动进入 SQL 分析闭环。
3. 对数据库实例配置做采集、规则分析、人工审核与建议输出。
4. 统一展示 workflow 状态、历史、回放、审核与趋势。
5. 使用 MAF workflow 管理执行图、request/response、checkpoint、resume 与 human-in-the-loop。

## 当前状态矩阵

| 能力域 | 目标能力 | 当前代码状态 | 状态判断 | 主文档 |
|---|---|---|---|---|
| Workflow 引擎 | 使用 MAF workflow 统一编排 | 仍是自研 `WorkflowRunner + IWorkflowExecutor` | `未开始` | [../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md](../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md) |
| SQL 调优 workflow | 解析、执行计划、索引建议、人工审核 | 后端主链存在，但不是 MAF，结果契约未统一 | `部分完成` | [../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md](../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md) |
| 配置调优 workflow | 配置采集、规则分析、人工审核、结果展示 | 后端骨架存在，前端未接入，结果类型未打通 | `部分完成` | [../03-design/workflow/DB_CONFIG_WORKFLOW_DESIGN.md](../03-design/workflow/DB_CONFIG_WORKFLOW_DESIGN.md) |
| HITL | Workflow 在 review gate 暂停，审核后继续 | 已有 review task 表和 API，但未接 MAF request/response | `部分完成` | [../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md](../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md) |
| 结果契约 | SQL 与配置调优共用统一结果壳 | 仍写死为 `OptimizationReport` | `未完成` | [../03-design/api/API_OVERVIEW.md](../03-design/api/API_OVERVIEW.md) |
| 历史与回放 | 统一状态页、历史页、SSE 回放 | 已有 API 与前端基础能力，但只支持旧结果模型 | `部分完成` | [../03-design/api/SSE_EVENT_CONTRACT.md](../03-design/api/SSE_EVENT_CONTRACT.md) |
| 慢 SQL 采集 | 周期采集并入库 | 已实现 | `已完成` | [../05-testing/TEST_QUERIES.md](../05-testing/TEST_QUERIES.md) |
| 慢 SQL 自动分析 | 采集后自动创建 SQL workflow，并能回溯关联 session | 未实现提交与关联字段 | `未完成` | [../03-design/api/DASHBOARD_API_CONTRACT.md](../03-design/api/DASHBOARD_API_CONTRACT.md) |
| 趋势与告警 | Dashboard 展示慢 SQL 趋势与告警 | 只有通用 dashboard stats | `未完成` | [../03-design/api/DASHBOARD_API_CONTRACT.md](../03-design/api/DASHBOARD_API_CONTRACT.md) |
| PromptVersion 管理 | Prompt 版本维护、激活、回滚 | 只有表结构 | `未开始` | [../04-implementation/MASTER_DELIVERY_ROADMAP.md](../04-implementation/MASTER_DELIVERY_ROADMAP.md) |
| 前端导航 | SQL、配置、审核、历史、回放、慢 SQL、Dashboard | 只有 `sql/review/history/replay` | `部分完成` | [../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md](../03-design/frontend/UI_INFORMATION_ARCHITECTURE.md) |

## 本轮完成目标

1. 把系统整体路线图和 done/not-done 状态固定下来。
2. 把 workflow 编排方案明确切换到 MAF。
3. 把 SQL 调优、配置调优、慢 SQL 闭环、dashboard 契约全部细化到 API 与类设计级别。
4. 把 AI 可执行任务拆到“新增什么类、类里有什么方法、入参出参是什么、如何验证”。

## 执行原则

1. 不做“推倒重来”，而是做“MAF 迁移式重构”。
2. 先统一契约，再迁移执行引擎，再补前端入口与慢 SQL 闭环。
3. `workflow/history/review/SSE` 四条面必须使用同一份结果协议。
