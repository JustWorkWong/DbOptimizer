# 术语表

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## A

**Agent**  
智能代理，负责执行特定任务的 AI 组件。在 DbOptimizer 中，Agent 通过调用 Tool 与外部系统交互，并基于 LLM 进行决策。

**Aspire**  
.NET Aspire，微软推出的云原生应用编排框架，用于简化分布式应用的开发和部署。

**Audit Log**  
审计日志，记录系统中的关键操作和事件，用于安全审计和问题追踪。

---

## C

**Checkpoint**  
检查点，Workflow 的状态快照。每个 Executor 执行完成后保存 Checkpoint，支持进程重启后恢复。

**Confidence**  
置信度，Agent 对其决策的信心程度，范围 0-100。高置信度表示 Agent 有充分证据支持其建议。

**Context**  
上下文，Workflow 中各 Executor 之间共享的数据。通过 `WorkflowContext` 传递。

**Coordinator Executor**  
协调器执行器，负责汇总多个 Executor 的结果，生成最终报告。

---

## D

**DDL**  
Data Definition Language，数据定义语言。用于定义数据库结构的 SQL 语句（如 CREATE TABLE, CREATE INDEX）。

**DML**  
Data Manipulation Language，数据操作语言。用于操作数据的 SQL 语句（如 SELECT, INSERT, UPDATE, DELETE）。

---

## E

**Evidence**  
证据，支持 Agent 决策的数据引用。例如执行计划中的节点 ID、性能指标等。

**Executor**  
执行器，Workflow 中的一个执行单元。每个 Executor 负责一个独立的任务，如解析 SQL、获取执行计划等。

**Execution Plan**  
执行计划，数据库查询优化器生成的查询执行方案。包含扫描方式、JOIN 顺序、索引使用等信息。

---

## F

**Fallback**  
降级策略，当主要方案失败时使用的备用方案。例如 MCP 不可用时降级到直接数据库连接。

---

## H

**Human-in-the-loop**  
人工介入循环，AI 决策需要人工审核的机制。在 DbOptimizer 中，关键优化建议需要人工审核后才能执行。

**Human Review Executor**  
人工审核执行器，负责等待人工审核并处理审核结果。

---

## I

**Index**  
索引，数据库中用于加速查询的数据结构。常见类型包括 B-Tree 索引、Hash 索引等。

**Index Advisor**  
索引顾问，负责分析查询并推荐索引的 Agent。

---

## J

**JSONB**  
PostgreSQL 中的二进制 JSON 数据类型，支持高效的查询和索引。

---

## M

**MAF**  
Microsoft Agent Framework，微软的 Agent 框架，用于构建多 Agent 协作系统。

**MCP**  
Model Context Protocol，模型上下文协议。标准化 AI 模型与外部工具交互的协议。

**MCP Client**  
MCP 客户端，调用 MCP Server 的客户端组件。

**MCP Server**  
MCP 服务器，提供工具能力的服务。例如 MySQL MCP Server 提供获取执行计划、表统计等功能。

---

## P

**Prompt**  
提示词，发送给 LLM 的指令文本。在 DbOptimizer 中，每个 Agent 有独立的 Prompt 模板。

**Prompt Caching**  
提示词缓存，LLM 提供商的优化功能，缓存重复的 Prompt 前缀以降低成本。

**Prompt Version**  
Prompt 版本，记录 Prompt 的历史版本，支持 A/B 测试和回滚。

---

## R

**Reasoning**  
推理过程，Agent 做出决策的思考过程。记录在 `decision_records` 表中。

**Recommendation**  
建议，Agent 生成的优化建议。包括索引推荐、SQL 重写、配置调整等。

**Regeneration Executor**  
重新生成执行器，根据审核反馈重新生成建议。

**Repository Pattern**  
仓储模式，封装数据访问逻辑的设计模式。提供统一的数据访问接口。

---

## S

**Session**  
会话，一次完整的 Workflow 执行过程。每个 Session 有唯一的 SessionId。

**SSE**  
Server-Sent Events，服务器推送事件。用于实时推送 Workflow 执行状态到前端。

**SQL Parser**  
SQL 解析器，负责解析 SQL 语句并提取结构化信息的 Agent。

---

## T

**Tool**  
工具，Agent 可以调用的外部功能。例如 `GetExecutionPlan`、`GetTableIndexes` 等。

**Tool Call**  
工具调用，Agent 调用 Tool 的记录。包含参数、返回值、执行时间等信息。

**Token**  
令牌，LLM 处理文本的基本单位。Token 使用量直接影响 API 成本。

---

## W

**Workflow**  
工作流，由多个 Executor 组成的有向无环图（DAG）。定义了任务的执行顺序和依赖关系。

**Workflow Context**  
工作流上下文，Workflow 中各 Executor 之间共享的数据容器。

**Workflow Session**  
工作流会话，一次完整的 Workflow 执行实例。

---

## 其他

**置信度（Confidence）**  
Agent 对其决策的信心程度，范围 0-100。

**证据链（Evidence Chain）**  
支持决策的一系列证据引用，形成完整的推理链条。

**回放（Replay）**  
重新播放历史 Workflow 的执行过程，用于调试和学习。

**审核驳回回流（Review Rejection Loop）**  
审核被拒绝后，Workflow 回到 Regeneration Executor 重新生成建议的流程。

**热点缓存（Hot Cache）**  
Redis 中缓存的高频访问数据，用于加速恢复。

**冷启动（Cold Start）**  
进程重启后从 PostgreSQL 恢复 Checkpoint 的过程。
