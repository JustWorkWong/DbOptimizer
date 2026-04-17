# Requirements Specification

## Purpose

定义 `DbOptimizer` 本轮以及整体目标的标准需求基线，替代零散需求描述作为执行入口。

## Scope

### In Scope

1. SQL 调优 workflow
2. 数据库配置调优 workflow
3. 慢 SQL 自动采集与自动分析闭环
4. Review / History / Replay / Dashboard
5. MAF workflow 引擎迁移
6. PromptVersion 基础管理

### Out Of Scope

1. 重新设计前端技术栈
2. 引入第二套 workflow 引擎
3. 与本轮无关的外部产品集成

## Business Goals

1. 提供可持续演进的数据库优化平台，而不是单次 SQL 分析工具。
2. 统一 SQL 调优、配置调优、慢 SQL 自动分析的产品形态。
3. 统一 workflow 生命周期、审核、历史与回放能力。
4. 为 AI 并行执行准备稳定的设计与 checklist。

## Functional Requirements

### FR-1 SQL Analysis

- 用户可以提交 SQL、数据库 ID、数据库类型。
- 系统返回 SQL 优化结果。
- 系统支持索引建议、SQL rewrite、人工审核。

### FR-2 DB Config Optimization

- 用户可以提交数据库实例标识与数据库类型。
- 系统采集配置与指标，输出配置建议。
- 系统支持人工审核与最终结果落库。

### FR-3 Slow Query Automation

- 系统定时采集慢 SQL。
- 新采集的慢 SQL 自动触发 SQL 分析 workflow。
- 慢 SQL 与 workflow session 双向可追踪。

### FR-4 Workflow Management

- 系统支持启动、查看状态、取消、恢复。
- 系统支持通过 SSE 查看运行事件。
- 系统支持 history / replay。

### FR-5 Review

- workflow 可在 review gate 暂停。
- reviewer 可 approve / reject / adjust。
- review submit 后 workflow 可继续。

### FR-6 Dashboard

- 展示总任务、运行中、待审核、完成数。
- 展示慢 SQL 趋势和告警。
- 展示最近任务和关联慢 SQL。

### FR-7 PromptVersion

- 支持 list / create / activate / rollback。

## Non-Functional Requirements

1. 必须使用 MAF workflow。
2. API 必须使用统一 envelope 和统一结果壳。
3. 设计必须细化到类、方法、入参、出参、数据库字段。
4. 任务清单必须可直接派给 AI。

## Current State

当前真实代码状态见：

- [../00-overview/SYSTEM_SCOPE_AND_STATUS.md](../00-overview/SYSTEM_SCOPE_AND_STATUS.md)

## Target State

目标实现状态见：

- [../02-architecture/SYSTEM_ARCHITECTURE.md](../02-architecture/SYSTEM_ARCHITECTURE.md)
- [../04-implementation/OVERALL_TASK_CHECKLIST.md](../04-implementation/OVERALL_TASK_CHECKLIST.md)

## References

- [PRODUCT_REQUIREMENTS.md](./PRODUCT_REQUIREMENTS.md)
- [DELIVERY_SCOPE_P0_P1.md](./DELIVERY_SCOPE_P0_P1.md)
