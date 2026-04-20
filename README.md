# DbOptimizer

**AI 驱动的数据库性能优化平台**

基于 Microsoft Agent Framework (MAF) 构建的智能数据库优化工具，通过多 Agent 协作提供深度的性能分析和优化建议。

> 状态说明（2026-04-20）
> 当前仓库已经完成 MAF `1.1.0` 基线升级，以及最小 `ExternalRequest/PendingRequests/ResumeStreamingAsync` 互操作验证；
> 但原生 checkpoint / review request-response / resume 生命周期尚未完全接入主业务链路，相关重构仍在进行中。

---

## 核心特性

### 🤖 MAF 驱动的多 Agent 协作
- **SQL 层调优**：SQL 解析 + 执行计划分析 + 索引推荐 + SQL 重写
- **数据库层调优**：参数配置 + 资源分析 + 负载特征
- **人工审核**：所有 AI 建议必须人工审核，驳回后回流重跑
- **置信度 + 证据链**：每个建议都带置信度、原因、证据引用
- **Workflow Checkpoint**：已具备基础字段与互操作验证，原生暂停/恢复主链路仍在重构中

### 🔍 透明化 AI 过程
- 实时显示 Agent 工作过程（SSE 推送）
- 展示工具调用（Function Calling）
- 展示 AI 决策推理链
- 完整的执行记录；MAF 原生 checkpoint 持久化主链路仍在重构中

### 📊 慢查询分析
- 手工输入 SQL 分析
- 慢 SQL 自动抓取（MySQL slow log / PostgreSQL pg_stat_statements）
- 执行计划解读
- 索引推荐

### 🔌 MCP 集成
- **MySQL MCP**：直接接入现有 MySQL MCP
- **PostgreSQL MCP**：直接接入现有 PostgreSQL MCP
- 平台自身不实现 MCP Server（第一版）

---

## 技术栈

**后端**：
- .NET 10
- ASP.NET Core Web API
- Microsoft Agent Framework (MAF)
- Aspire（本地编排 PostgreSQL / Redis）
- SSE（实时推送）
- Entity Framework Core

**前端**：
- Vue 3
- Element Plus（UI 组件）
- Monaco Editor（SQL 编辑器）
- ECharts（图表）

**数据库**：
- PostgreSQL（主存储 + pgvector）
- Redis（缓存 + 会话）

**AI**：
- Azure OpenAI / Anthropic Claude
- Microsoft Agent Framework

**支持的目标数据库**：
- MySQL 5.7+
- PostgreSQL 13+

---

## 快速开始

### 前置要求

- .NET 10 SDK
- Azure OpenAI API Key 或 Anthropic API Key

### 安装步骤

1. **克隆仓库**
```bash
git clone https://github.com/yourusername/DbOptimizer.git
cd DbOptimizer
```

2. **配置环境变量**
```bash
cp .env.example .env
# 编辑 .env，填入 API Key
```

3. **启动应用（Aspire 自动编排 PostgreSQL / Redis）**
```bash
dotnet run --project src/DbOptimizer.AppHost
```

4. **访问应用**
```
https://localhost:17170 # Aspire AppHost / Dashboard 登录入口
http://localhost:15069  # API 对外固定入口 / Swagger / Health
http://localhost:10817  # Web 对外固定入口
```

说明：
- 在 Aspire 下，`15069` 是 API 的固定发布端口。
- 在 Aspire 下，`10817` 是 Web 的稳定入口。
- `8669` 在当前 Windows 环境位于 TCP excluded port range 内，无法稳定绑定，因此不再使用。
- 现在不再对 API/Web 端口做 fallback 猜测。缺少 `PORT` 或 `VITE_API_PROXY_TARGET` 时，前端会直接启动失败。
- 不应该再把容器或进程里看到的内部监听端口当成浏览器入口；浏览器和前端只认 AppHost 明确发布出来的固定端口。

---

## 项目结构

```
DbOptimizer/
├── src/
│   ├── DbOptimizer.AppHost/          # Aspire 编排
│   ├── DbOptimizer.API/              # ASP.NET Core Web API
│   ├── DbOptimizer.AgentRuntime/     # MAF Agent 运行时
│   ├── DbOptimizer.Core/             # 业务逻辑 + Workflow
│   ├── DbOptimizer.Infrastructure/   # 数据访问 + MCP 集成
│   ├── DbOptimizer.Web/              # Vue 3 前端
│   └── DbOptimizer.Shared/           # 共享模型
├── tests/
│   ├── DbOptimizer.Tests.Unit/
│   └── DbOptimizer.Tests.Integration/
├── docs/
│   ├── REQUIREMENTS.md               # 需求文档
│   └── DESIGN.md                     # 设计文档
├── CLAUDE.md
└── README.md
```

---

## 文档

- [需求文档](docs/REQUIREMENTS.md)
- [设计文档](docs/DESIGN.md)

---

## 使用示例

### 1. 分析慢查询

```sql
SELECT u.*, o.* 
FROM users u 
LEFT JOIN orders o ON u.id = o.user_id 
WHERE u.created_at > '2024-01-01'
```

**AI 分析结果**：
- 问题：`users` 表全表扫描
- 建议：在 `users.created_at` 上创建索引
- 预估提升：查询时间减少 80%

### 2. 配置优化

**当前配置**：
- `innodb_buffer_pool_size`: 128M
- 数据量：10GB

**AI 建议**：
- 调整为：4G
- 原因：Buffer Pool 命中率仅 60%
- 预估提升：查询性能提升 30-50%

### 3. 人工审核

**审核流程**：
1. Agent 生成优化建议
2. 前端展示建议 + 置信度 + 证据链
3. 用户审核：同意 / 驳回 / 调整
4. 驳回后回流 Workflow 重新生成

---

## 开发路线图

### Phase 1: 核心功能（第一版）✅
- [x] Aspire 编排 + 基础架构
- [x] MySQL & PostgreSQL MCP 接入
- [x] SQL 调优工作流（基于 MAF）
- [x] 数据库层调优工作流（基于 MAF）
- [ ] 人工审核 + 驳回回流（MAF 原生 Request/Response 主链路重构中）
- [ ] Agent 持久化 + MAF Workflow checkpoint（原生 checkpoint 主链路重构中）
- [x] 历史任务与版本查看
- [x] Vue 3 前端 + SSE 实时推送
- [ ] 慢 SQL 自动抓取与分析

### Phase 2: 增强功能 📅
- [ ] 上下文压缩 / 摘要
- [ ] RAG 知识库
- [ ] Token 优化策略
- [ ] 性能监控面板

### Phase 3: 扩展能力 💡
- [ ] 平台对外暴露 MCP Server
- [ ] Skills 插件系统
- [ ] 多数据库类型扩展

---

## 贡献指南

欢迎贡献！请查看 [CONTRIBUTING.md](CONTRIBUTING.md)

---

## 许可证

MIT License

---

## 联系方式

- 作者：tengfengsu
- 邮箱：your.email@example.com
- GitHub：[@yourusername](https://github.com/yourusername)

---

## 致谢

- [Microsoft Agent Framework](https://github.com/microsoft/agent-framework)
- [Vue.js](https://vuejs.org/)
- [Element Plus](https://element-plus.org/)
- [Supabase Index Advisor](https://github.com/supabase/index_advisor)


## Native Runtime Status

- 2026-04-21: The main workflow path now runs on native MAF checkpoint + native review request/response resume + unified event projection/logging.
- Remaining work is governance cleanup (feature flags/concurrency/legacy session tooling) rather than missing native-runtime main-path functionality.
