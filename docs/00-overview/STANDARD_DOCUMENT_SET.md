# Standard Document Set

## Purpose

这份文档定义 `DbOptimizer` 的标准文档包，后续 AI 和人工都以这组文档为唯一主入口。

## Canonical Documents

| 类别 | 标准文档 | 用途 |
|---|---|---|
| 文档总入口 | [../README.md](../README.md) | 文档树与阅读顺序 |
| 总览/状态 | [SYSTEM_SCOPE_AND_STATUS.md](./SYSTEM_SCOPE_AND_STATUS.md) | 系统范围、已完成/未完成、当前阶段 |
| 需求文档 | [../01-requirements/REQUIREMENTS_SPEC.md](../01-requirements/REQUIREMENTS_SPEC.md) | 目标、范围、功能需求、非功能需求 |
| 架构文档 | [../02-architecture/SYSTEM_ARCHITECTURE.md](../02-architecture/SYSTEM_ARCHITECTURE.md) | 系统架构、MAF 主线、分层、依赖 |
| 设计文档 | [../03-design/SYSTEM_DESIGN_SPEC.md](../03-design/SYSTEM_DESIGN_SPEC.md) | 设计拆分入口与设计规则 |
| 前后端出入参 | [../03-design/api/FRONTEND_BACKEND_IO_SPEC.md](../03-design/api/FRONTEND_BACKEND_IO_SPEC.md) | API 请求/响应、前端方法映射 |
| 整体任务 Checklist | [../04-implementation/OVERALL_TASK_CHECKLIST.md](../04-implementation/OVERALL_TASK_CHECKLIST.md) | Epic 级总任务图、依赖、状态 |
| 已完成事项 | [../04-implementation/COMPLETED_ITEMS.md](../04-implementation/COMPLETED_ITEMS.md) | 当前已实现/已明确落地内容 |
| 未完成事项 | [../04-implementation/PENDING_ITEMS.md](../04-implementation/PENDING_ITEMS.md) | 当前缺口、风险、待补内容 |
| 细分任务 Checklist | [../04-implementation/DETAILED_TASK_CHECKLIST.md](../04-implementation/DETAILED_TASK_CHECKLIST.md) | 任务级执行清单 |
| 任务卡索引 | [../04-implementation/TASK_CARDS_INDEX.md](../04-implementation/TASK_CARDS_INDEX.md) | 每个 `TASK-*` 的独立派发入口 |

## Supporting Documents

以下文档仍然重要，但不作为第一层入口：

- `02-architecture/MAF_WORKFLOW_ARCHITECTURE.md`
- `03-design/api/*.md`
- `03-design/workflow/*.md`
- `03-design/frontend/*.md`
- `03-design/data/DATA_MODEL.md`
- `04-implementation/IMPLEMENTATION_TECHNICAL_PLAN.md`
- `04-implementation/IMPLEMENTATION_ACCEPTANCE_PLAN.md`
- `04-implementation/AI_EXECUTION_PLAYBOOK.md`

## Standard Format

每类标准文档统一使用以下章节：

1. `Purpose`
2. `Scope`
3. `Current State`
4. `Target State`
5. `Checklist / Decisions / Contracts`
6. `References`

## Execution Rule

后续派任务时，最少附带这 4 份：

1. `REQUIREMENTS_SPEC.md`
2. `SYSTEM_ARCHITECTURE.md`
3. `FRONTEND_BACKEND_IO_SPEC.md`
4. `DETAILED_TASK_CHECKLIST.md`
