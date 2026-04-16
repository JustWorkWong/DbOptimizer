# DbOptimizer 项目规范

## 核心约束

**技术栈**：
- 后端：.NET 10 + ASP.NET Core + MAF + Aspire
- 前端：Vue 3 + Element Plus
- 数据库：PostgreSQL + Redis
- 目标数据库：MySQL 5.7+ / PostgreSQL 13+

**架构文档**：
- `docs/REQUIREMENTS.md`：需求文档（主文档）
- `docs/DESIGN.md`：设计文档（主文档）
- 修改架构前必须先更新文档

**第一版范围**：
- 单项目模式 + Aspire 编排
- MySQL & PostgreSQL MCP 同时接入
- 数据库层调优 + SQL 调优工作流
- 人工审核 + 审核驳回回流
- Agent 全量持久化 + Workflow checkpoint
- 置信度 + 证据链展示

**第一版不做**：用户体系、自动执行变更、平台暴露 MCP、复杂 RAG

---

## 项目结构

```
src/
├── DbOptimizer.AppHost/          # Aspire 编排
├── DbOptimizer.API/              # Web API + SSE 端点
├── DbOptimizer.AgentRuntime/     # MAF Agent 运行时
├── DbOptimizer.Core/             # Workflows + Executors + Services + Models
├── DbOptimizer.Infrastructure/   # Database + AI + MCP + Repositories
├── DbOptimizer.Web/              # Vue 3 前端
└── DbOptimizer.Shared/           # DTOs + Validators
```

---

## MAF 特定规范

**Agent 设计**：
- 单一职责，命名：`{功能}Agent`
- Prompt 存储在 `PromptVersion` 表，每次修改创建新版本

**Tool 设计**：
- Tool 是纯函数，添加 `[Description]` 属性
- 示例：`[Description("获取表的索引信息")] GetTableIndexesAsync(string tableName)`

**数据持久化**：
- 必须记录：Agent 执行记录、Tool 调用、决策记录、Agent 消息、错误
- 目的：调试、优化 Prompt、追踪成本、审计

---

## 前端规范

**Vue 3**：
- 使用 Composition API（`<script setup>`）
- Props 类型定义，组件单一职责

**状态管理**：
- Pinia：State + Getters + Actions

**SSE 集成**：
```typescript
const eventSource = new EventSource(`/api/workflows/${sessionId}/events`);
eventSource.onmessage = (event) => {
  const data = JSON.parse(event.data);
  store.updateWorkflowState(data);
};
```

---

## 命名约定

**C# 代码**：
- 类名：PascalCase（`SqlParserAgent`）
- 方法名：PascalCase（`AnalyzeAsync`）
- 参数/变量：camelCase（`sqlQuery`）
- 私有字段：_camelCase（`_dbConnection`）

**数据库**：
- 表名：snake_case 复数（`slow_queries`）
- 字段名：snake_case（`created_at`）

---

## 变更日志

- 2026-04-16: 明确迁移职责边界：后端结构迁移统一走 EF Core Migration，AppHost SQL 初始化仅用于测试数据
- 2026-04-15: 创建项目规范
- 2026-04-15: 精简配置，移除重复内容
