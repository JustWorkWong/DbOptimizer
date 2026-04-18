# 部署指南

**创建日期**: 2026-04-18  
**版本**: 1.0  
**目标环境**: 测试环境 / 生产环境

---

## 1. 前置条件

### 1.1 基础设施要求

| 组件 | 版本要求 | 用途 |
|------|---------|------|
| .NET Runtime | 10.0+ | 应用运行时 |
| PostgreSQL | 13+ | 主数据库 |
| Redis | 6.0+ | 缓存和会话存储 |
| Docker | 20.10+ (可选) | Aspire 编排 |

### 1.2 网络要求
- PostgreSQL 端口: 5432
- Redis 端口: 6379
- API 端口: 5000 (HTTP) / 5001 (HTTPS)
- Aspire Dashboard 端口: 18888

### 1.3 权限要求
- PostgreSQL 数据库创建权限
- Redis 读写权限
- 文件系统读写权限（日志目录）

---

## 2. 环境配置

### 2.1 环境变量清单

```bash
# 数据库配置
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=dboptimizer;Username=dbopt_user;Password=<password>"

# Redis 配置
export ConnectionStrings__Redis="localhost:6379,password=<password>,ssl=false"

# AI 服务配置
export AI__Provider="OpenAI"  # 或 "AzureOpenAI"
export AI__ApiKey="<your-api-key>"
export AI__Endpoint="https://api.openai.com/v1"  # OpenAI
# export AI__Endpoint="https://<resource>.openai.azure.com"  # Azure OpenAI
export AI__ModelName="gpt-4"

# MCP 配置
export MCP__MySql__Enabled="true"
export MCP__PostgreSql__Enabled="true"

# 日志配置
export Logging__LogLevel__Default="Information"
export Logging__LogLevel__Microsoft="Warning"
export Logging__LogLevel__DbOptimizer="Debug"

# Aspire 配置
export ASPIRE_ALLOW_UNSECURED_TRANSPORT="true"  # 仅开发环境
export DOTNET_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:18889"
```

### 2.2 配置文件

**appsettings.Production.json**:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "DbOptimizer": "Information"
    }
  },
  "AllowedHosts": "*",
  "Workflow": {
    "CheckpointCompressionEnabled": true,
    "CheckpointCompressionThreshold": 10240,
    "MaxConcurrentSessions": 100
  },
  "Redis": {
    "CheckpointTtlMinutes": 1440
  }
}
```

---

## 3. 数据库部署

### 3.1 创建数据库

```sql
-- 创建数据库
CREATE DATABASE dboptimizer
    WITH 
    OWNER = dbopt_user
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.UTF-8'
    LC_CTYPE = 'en_US.UTF-8'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

-- 授权
GRANT ALL PRIVILEGES ON DATABASE dboptimizer TO dbopt_user;
```

### 3.2 运行迁移

```bash
# 进入 API 项目目录
cd src/DbOptimizer.API

# 应用迁移
dotnet ef database update --project ../DbOptimizer.Infrastructure

# 验证迁移
dotnet ef migrations list --project ../DbOptimizer.Infrastructure
```

### 3.3 验证数据库

```sql
-- 检查表是否创建
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public'
ORDER BY table_name;

-- 预期表清单:
-- - workflow_sessions
-- - review_tasks
-- - slow_queries
-- - database_configs
-- - __EFMigrationsHistory
```

---

## 4. 应用部署

### 4.1 构建应用

```bash
# 清理构建
dotnet clean

# 发布生产版本
dotnet publish src/DbOptimizer.AppHost/DbOptimizer.AppHost.csproj \
    -c Release \
    -o ./publish \
    --self-contained false

# 验证构建
ls -lh ./publish
```

### 4.2 部署文件

```bash
# 复制到部署目录
sudo mkdir -p /opt/dboptimizer
sudo cp -r ./publish/* /opt/dboptimizer/

# 设置权限
sudo chown -R dbopt_user:dbopt_group /opt/dboptimizer
sudo chmod +x /opt/dboptimizer/DbOptimizer.AppHost
```

### 4.3 配置 systemd 服务

**创建服务文件**: `/etc/systemd/system/dboptimizer.service`

```ini
[Unit]
Description=DbOptimizer Service
After=network.target postgresql.service redis.service

[Service]
Type=notify
User=dbopt_user
Group=dbopt_group
WorkingDirectory=/opt/dboptimizer
ExecStart=/opt/dboptimizer/DbOptimizer.AppHost
Restart=on-failure
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=dboptimizer
Environment="ASPNETCORE_ENVIRONMENT=Production"
EnvironmentFile=/etc/dboptimizer/environment

[Install]
WantedBy=multi-user.target
```

**创建环境文件**: `/etc/dboptimizer/environment`

```bash
ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=dboptimizer;Username=dbopt_user;Password=<password>
ConnectionStrings__Redis=localhost:6379,password=<password>
AI__Provider=OpenAI
AI__ApiKey=<your-api-key>
AI__Endpoint=https://api.openai.com/v1
AI__ModelName=gpt-4
```

### 4.4 启动服务

```bash
# 重载 systemd
sudo systemctl daemon-reload

# 启动服务
sudo systemctl start dboptimizer

# 检查状态
sudo systemctl status dboptimizer

# 查看日志
sudo journalctl -u dboptimizer -f

# 设置开机自启
sudo systemctl enable dboptimizer
```

---

## 5. 验证部署

### 5.1 健康检查

```bash
# API 健康检查
curl http://localhost:5000/health

# 预期响应: {"status":"Healthy"}

# 详细健康检查
curl http://localhost:5000/health/ready

# 预期响应包含:
# - PostgreSQL: Healthy
# - Redis: Healthy
```

### 5.2 功能验证

```bash
# 1. 提交 SQL 分析任务
curl -X POST http://localhost:5000/api/workflows/sql \
  -H "Content-Type: application/json" \
  -d '{
    "sqlText": "SELECT * FROM users WHERE age > 30",
    "databaseId": "test-db",
    "databaseEngine": "postgresql"
  }'

# 预期响应: {"sessionId":"<guid>","status":"Running"}

# 2. 查询会话状态
curl http://localhost:5000/api/workflows/sessions/<session-id>

# 3. 订阅 SSE 事件
curl -N http://localhost:5000/api/workflows/<session-id>/events
```

### 5.3 监控验证

```bash
# 访问 Aspire Dashboard
open http://localhost:18888

# 检查指标:
# - Workflow 执行时间
# - Checkpoint 保存时间
# - 错误率
# - 并发会话数
```

---

## 6. 日志配置

### 6.1 日志目录

```bash
# 创建日志目录
sudo mkdir -p /var/log/dboptimizer
sudo chown dbopt_user:dbopt_group /var/log/dboptimizer
```

### 6.2 日志轮转

**创建配置**: `/etc/logrotate.d/dboptimizer`

```
/var/log/dboptimizer/*.log {
    daily
    rotate 30
    compress
    delaycompress
    notifempty
    create 0640 dbopt_user dbopt_group
    sharedscripts
    postrotate
        systemctl reload dboptimizer > /dev/null 2>&1 || true
    endscript
}
```

---

## 7. 性能调优

### 7.1 PostgreSQL 优化

```sql
-- 连接池配置
ALTER SYSTEM SET max_connections = 200;
ALTER SYSTEM SET shared_buffers = '256MB';
ALTER SYSTEM SET effective_cache_size = '1GB';
ALTER SYSTEM SET work_mem = '16MB';

-- 重载配置
SELECT pg_reload_conf();
```

### 7.2 Redis 优化

```bash
# 编辑 redis.conf
maxmemory 512mb
maxmemory-policy allkeys-lru
save ""  # 禁用 RDB 持久化（仅缓存用途）
```

### 7.3 应用优化

```json
// appsettings.Production.json
{
  "Workflow": {
    "CheckpointCompressionEnabled": true,
    "CheckpointCompressionThreshold": 10240,
    "MaxConcurrentSessions": 100
  },
  "Redis": {
    "CheckpointTtlMinutes": 1440
  }
}
```

---

## 8. 安全加固

### 8.1 网络安全

```bash
# 配置防火墙（仅允许必要端口）
sudo ufw allow 5000/tcp  # API
sudo ufw allow 5001/tcp  # API HTTPS
sudo ufw deny 5432/tcp   # PostgreSQL（仅内网）
sudo ufw deny 6379/tcp   # Redis（仅内网）
```

### 8.2 HTTPS 配置

```bash
# 生成证书（生产环境使用 Let's Encrypt）
sudo certbot certonly --standalone -d dboptimizer.example.com

# 配置 Kestrel
# appsettings.Production.json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://*:5001",
        "Certificate": {
          "Path": "/etc/letsencrypt/live/dboptimizer.example.com/fullchain.pem",
          "KeyPath": "/etc/letsencrypt/live/dboptimizer.example.com/privkey.pem"
        }
      }
    }
  }
}
```

### 8.3 依赖包安全

```bash
# 升级 MessagePack（修复已知漏洞）
dotnet add package MessagePack --version 2.5.187

# 扫描依赖包漏洞
dotnet list package --vulnerable
```

---

## 9. 故障排查

### 9.1 常见问题

**问题 1**: 服务无法启动

```bash
# 检查日志
sudo journalctl -u dboptimizer -n 100

# 检查端口占用
sudo netstat -tulpn | grep 5000

# 检查配置文件
cat /etc/dboptimizer/environment
```

**问题 2**: 数据库连接失败

```bash
# 测试连接
psql -h localhost -U dbopt_user -d dboptimizer

# 检查 PostgreSQL 日志
sudo tail -f /var/log/postgresql/postgresql-13-main.log
```

**问题 3**: Redis 连接失败

```bash
# 测试连接
redis-cli -h localhost -p 6379 -a <password> ping

# 检查 Redis 日志
sudo tail -f /var/log/redis/redis-server.log
```

### 9.2 诊断命令

```bash
# 检查服务状态
sudo systemctl status dboptimizer

# 检查进程
ps aux | grep DbOptimizer

# 检查端口监听
sudo ss -tulpn | grep -E '5000|5001'

# 检查磁盘空间
df -h

# 检查内存使用
free -h
```

---

## 10. 部署检查清单

### 10.1 部署前
- [ ] 环境变量配置完整
- [ ] 数据库已创建
- [ ] Redis 可访问
- [ ] 证书已配置（生产环境）
- [ ] 防火墙规则已配置

### 10.2 部署中
- [ ] 应用构建成功
- [ ] 文件复制完整
- [ ] 权限设置正确
- [ ] systemd 服务已创建
- [ ] 数据库迁移已应用

### 10.3 部署后
- [ ] 服务启动成功
- [ ] 健康检查通过
- [ ] 功能验证通过
- [ ] 监控正常
- [ ] 日志正常输出

---

**最后更新**: 2026-04-18  
**负责人**: AI Agent  
**审核人**: 待定
