# DbOptimizer

**AI 驱动的数据库性能优化平台**

基于 Microsoft Agent Framework (MAF) 和 Blazor WebAssembly 构建的智能数据库优化工具，通过多 Agent 协作提供深度的性能分析和优化建议。

---

## 核心特性

### 🤖 多 Agent 协作分析
- **SqlParserAgent**：分析 SQL 语法结构
- **ExecutionPlanAgent**：解读执行计划
- **IndexAdvisorAgent**：推荐索引策略
- **ConfigurationAdvisorAgent**：分析数据库配置
- **CoordinatorAgent**：整合分析结果

### 🔍 透明化 AI 过程
- 实时显示 Agent 工作过程
- 展示工具调用（Function Calling）
- 展示 AI 决策推理链
- 完整的执行记录

### 📊 全方位性能监控
- 慢查询自动采集
- 性能指标趋势分析
- 资源使用监控
- 配置参数优化建议

### 🧠 RAG 知识库
- 历史优化案例
- 数据库最佳实践
- 语义搜索相似问题

### 🔌 可扩展集成
- **MCP Server**：对外提供标准化服务
- **MCP Client**：复用现有数据库 MCP
- **Skills 插件**：支持自定义扩展

---

## 技术栈

**后端**：
- .NET 10
- ASP.NET Core Web API
- Microsoft Agent Framework (MAF)
- SignalR（实时通信）
- Entity Framework Core
- Quartz.NET（定时任务）

**前端**：
- Blazor WebAssembly
- MudBlazor（UI 组件）
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
- Docker & Docker Compose
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

3. **启动依赖服务**
```bash
docker-compose up -d
```

4. **运行数据库迁移**
```bash
dotnet ef database update --project src/DbOptimizer.Infrastructure
```

5. **启动应用**
```bash
dotnet run --project src/DbOptimizer.API
```

6. **访问应用**
```
http://localhost:5000
```

---

## 项目结构

```
DbOptimizer/
├── src/
│   ├── DbOptimizer.API/              # Web API
│   ├── DbOptimizer.Core/             # 业务逻辑 + Agents
│   ├── DbOptimizer.Infrastructure/   # 数据访问 + 外部服务
│   ├── DbOptimizer.Web/              # Blazor 前端
│   ├── DbOptimizer.Shared/           # 共享模型
│   └── DbOptimizer.Skills/           # Skills 实现
├── tests/
│   ├── DbOptimizer.Tests.Unit/
│   └── DbOptimizer.Tests.Integration/
├── docs/
│   ├── superpowers/specs/
│   │   └── 2026-04-14-dboptimizer-design.md
│   ├── ARCHITECTURE.md
│   ├── DATA_MODEL.md
│   └── IMPLEMENTATION_PLAN.md
├── mcp/                              # MCP Server 配置
├── docker-compose.yml
├── CLAUDE.md
└── README.md
```

---

## 文档

- [需求设计文档](docs/superpowers/specs/2026-04-14-dboptimizer-design.md)
- [架构设计文档](docs/ARCHITECTURE.md)
- [数据模型文档](docs/DATA_MODEL.md)
- [实现计划](docs/IMPLEMENTATION_PLAN.md)

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

### 3. MCP 集成

```bash
# 在 Claude Code 中使用
/analyze-slow-query "SELECT * FROM users WHERE email = 'test@example.com'"
```

---

## 开发路线图

### Phase 1: 核心 AI 能力 ✅
- [x] 多 Agent 协作
- [x] 透明化 AI 过程
- [x] 数据持久化

### Phase 2: 监控 + 配置优化 🚧
- [ ] 慢查询监控
- [ ] 配置优化
- [ ] RAG 知识库

### Phase 3: MCP + Skills 📅
- [ ] MCP Server
- [ ] Skills 插件系统
- [ ] 文档完善

### Phase 4: 生产级特性 💡
- [ ] Agent 模式部署
- [ ] 审批流
- [ ] 告警通知

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
- [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
- [MudBlazor](https://mudblazor.com/)
- [Supabase Index Advisor](https://github.com/supabase/index_advisor)

