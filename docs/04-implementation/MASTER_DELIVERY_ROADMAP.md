# Master Delivery Roadmap

## 总览

| Epic | 目标 | 当前状态 | 输出 |
|---|---|---|---|
| A | 统一结果契约与数据库字段 | `未开始` | 统一 DTO、result envelope、source/engine 字段 |
| B | 引入 MAF runtime 基础设施 | `未开始` | MAF runtime、factory、projection、resume/cancel |
| C | SQL workflow 迁移到 MAF | `部分完成` | SQL workflow 可运行、可 review、可 replay |
| D | 配置调优 workflow 打通 | `部分完成` | 前后端闭环、统一结果展示 |
| E | 慢 SQL 自动分析闭环 | `未开始` | 采集 -> 自动提交 -> 追踪到 session |
| F | Dashboard 趋势与告警 | `未开始` | trend、alerts、slow query list/detail |
| G | PromptVersion 管理与运营能力 | `未开始` | prompt 版本 API/UI/回滚 |

## Epic A

目标：

- 固定唯一结果协议 `WorkflowResultEnvelope`
- 固定 `sourceType/sourceRefId`
- 为 MAF 运行态预留表字段

出口：

- `workflow/history/review` 都返回统一结果壳
- EF 实体与数据库字段准备完成

## Epic B

目标：

- 引入 `Microsoft.Agents.AI.Workflows`
- 建立 workflow factory、runtime、projection、checkpoint bridge

出口：

- API 层通过 application service 启动 MAF workflow
- `resume/cancel/status` 不再直接依赖旧 runner

## Epic C

目标：

- SQL workflow 切换到 MAF
- review gate 使用 request/response
- SQL rewrite 不再是占位

出口：

- SQL workflow 从创建到完成可全链路跑通
- `history` 与 `SSE` 可看到 SQL workflow 的 MAF 事件投影

## Epic D

目标：

- 配置调优 workflow 切换到 MAF
- 前端新增配置调优入口与结果渲染

出口：

- 配置调优可从 UI 发起、进入 review、完成、进入 history

## Epic E

目标：

- 慢 SQL 采集结果自动提交 SQL workflow
- 建立 `slow_queries.latest_analysis_session_id`

出口：

- 可从 slow query 追到 analysis session，也可从 history 追到 slow query

## Epic F

目标：

- dashboard 增加趋势和告警
- 补 slow query 列表与详情 API

出口：

- 前端可查看趋势、告警、slow query 明细与最近分析结果

## Epic G

目标：

- 打通 `prompt_versions` 的 API、管理页、激活与回滚

出口：

- prompt 版本最少支持 list/create/activate/rollback

## 依赖矩阵

| Epic | 依赖 | 准入条件 |
|---|---|---|
| A | 无 | 文档契约已冻结 |
| B | A | 统一结果壳与数据库字段已定义 |
| C | A + B | MAF runtime 与 projection 基础可用 |
| D | A + B | 配置 workflow 所需 runtime/review 契约可用 |
| E | C | SQL workflow 已支持 `sourceType=slow-query` |
| F | E | slow query 追踪与 dashboard API 已存在 |
| G | A | 可独立开始，但不应阻塞 A-F |
