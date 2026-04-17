# Overall Task Checklist

## Purpose

提供 Epic 级总清单，明确做完的、未做的、依赖关系和执行顺序。

## Current State

当前总体状态以 `SYSTEM_SCOPE_AND_STATUS.md` 为准，这份文档只做任务执行视角的汇总。

## Checklist

| Epic | 名称 | 状态 | 依赖 | 是否可开始 |
|---|---|---|---|---|
| A | 契约与数据库基础 | `进行中（TASK-A1 已完成）` | 无 | `是` |
| B | MAF Runtime 基础设施 | `未完成` | A | `否` |
| C | SQL Workflow 迁移 | `未完成` | A + B | `否` |
| D | 配置调优闭环 | `未完成` | A + B | `否` |
| E | 慢 SQL 自动分析闭环 | `未完成` | C | `否` |
| F | Dashboard 趋势与告警 | `未完成` | E | `否` |
| G | PromptVersion 管理 | `未完成` | A | `否` |

## Execution Order

1. Epic A
2. Epic B
3. Epic C
4. Epic D
5. Epic E
6. Epic F
7. Epic G

## References

- [MASTER_DELIVERY_ROADMAP.md](./MASTER_DELIVERY_ROADMAP.md)
- [DETAILED_TASK_CHECKLIST.md](./DETAILED_TASK_CHECKLIST.md)
