# 系统架构设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [整体架构](#1-整体架构)
2. [分层设计](#2-分层设计)
3. [技术选型](#3-技术选型)
4. [模块依赖关系](#4-模块依赖关系)

---

## 1. 整体架构

### 1.1 系统架构图

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Vue 3 前端 (Element Plus)                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐             │
│  │ SQL Analysis │  │ DB Config    │  │ Review       │             │
│  │ Page         │  │ Optimization │  │ Workspace    │             │
│  └──────────────┘  └──────────────┘  └──────────────┘             │
│                            │                                         │
│                    HTTP REST API + SSE                              │
└────────────────────────────┼────────────────────────────────────────┘
                             │
┌────────────────────────────┼────────────────────────────────────────┐
│                  ASP.NET Core Web API                               │
│  ┌──────────────────────────┴─────────────────────────────────┐    │
│  │              Controllers + SSE Endpoints                     │    │
│  └──────────────────────────┬─────────────────────────────────┘    │
│                             │                                        │
│  ┌──────────────────────────┴─────────────────────────────────┐    │
│  │           MAF Workflow Orchestrator                         │    │
│  │  ┌────────────────────────────────────────────────────┐    │    │
│  │  │  SQL Analysis Workflow                             │    │    │
│  │  │  ┌──────────┐  ┌──────────┐  ┌──────────┐        │    │    │
│  │  │  │ SQL      │→ │Execution │→ │  Index   │        │    │    │
│  │  │  │ Parser   │  │  Plan    │  │ Advisor  │        │    │    │
│  │  │  │ Executor │  │ Executor │  │ Executor │        │    │    │
│  │  │  └──────────┘  └──────────┘  └──────────┘        │    │    │
│  │  │       ↓             ↓             ↓               │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Coordinator Executor             │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  │       ↓                                           │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Human Review Executor            │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  │       ↓ (if rejected)                            │    │    │
│  │  │  ┌────────────────────────────────────┐          │    │    │
│  │  │  │   Regeneration Executor            │          │    │    │
│  │  │  └────────────────────────────────────┘          │    │    │
│  │  └────────────────────────────────────────────────────┘    │    │
│  └─────────────────────────┬───────────────────────────────────┘    │
│                            │                                         │
│  ┌─────────────────────────┴───────────────────────────────────┐    │
│  │              Shared Services                                 │    │
│  │  • WorkflowSessionManager                                   │    │
│  │  • CheckpointStorage                                        │    │
│  │  • SSEPublisher                                             │    │
│  │  • McpClientPool                                            │    │
│  └──────────────────────────┬───────────────────────────────────┘    │
└─────────────────────────────┼────────────────────────────────────────┘
                              │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
   ┌────▼────┐          ┌────▼────┐          ┌────▼────┐
   │PostgreSQL│          │  Redis  │          │   MCP   │
   │         │          │         │          │ Servers │
   │• Sessions│          │• Cache  │          │         │
   │• Checkpts│          │• SSE    │          │• MySQL  │
   │• Agents  │          │         │          │• PgSQL  │
   └─────────┘          └─────────┘          └─────────┘
```

### 1.2 架构特点

**关键设计决策**：

1. **MAF Workflow 编排**：使用 Microsoft Agent Framework 实现多 Agent 协作
2. **Checkpoint 机制**：支持进程重启后恢复，长时间运行的 Workflow
3. **SSE 实时推送**：前端实时展示 Agent 工作过程
4. **MCP 协议集成**：通过 MCP 访问目标数据库（MySQL / PostgreSQL）
5. **Human-in-the-loop**：关键决策需要人工审核

**架构优势**：

- **可观测性**：完整记录 Agent 执行过程、决策推理链
- **容错性**：Checkpoint + 重试机制
- **扩展性**：新增 Executor 即可扩展功能
- **透明性**：实时展示 AI 工作过程，建立信任

---

## 2. 分层设计

### 2.1 项目结构

```
src/
├── DbOptimizer.AppHost/          # Aspire 编排入口
├── DbOptimizer.API/              # Web API + SSE 端点
├── DbOptimizer.Core/             # 核心业务模型
│   └── Models/                   # 领域模型
├── DbOptimizer.Infrastructure/   # 基础设施层
│   ├── Workflows/                # Workflow 定义与执行器
│   ├── Persistence/              # EF Core + Repositories
│   ├── SlowQuery/                # 慢查询收集
│   ├── Checkpointing/            # Checkpoint 存储
│   └── Mcp/                      # MCP 客户端
└── DbOptimizer.Web/              # Vue 3 前端
```

### 2.2 分层职责

| 层级 | 职责 | 依赖方向 |
|------|------|---------|
| **Presentation** | API Controllers, SSE Endpoints | → Infrastructure |
| **Infrastructure** | Workflows, Executors, Database, MCP | → Core |
| **Core** | 领域模型, 业务规则 | 无外部依赖 |

**依赖倒置原则**：
- Core 层定义纯业务模型（ParsedSqlModels, ExecutionPlanModels）
- Infrastructure 层实现所有基础设施（Workflows, Database, MCP）
- API 层调用 Infrastructure 提供的服务
- 通过 DI 注入依赖

---

## 3. 技术选型

### 3.1 后端技术栈

| 组件 | 技术选型 | 理由 |
|------|---------|------|
| **框架** | .NET 10 + ASP.NET Core | 最新稳定版本，性能优异 |
| **Agent 框架** | Microsoft Agent Framework (MAF) | 官方支持，与 .NET 生态集成好 |
| **ORM** | Entity Framework Core | 成熟稳定，支持 PostgreSQL |
| **缓存** | Redis | 高性能，支持 SSE 会话管理 |
| **编排** | .NET Aspire | 本地开发体验好，自动配置依赖 |
| **实时推送** | Server-Sent Events (SSE) | 单向推送，比 WebSocket 简单 |

### 3.2 前端技术栈

| 组件 | 技术选型 | 理由 |
|------|---------|------|
| **框架** | Vue 3 (Composition API) | 轻量级，学习曲线平缓 |
| **UI 库** | Element Plus | 企业级组件库，开箱即用 |
| **状态管理** | Pinia | Vue 3 官方推荐 |
| **SQL 编辑器** | Monaco Editor | VS Code 同款编辑器 |
| **图表** | ECharts | 功能强大，社区活跃 |

### 3.3 数据库选型

| 数据库 | 用途 | 理由 |
|--------|------|------|
| **PostgreSQL** | 主存储 | 支持 JSONB，适合存储 Checkpoint |
| **Redis** | 缓存 + 会话 | 高性能，支持 Pub/Sub |

### 3.4 AI 服务选型

| 服务 | 用途 | 理由 |
|------|------|------|
| **Azure OpenAI** | GPT-4o | 企业级稳定性，支持 Prompt Caching |
| **Anthropic Claude** | Claude 3.5 Sonnet | 推理能力强，适合复杂分析 |

**选型策略**：
- 默认使用 Azure OpenAI（稳定性优先）
- 复杂推理任务使用 Claude（能力优先）
- 支持配置切换

---

## 4. 模块依赖关系

### 4.1 核心模块依赖图

```
┌─────────────────────────────────────────────────────────────┐
│                      DbOptimizer.API                        │
│  (Controllers, SSE Endpoints, Middleware)                   │
└────────────────────────┬────────────────────────────────────┘
                         │
         ┌───────────────┴───────────────┐
         │                               │
┌────────▼────────┐             ┌───────▼──────────────┐
│ DbOptimizer.    │             │ DbOptimizer.         │
│ Infrastructure  │             │ Core                 │
│                 │             │                      │
│ • Workflows     │────────────→│ • Models             │
│ • Executors     │             │ • Domain Objects     │
│ • Persistence   │             │                      │
│ • MCP Clients   │             │                      │
│ • Checkpointing │             │                      │
└─────────────────┘             └──────────────────────┘
```

### 4.2 依赖规则

**允许的依赖**：
- API → Infrastructure, Core
- Infrastructure → Core

**禁止的依赖**：
- Core → Infrastructure（Core 保持纯净，无基础设施依赖）
- Core → API

---

## 5. 与其他文档的映射关系

- **需求文档**：[REQUIREMENTS.md](./REQUIREMENTS.md)
- **Workflow 设计**：[WORKFLOW_DESIGN.md](./WORKFLOW_DESIGN.md)
- **数据模型**：[DATA_MODEL.md](./DATA_MODEL.md)
- **MCP 集成**：[MCP_INTEGRATION.md](./MCP_INTEGRATION.md)
- **安全设计**：[SECURITY_DESIGN.md](./SECURITY_DESIGN.md)
- **部署架构**：[DEPLOYMENT.md](./DEPLOYMENT.md)
- **前端架构**：[FRONTEND_ARCHITECTURE.md](./FRONTEND_ARCHITECTURE.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
