# DbOptimizer 实现计划

**创建日期**：2026-04-15  
**版本**：v1.0

---

## 1. 实现阶段划分

### Phase 1: 核心 AI 能力（2 周）

**目标**：实现基础的 SQL 分析功能，展示 MAF 多 Agent 协作

#### 1.1 基础架构搭建（3 天）

**任务**：
- [ ] 创建 .NET 10 解决方案
- [ ] 搭建项目结构（API + Core + Infrastructure + Web + Shared）
- [ ] 配置 PostgreSQL + Redis
- [ ] 配置 Entity Framework Core
- [ ] 配置 MAF + Azure OpenAI
- [ ] 配置 SignalR
- [ ] 编写 CLAUDE.md

**交付物**：
- 可运行的空项目
- 数据库迁移脚本
- Docker Compose 配置

#### 1.2 数据库适配器（2 天）

**任务**：
- [ ] 实现 `IDatabaseAdapter` 接口
- [ ] 实现 `MySqlAdapter`
  - 连接测试
  - 获取执行计划（EXPLAIN）
  - 获取表结构
  - 获取索引信息
- [ ] 实现 `PostgreSqlAdapter`
  - 连接测试
  - 获取执行计划（EXPLAIN ANALYZE）
  - 获取表结构
  - 获取索引信息
- [ ] 单元测试

**交付物**：
- 数据库适配器实现
- 单元测试（覆盖率 > 80%）

#### 1.3 MAF Agents 实现（4 天）

**任务**：
- [ ] 实现 Tools（Function Calling）
  - `DatabaseMetadataTool`
  - `ExecutionPlanTool`
  - `IndexAnalysisTool`
- [ ] 实现 Agents
  - `SqlParserAgent`：分析 SQL 语法结构
  - `ExecutionPlanAgent`：解读执行计划
  - `IndexAdvisorAgent`：推荐索引
  - `CoordinatorAgent`：整合结果
- [ ] 实现 `AgentOrchestrator`：编排 Agent 执行
- [ ] 实现数据持久化（Agent 执行记录、工具调用、决策记录）
- [ ] 集成测试

**交付物**：
- 4 个 Agent 实现
- Agent 编排器
- 集成测试

#### 1.4 API + SignalR（2 天）

**任务**：
- [ ] 实现 REST API
  - `POST /api/analysis/analyze`：触发分析
  - `GET /api/analysis/{sessionId}`：查询分析结果
  - `GET /api/analysis/sessions`：查询历史会话
- [ ] 实现 SignalR Hub
  - `AgentStarted`
  - `ToolCalled`
  - `AgentDecisionMade`
  - `AgentCompleted`
  - `AnalysisCompleted`
- [ ] API 文档（Swagger）

**交付物**：
- REST API 实现
- SignalR Hub 实现
- API 文档

#### 1.5 Blazor 前端（3 天）

**任务**：
- [ ] 搭建 Blazor WebAssembly 项目
- [ ] 配置 MudBlazor
- [ ] 实现 Analyze 页面
  - SQL 编辑器（Monaco Editor）
  - Agent 执行过程可视化
  - 优化报告展示
- [ ] 实现 SignalR 客户端
- [ ] 状态管理（Fluxor）

**交付物**：
- Analyze 页面
- 实时显示 Agent 工作过程

---

### Phase 2: 监控 + 配置优化（2 周）

**目标**：实现慢查询监控、配置优化、RAG 知识库

#### 2.1 慢查询采集（3 天）

**任务**：
- [ ] 实现 `SlowQueryCollector`
  - MySQL：读取 `mysql.slow_log`
  - PostgreSQL：读取 `pg_stat_statements`
- [ ] 实现定时任务（Quartz.NET）
- [ ] 实现慢查询去重（SQL 指纹）
- [ ] 实现慢查询列表 API
- [ ] 实现 Monitor 页面

**交付物**：
- 慢查询自动采集
- Monitor 页面

#### 2.2 配置优化（4 天）

**任务**：
- [ ] 实现配置采集
  - `ConfigurationTool`：读取数据库配置
  - `ServerMetricsTool`：采集服务器指标
  - `DatabaseStatsTool`：采集数据库统计
- [ ] 实现 Agents
  - `ConfigurationAdvisorAgent`
  - `ResourceAnalyzerAgent`
- [ ] 实现配置分析 API
- [ ] 实现 Config 页面
  - 资源概览
  - 配置建议表格
  - 资源瓶颈告警

**交付物**：
- 配置优化功能
- Config 页面

#### 2.3 RAG 知识库（4 天）

**任务**：
- [ ] 配置 pgvector
- [ ] 实现 Embedding 服务
- [ ] 实现知识库 CRUD
  - 添加案例
  - 向量化
  - 语义搜索
- [ ] 集成到 Agent（参考历史案例）
- [ ] 实现 Knowledge 页面
  - 案例列表
  - 搜索
  - 添加/编辑案例

**交付物**：
- RAG 知识库
- Knowledge 页面

#### 2.4 性能趋势图表（3 天）

**任务**：
- [ ] 实现性能指标采集
- [ ] 实现趋势图表（ECharts）
  - QPS/TPS 趋势
  - 慢查询数量趋势
  - Buffer Pool 命中率趋势
- [ ] 更新 Monitor 页面

**交付物**：
- 性能趋势图表

---

### Phase 3: MCP + Skills + 完善（1 周）

**目标**：实现 MCP Server、Skills 插件系统、完善文档

#### 3.1 MCP Server（2 天）

**任务**：
- [ ] 实现 MCP Server
  - `analyze_slow_query`
  - `get_slow_queries`
  - `recommend_indexes`
  - `analyze_configuration`
- [ ] 编写 MCP 配置文件
- [ ] 测试与 Claude Code 集成

**交付物**：
- MCP Server 实现
- MCP 配置文档

#### 3.2 MCP Client（1 天）

**任务**：
- [ ] 集成 `@modelcontextprotocol/server-postgres`
- [ ] 集成 `@modelcontextprotocol/server-mysql`
- [ ] 实现 MCP Client 适配器

**交付物**：
- MCP Client 集成

#### 3.3 Skills 插件系统（2 天）

**任务**：
- [ ] 实现 `ISkillPlugin` 接口
- [ ] 实现 Skills 加载器
- [ ] 实现内置 Skills
  - `/analyze-slow-query`
  - `/recommend-indexes`
  - `/explain-plan`
  - `/analyze-config`
- [ ] 编写 Skills 文档

**交付物**：
- Skills 插件系统
- 内置 Skills
- Skills 开发文档

#### 3.4 测试 + 文档（2 天）

**任务**：
- [ ] 单元测试补充（覆盖率 > 80%）
- [ ] 集成测试补充
- [ ] 编写用户文档
  - 快速开始
  - 功能说明
  - 配置指南
- [ ] 编写开发文档
  - 架构说明
  - 扩展指南
- [ ] 录制 Demo 视频

**交付物**：
- 完整测试
- 用户文档
- 开发文档
- Demo 视频

---

### Phase 4: 生产级特性（可选，1-2 周）

**目标**：实现生产环境所需的高级特性

#### 4.1 Agent 模式部署（3 天）

**任务**：
- [ ] 实现轻量级 Agent
- [ ] 实现数据推送
- [ ] 实现 Agent 管理

#### 4.2 审批流（可选，3 天）

**任务**：
- [ ] 实现审批流引擎
- [ ] 集成到优化建议

#### 4.3 告警通知（可选，2 天）

**任务**：
- [ ] 实现告警规则
- [ ] 实现通知渠道（邮件/Webhook）

---

## 2. 功能优先级

### P0（必须实现）

- [x] SQL 分析（多 Agent 协作）
- [x] 透明化 AI 过程
- [x] 数据持久化
- [x] Blazor 前端
- [x] 配置优化

### P1（重要）

- [x] 慢查询监控
- [x] RAG 知识库
- [x] MCP Server
- [x] Skills 插件系统

### P2（可选）

- [ ] Agent 模式部署
- [ ] 审批流
- [ ] 告警通知
- [ ] 批量分析
- [ ] 导出功能

---

## 3. 技术风险

### 3.1 MAF 稳定性

**风险**：MAF 1.0 刚 GA，可能有 bug

**缓解**：
- 关注官方 GitHub Issues
- 准备降级方案（直接调用 Azure OpenAI API）

### 3.2 Blazor WebAssembly 性能

**风险**：首次加载慢

**缓解**：
- 启用 AOT 编译
- 使用 Lazy Loading
- 优化资源大小

### 3.3 数据库连接安全

**风险**：存储数据库密码

**缓解**：
- 使用 AES 加密
- 支持环境变量
- 支持 Azure Key Vault（可选）

---

## 4. 里程碑

| 里程碑 | 日期 | 交付物 |
|--------|------|--------|
| M1: MVP | Week 2 | 核心 SQL 分析功能 |
| M2: 监控 | Week 4 | 慢查询监控 + 配置优化 + RAG |
| M3: 集成 | Week 5 | MCP + Skills + 文档 |
| M4: 生产级 | Week 7 | Agent 模式 + 审批流（可选）|

---

## 5. 团队分工（单人项目）

**Phase 1（Week 1-2）**：
- 专注后端（MAF Agents）
- 简单前端（验证功能）

**Phase 2（Week 3-4）**：
- 完善前端（UI/UX）
- 补充功能（监控、配置）

**Phase 3（Week 5）**：
- 集成（MCP、Skills）
- 文档 + 测试

---

## 6. 验收标准

### Phase 1

- [ ] 输入 SQL，能看到 3 个 Agent 依次执行
- [ ] 实时显示 Agent 调用的工具
- [ ] 实时显示 Agent 的决策过程
- [ ] 输出优化建议（索引、SQL 重写）
- [ ] 所有数据持久化到数据库

### Phase 2

- [ ] 自动采集慢查询
- [ ] 显示慢查询列表
- [ ] 分析数据库配置，给出建议
- [ ] 知识库能检索相似案例
- [ ] 性能趋势图表

### Phase 3

- [ ] Claude Code 能通过 MCP 调用分析功能
- [ ] Skills 能在 Claude Code 中使用
- [ ] 文档完整，能指导他人使用

---

## 7. 下一步

完成 brainstorming 后，进入 **writing-plans** 阶段，生成详细的实现计划。

