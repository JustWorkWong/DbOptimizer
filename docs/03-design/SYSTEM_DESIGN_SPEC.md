# System Design Specification

## Purpose

作为设计层总入口，统一指向 workflow、API、前端、数据设计文档。

## Scope

覆盖：

1. API 契约
2. workflow 详细设计
3. 前端页面与组件设计
4. 数据模型

## Implementation Status

**✅ MAF 迁移已完成** (2026-04-18)

所有 workflow 设计已基于 MAF 实现。

## Design Structure

### API Design

- [api/API_OVERVIEW.md](./api/API_OVERVIEW.md)
- [api/WORKFLOW_API_CONTRACT.md](./api/WORKFLOW_API_CONTRACT.md)
- [api/REVIEW_API_CONTRACT.md](./api/REVIEW_API_CONTRACT.md)
- [api/DASHBOARD_API_CONTRACT.md](./api/DASHBOARD_API_CONTRACT.md)
- [api/SSE_EVENT_CONTRACT.md](./api/SSE_EVENT_CONTRACT.md)
- [api/FRONTEND_BACKEND_IO_SPEC.md](./api/FRONTEND_BACKEND_IO_SPEC.md)

### Workflow Design

- [workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md](./workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md)
- [workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md](./workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md)
- [workflow/DB_CONFIG_WORKFLOW_DESIGN.md](./workflow/DB_CONFIG_WORKFLOW_DESIGN.md)

### Frontend Design

- [frontend/UI_INFORMATION_ARCHITECTURE.md](./frontend/UI_INFORMATION_ARCHITECTURE.md)
- [frontend/PAGE_DESIGN.md](./frontend/PAGE_DESIGN.md)
- [frontend/COMPONENT_SPEC.md](./frontend/COMPONENT_SPEC.md)

### Data Design

- [data/DATA_MODEL.md](./data/DATA_MODEL.md)

## Design Rules

1. 所有设计必须能落到类/方法/API/字段。
2. 如果某个任务实现需要自己补关键契约，说明设计还不够完整。
3. 设计文档必须与 `DETAILED_TASK_CHECKLIST.md` 一一对应。

## References

- [../04-implementation/DETAILED_TASK_CHECKLIST.md](../04-implementation/DETAILED_TASK_CHECKLIST.md)
