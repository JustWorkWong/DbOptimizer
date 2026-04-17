# 部署架构

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [Aspire 编排](#1-aspire-编排)
2. [Docker 部署](#2-docker-部署)
3. [生产环境配置](#3-生产环境配置)
4. [运维指南](#4-运维指南)

---

## 1. Aspire 编排

### 1.1 Aspire 概述

**.NET Aspire** 是微软推出的云原生应用编排框架，用于简化分布式应用的开发和部署。

**核心优势**：
- **本地开发体验**：一键启动所有依赖（PostgreSQL、Redis）
- **服务发现**：自动配置服务间通信
- **可观测性**：内置 Dashboard，实时监控
- **生产就绪**：支持导出 Docker Compose / Kubernetes 配置

### 1.2 AppHost 配置

```csharp
// DbOptimizer.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加 PostgreSQL
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var db = postgres.AddDatabase("dboptimizer");

// 添加 Redis
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// 添加 API 项目
var api = builder.AddProject<Projects.DbOptimizer_API>("api")
    .WithReference(db)
    .WithReference(redis)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

// 添加前端项目
var web = builder.AddNpmApp("web", "../DbOptimizer.Web")
    .WithReference(api)
    .WithHttpEndpoint(port: 5173, env: "PORT");

builder.Build().Run();
```

### 1.3 启动命令

```bash
# 启动 Aspire 编排
cd src/DbOptimizer.AppHost
dotnet run

# 访问 Aspire Dashboard
# http://localhost:15000
```

### 1.4 Aspire Dashboard

**功能**：
- 查看所有服务状态
- 实时日志流
- 分布式追踪
- 性能指标

---

## 2. Docker 部署

### 2.1 Dockerfile

#### 2.1.1 API Dockerfile

```dockerfile
# DbOptimizer.API/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["DbOptimizer.API/DbOptimizer.API.csproj", "DbOptimizer.API/"]
COPY ["DbOptimizer.Core/DbOptimizer.Core.csproj", "DbOptimizer.Core/"]
COPY ["DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj", "DbOptimizer.Infrastructure/"]
COPY ["DbOptimizer.Shared/DbOptimizer.Shared.csproj", "DbOptimizer.Shared/"]
RUN dotnet restore "DbOptimizer.API/DbOptimizer.API.csproj"

COPY . .
WORKDIR "/src/DbOptimizer.API"
RUN dotnet build "DbOptimizer.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DbOptimizer.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DbOptimizer.API.dll"]
```

#### 2.1.2 前端 Dockerfile

```dockerfile
# DbOptimizer.Web/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

### 2.2 Docker Compose

```yaml
# docker-compose.yml
version: '3.8'

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: dboptimizer
      POSTGRES_USER: dboptimizer
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dboptimizer"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    volumes:
      - redis_data:/data
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  api:
    build:
      context: .
      dockerfile: DbOptimizer.API/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=dboptimizer;Username=dboptimizer;Password=${POSTGRES_PASSWORD}"
      ConnectionStrings__Redis: "redis:6379"
      AI__Provider: "AzureOpenAI"
      AI__AzureOpenAI__Endpoint: ${AZURE_OPENAI_ENDPOINT}
      AI__AzureOpenAI__ApiKey: ${AZURE_OPENAI_API_KEY}
      AI__AzureOpenAI__DeploymentName: ${AZURE_OPENAI_DEPLOYMENT}
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    ports:
      - "8080:8080"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  web:
    build:
      context: ./DbOptimizer.Web
      dockerfile: Dockerfile
    depends_on:
      - api
    ports:
      - "80:80"
    environment:
      VITE_API_BASE_URL: http://api:8080

volumes:
  postgres_data:
  redis_data:
```

### 2.3 启动命令

```bash
# 启动所有服务
docker-compose up -d

# 查看日志
docker-compose logs -f api

# 停止服务
docker-compose down

# 清理数据卷
docker-compose down -v
```

---

## 3. 生产环境配置

### 3.1 环境变量

```bash
# .env.production
POSTGRES_PASSWORD=your_secure_password
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_API_KEY=your_api_key
AZURE_OPENAI_DEPLOYMENT=gpt-4

# MCP 配置
MCP_MYSQL_SERVER_URL=http://mysql-mcp:3000
MCP_POSTGRESQL_SERVER_URL=http://postgresql-mcp:3001

# 加密密钥
ENCRYPTION_KEY=your_base64_encoded_key
ENCRYPTION_IV=your_base64_encoded_iv
```

### 3.2 Nginx 配置

```nginx
# nginx.conf
events {
    worker_connections 1024;
}

http {
    include mime.types;
    default_type application/octet-stream;

    upstream api {
        server api:8080;
    }

    server {
        listen 80;
        server_name dboptimizer.example.com;

        # 前端静态文件
        location / {
            root /usr/share/nginx/html;
            try_files $uri $uri/ /index.html;
        }

        # API 代理
        location /api/ {
            proxy_pass http://api/;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            
            # SSE 支持
            proxy_buffering off;
            proxy_cache off;
            proxy_read_timeout 86400s;
        }
    }
}
```

### 3.3 数据库迁移

```bash
# 生成迁移
dotnet ef migrations add InitialCreate --project DbOptimizer.Infrastructure --startup-project DbOptimizer.API

# 应用迁移
dotnet ef database update --project DbOptimizer.Infrastructure --startup-project DbOptimizer.API

# 生产环境自动迁移
# Program.cs
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

---

## 4. 运维指南

### 4.1 健康检查

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection"))
    .AddRedis(builder.Configuration.GetConnectionString("Redis"))
    .AddCheck<McpHealthCheck>("mcp");

app.MapHealthChecks("/health");
```

### 4.2 日志配置

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "/var/log/dboptimizer/log-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

### 4.3 监控指标

**关键指标**：
- Workflow 成功率
- 平均执行时间
- Token 消耗
- MCP 调用延迟
- 数据库连接池状态

```csharp
// 使用 Prometheus
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddPrometheusExporter();
        metrics.AddMeter("DbOptimizer.Workflows");
    });
```

### 4.4 备份策略

**PostgreSQL 备份**：
```bash
# 每日备份
0 2 * * * docker exec postgres pg_dump -U dboptimizer dboptimizer > /backup/dboptimizer_$(date +\%Y\%m\%d).sql

# 保留 30 天
find /backup -name "dboptimizer_*.sql" -mtime +30 -delete
```

**Redis 备份**：
```bash
# 启用 AOF
redis-cli CONFIG SET appendonly yes
redis-cli CONFIG SET appendfsync everysec
```

### 4.5 故障排查

**常见问题**：

| 问题 | 排查步骤 |
|------|---------|
| **API 无响应** | 检查日志、数据库连接、Redis 连接 |
| **SSE 断开** | 检查 Nginx 配置、网络稳定性 |
| **Workflow 卡住** | 查看 Checkpoint 状态、Agent 执行记录 |
| **MCP 超时** | 检查 MCP Server 状态、网络延迟 |

**日志查询**：
```bash
# 查看最近 100 条错误日志
docker-compose logs --tail=100 api | grep ERROR

# 实时跟踪日志
docker-compose logs -f api

# 查看特定 SessionId 的日志
docker-compose logs api | grep "session_id=abc123"
```

---

## 附录

### A. 部署拓扑

```
┌─────────────────────────────────────────────────────────┐
│                    Internet                             │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│                 Nginx (Reverse Proxy)                   │
│                 - SSL Termination                       │
│                 - Load Balancing                        │
└────────────────────┬────────────────────────────────────┘
                     │
         ┌───────────┴───────────┐
         │                       │
         ▼                       ▼
┌─────────────────┐     ┌─────────────────┐
│   API Server    │     │   Web Server    │
│   (ASP.NET)     │     │   (Nginx)       │
└────────┬────────┘     └─────────────────┘
         │
    ┌────┴────┐
    │         │
    ▼         ▼
┌────────┐ ┌────────┐
│Postgres│ │ Redis  │
└────────┘ └────────┘
```

### B. 容量规划

| 资源 | 最小配置 | 推荐配置 |
|------|---------|---------|
| **API Server** | 2 CPU, 4GB RAM | 4 CPU, 8GB RAM |
| **PostgreSQL** | 2 CPU, 4GB RAM | 4 CPU, 8GB RAM |
| **Redis** | 1 CPU, 2GB RAM | 2 CPU, 4GB RAM |
| **存储** | 50GB | 200GB |
