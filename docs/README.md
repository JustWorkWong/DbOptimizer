# DbOptimizer Docs

`docs/` 现在按“先看整体，再看设计，再看执行”的顺序组织，不再把需求、设计、实施、测试平铺在同一层。

推荐阅读顺序：

1. [00-overview/STANDARD_DOCUMENT_SET.md](./00-overview/STANDARD_DOCUMENT_SET.md)
2. [00-overview/SYSTEM_SCOPE_AND_STATUS.md](./00-overview/SYSTEM_SCOPE_AND_STATUS.md)
3. [01-requirements/REQUIREMENTS_SPEC.md](./01-requirements/REQUIREMENTS_SPEC.md)
4. [02-architecture/SYSTEM_ARCHITECTURE.md](./02-architecture/SYSTEM_ARCHITECTURE.md)
5. [03-design/SYSTEM_DESIGN_SPEC.md](./03-design/SYSTEM_DESIGN_SPEC.md)
6. [03-design/api/FRONTEND_BACKEND_IO_SPEC.md](./03-design/api/FRONTEND_BACKEND_IO_SPEC.md)
7. [04-implementation/OVERALL_TASK_CHECKLIST.md](./04-implementation/OVERALL_TASK_CHECKLIST.md)
8. [04-implementation/DETAILED_TASK_CHECKLIST.md](./04-implementation/DETAILED_TASK_CHECKLIST.md)

目录说明：

- `00-overview/`: 总入口、系统范围、当前状态
- `01-requirements/`: 需求与阶段范围
- `02-architecture/`: 总体架构与专项架构
- `03-design/`: API 契约、workflow 详细设计、前端/数据设计
- `04-implementation/`: 路线图、任务清单、验收、AI 执行规范
- `05-testing/`: 测试方法与验证资料
- `06-standards/`: 编码与协作规范
- `90-archive/`: 历史方案、旧版索引、差异分析

当前执行基线：

- 必须使用 Microsoft Agent Framework Workflow 功能完成工作流重构。
- 详细设计以 `03-design/` 为准，不能再直接以 archive 中旧文档为实现依据。
- AI 派发任务时，以 `04-implementation/README.md` 为入口，以 `04-implementation/AI_EXECUTION_PLAYBOOK.md` 为执行规则。
