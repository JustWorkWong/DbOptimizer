# 开发环境搭建指南

**项目名称**：DbOptimizer - AI 驱动的数据库性能优化平台  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [前置要求](#1-前置要求)
2. [克隆仓库](#2-克隆仓库)
3. [后端环境配置](#3-后端环境配置)
4. [前端环境配置](#4-前端环境配置)
5. [MCP 服务器配置](#5-mcp-服务器配置)
6. [启动项目](#6-启动项目)
7. [常见问题排查](#7-常见问题排查)

---

## 1. 前置要求

### 1.1 必需软件

| 软件 | 版本要求 | 下载地址 |
|------|---------|---------|
| .NET SDK | 10.0+ | https://dotnet.microsoft.com/download |
| Node.js | 20.0+ | https://nodejs.org/ |
| PostgreSQL | 16.0+ | https://www.postgresql.org/download/ |
| Redis | 7.0+ | https://redis.io/download |
| Docker | 24.0+ | https://www.docker.com/get-started |
| Git | 2.40+ | https://git-scm.com/downloads |

### 1.2 验证安装

```bash
# 验证 .NET SDK
dotnet --version
# 预期输出：10.0.x

# 验证 Node.js
node --version
# 预期输出：v20.x.x

# 验证 npm
npm --version
# 预期输出：10.x.x

# 验证 PostgreSQL
psql --version
# 预期输出：psql (PostgreSQL) 16.x

# 验证 Redis
redis-cli --version
# 预期输出：redis-cli 7.x.x

# 验证 Docker
docker --version
# 预期输出：Docker version 24.x.x
```

### 1.3 推荐工具

- **IDE**：Visual Studio 2022 / JetBrains Rider / VS Code
- **数据库客户端**：DBeaver / pgAdmin / DataGrip
- **API 测试**：Postman / Insomnia / Bruno
- **Git 客户端**：GitHub Desktop / GitKraken / SourceTree

---

## 2. 克隆仓库

```bash
# 克隆项目
git clone https://github.com/your-org/DbOptimizer.git
cd DbOptimizer

# 查看项目结构
tree -L 2 src/
```

**预期输出**：
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

## 3. 后端环境配置

### 3.1 创建数据库

```bash
# 连接到 PostgreSQL
psql -U postgres

# 创建数据库
CREATE DATABASE dboptimizer;

# 创建用户（可选）
CREATE USER dboptimizer_user WITH PASSWORD 'your_password';
GRANT ALL PRIVILEGES ON DATABASE dboptimizer TO dboptimizer_user;

# 退出
\q
```

### 3.2 配置 appsettings.json

```bash
# 复制配置模板
cd src/DbOptimizer.API
cp appsettings.Development.json.example appsettings.Development.json
```

**编辑 `appsettings.Development.json`**：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=dboptimizer;Username=postgres;Password=your_password",
    "Redis": "localhost:6379"
  },
  "AI": {
    "Provider": "Anthropic",
    "ApiKey": "sk-ant-api03-...",
    "Model": "claude-sonnet-4-6",
    "MaxTokens": 4096
  },
  "MCP": {
    "MySql": {
      "ServerPath": "/path/to/mysql-mcp-server",
      "Timeout": 30000
    },
    "PostgreSql": {
      "ServerPath": "/path/to/postgresql-mcp-server",
      "Timeout": 30000
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**重要**：
- 将 `your_password` 替换为实际密码
- 将 `sk-ant-api03-...` 替换为实际的 Anthropic API Key
- 将 MCP 服务器路径替换为实际路径（见 [5. MCP 服务器配置](#5-mcp-服务器配置)）

### 3.3 运行数据库迁移

```bash
# 安装 EF Core 工具（首次）
dotnet tool install --global dotnet-ef

# 进入 Infrastructure 项目
cd src/DbOptimizer.Infrastructure

# 创建迁移
dotnet ef migrations add InitialCreate --startup-project ../DbOptimizer.API

# 应用迁移
dotnet ef database update --startup-project ../DbOptimizer.API
```

**验证迁移**：

```bash
# 连接数据库
psql -U postgres -d dboptimizer

# 查看表
\dt

# 预期输出：
# workflow_sessions
# agent_executions
# tool_calls
# decision_records
# agent_messages
# prompt_versions
# ...
```

### 3.4 启动 Redis

```bash
# 使用 Docker 启动 Redis
docker run -d --name redis -p 6379:6379 redis:7-alpine

# 验证 Redis
redis-cli ping
# 预期输出：PONG
```

### 3.5 恢复 NuGet 包

```bash
# 返回项目根目录
cd ../..

# 恢复所有项目的依赖
dotnet restore
```

---

## 4. 前端环境配置

### 4.1 安装依赖

```bash
# 进入前端项目
cd src/DbOptimizer.Web

# 安装依赖
npm install
```

### 4.2 配置环境变量

```bash
# 复制环境变量模板
cp .env.example .env.development
```

**编辑 `.env.development`**：

```bash
# API 基础地址
VITE_API_BASE_URL=http://localhost:5000

# SSE 端点
VITE_SSE_BASE_URL=http://localhost:5000

# 是否启用 Mock 数据
VITE_ENABLE_MOCK=false

# 日志级别
VITE_LOG_LEVEL=debug
```

### 4.3 验证前端配置

```bash
# 检查 package.json
cat package.json | grep -A 5 "dependencies"

# 预期输出：
# "dependencies": {
#   "vue": "^3.4.0",
#   "element-plus": "^2.5.0",
#   "pinia": "^2.1.0",
#   "monaco-editor": "^0.45.0",
#   ...
# }
```

---

## 5. MCP 服务器配置

### 5.1 安装 MySQL MCP 服务器

```bash
# 使用 npm 全局安装
npm install -g @modelcontextprotocol/server-mysql

# 验证安装
which mysql-mcp-server
# 预期输出：/usr/local/bin/mysql-mcp-server（或类似路径）
```

### 5.2 安装 PostgreSQL MCP 服务器

```bash
# 使用 npm 全局安装
npm install -g @modelcontextprotocol/server-postgresql

# 验证安装
which postgresql-mcp-server
# 预期输出：/usr/local/bin/postgresql-mcp-server（或类似路径）
```

### 5.3 配置 MCP 连接

**更新 `appsettings.Development.json` 中的 MCP 路径**：

```json
{
  "MCP": {
    "MySql": {
      "ServerPath": "/usr/local/bin/mysql-mcp-server",
      "Timeout": 30000,
      "Args": ["--host", "localhost", "--port", "3306"]
    },
    "PostgreSql": {
      "ServerPath": "/usr/local/bin/postgresql-mcp-server",
      "Timeout": 30000,
      "Args": ["--host", "localhost", "--port", "5432"]
    }
  }
}
```

### 5.4 测试 MCP 连接

```bash
# 测试 MySQL MCP
mysql-mcp-server --host localhost --port 3306 --user root --password your_password

# 测试 PostgreSQL MCP
postgresql-mcp-server --host localhost --port 5432 --user postgres --password your_password
```

---

## 6. 启动项目

### 6.1 使用 Aspire 启动（推荐）

```bash
# 返回项目根目录
cd ../..

# 启动 Aspire 编排
cd src/DbOptimizer.AppHost
dotnet run
```

**Aspire Dashboard**：
- 访问：http://localhost:15000
- 查看所有服务状态、日志、指标

**服务端点**：
- API：http://localhost:5000
- 前端：http://localhost:5173

### 6.2 手动启动（开发调试）

**终端 1 - 启动后端**：

```bash
cd src/DbOptimizer.API
dotnet run
```

**终端 2 - 启动前端**：

```bash
cd src/DbOptimizer.Web
npm run dev
```

**终端 3 - 启动 Redis（如未启动）**：

```bash
docker start redis
```

### 6.3 验证启动

**检查后端**：

```bash
# 健康检查
curl http://localhost:5000/health

# 预期输出：
# {"status":"Healthy","checks":[...]}
```

**检查前端**：

访问 http://localhost:5173，应该看到 DbOptimizer 登录页面。

**检查 SSE 连接**：

```bash
# 测试 SSE 端点
curl -N http://localhost:5000/api/workflows/test-session/events

# 预期输出：持续的 SSE 事件流
```

---

## 7. 常见问题排查

### 7.1 数据库连接失败

**错误信息**：
```
Npgsql.NpgsqlException: Connection refused
```

**解决方案**：

```bash
# 检查 PostgreSQL 是否运行
pg_isready -h localhost -p 5432

# 如果未运行，启动 PostgreSQL
# macOS
brew services start postgresql@16

# Linux
sudo systemctl start postgresql

# Windows
net start postgresql-x64-16
```

### 7.2 Redis 连接失败

**错误信息**：
```
StackExchange.Redis.RedisConnectionException: No connection is available
```

**解决方案**：

```bash
# 检查 Redis 是否运行
redis-cli ping

# 如果未运行，启动 Redis
docker start redis

# 或使用本地安装
# macOS
brew services start redis

# Linux
sudo systemctl start redis
```

### 7.3 EF Core 迁移失败

**错误信息**：
```
Build failed. Use dotnet build to see the errors.
```

**解决方案**：

```bash
# 清理并重新构建
dotnet clean
dotnet build

# 重新运行迁移
cd src/DbOptimizer.Infrastructure
dotnet ef database update --startup-project ../DbOptimizer.API
```

### 7.4 前端依赖安装失败

**错误信息**：
```
npm ERR! code ERESOLVE
```

**解决方案**：

```bash
# 清理 npm 缓存
npm cache clean --force

# 删除 node_modules 和 package-lock.json
rm -rf node_modules package-lock.json

# 重新安装
npm install
```

### 7.5 MCP 服务器无法启动

**错误信息**：
```
MCP server process exited with code 1
```

**解决方案**：

```bash
# 检查 MCP 服务器路径
which mysql-mcp-server
which postgresql-mcp-server

# 手动测试 MCP 服务器
mysql-mcp-server --help
postgresql-mcp-server --help

# 更新 appsettings.json 中的路径
```

### 7.6 Aspire Dashboard 无法访问

**错误信息**：
```
This site can't be reached
```

**解决方案**：

```bash
# 检查 Aspire 是否正在运行
dotnet run --project src/DbOptimizer.AppHost

# 检查端口是否被占用
netstat -an | grep 15000

# 如果端口被占用，修改 Program.cs 中的端口
```

### 7.7 SSE 连接断开

**错误信息**：
```
EventSource failed: The connection was closed
```

**解决方案**：

1. 检查后端日志是否有异常
2. 验证 SSE 端点是否正常：
   ```bash
   curl -N http://localhost:5000/api/workflows/test-session/events
   ```
3. 检查前端 SSE 重连逻辑（见 `COMPONENT_SPEC.md`）

### 7.8 AI API 调用失败

**错误信息**：
```
Anthropic API error: 401 Unauthorized
```

**解决方案**：

1. 验证 API Key 是否正确
2. 检查 API Key 是否有足够的配额
3. 测试 API 连接：
   ```bash
   curl https://api.anthropic.com/v1/messages \
     -H "x-api-key: $ANTHROPIC_API_KEY" \
     -H "anthropic-version: 2023-06-01" \
     -H "content-type: application/json" \
     -d '{"model":"claude-sonnet-4-6","max_tokens":1024,"messages":[{"role":"user","content":"Hello"}]}'
   ```

---

## 相关文档

- **项目规范**：[CLAUDE.md](../CLAUDE.md)
- **需求文档**：[REQUIREMENTS.md](./REQUIREMENTS.md)
- **架构设计**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
- **C# 编码规范**：[CODING_STANDARDS_CSHARP.md](./CODING_STANDARDS_CSHARP.md)
- **TypeScript 编码规范**：[CODING_STANDARDS_TYPESCRIPT.md](./CODING_STANDARDS_TYPESCRIPT.md)
- **Git 工作流**：[GIT_WORKFLOW.md](./GIT_WORKFLOW.md)

---

## 下一步

环境搭建完成后，建议：

1. 阅读 [REQUIREMENTS.md](./REQUIREMENTS.md) 了解项目需求
2. 阅读 [ARCHITECTURE.md](./ARCHITECTURE.md) 了解系统架构
3. 阅读 [CODING_STANDARDS_CSHARP.md](./CODING_STANDARDS_CSHARP.md) 和 [CODING_STANDARDS_TYPESCRIPT.md](./CODING_STANDARDS_TYPESCRIPT.md) 了解编码规范
4. 运行单元测试验证环境：
   ```bash
   # 后端测试
   dotnet test
   
   # 前端测试
   cd src/DbOptimizer.Web
   npm run test
   ```

---

**文档版本**：v1.0  
**最后更新**：2026-04-15  
**维护者**：tengfengsu
