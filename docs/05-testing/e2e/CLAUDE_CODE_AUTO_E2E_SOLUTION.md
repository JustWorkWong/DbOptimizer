# Claude Code 完全自动化 E2E 测试方案

## 目标

实现 Claude Code 完全自动化：
1. 自动启动所有服务
2. 自动运行测试
3. 自动发现问题
4. 自动修复 Bug

## 核心问题分析

### 当前障碍
- ❌ Aspire 需要交互式终端
- ❌ 后台启动只能启动容器，无法启动 API/前端
- ❌ Playwright 需要服务已经运行

### 解决方案：绕过 Aspire

**不使用 Aspire 编排，改用独立启动**：
1. Docker Compose 启动容器
2. 直接启动 API (`dotnet run`)
3. 直接启动前端 (`npm run dev`)
4. Playwright 测试
5. 自动分析错误并修复

## 方案设计

### 架构

```
┌─────────────────────────────────────────┐
│         Claude Code 自动化流程           │
├─────────────────────────────────────────┤
│                                         │
│  1. 启动 Docker Compose                 │
│     ├─ PostgreSQL                       │
│     ├─ MySQL                            │
│     └─ Redis                            │
│                                         │
│  2. 后台启动 API                        │
│     └─ dotnet run (后台)                │
│                                         │
│  3. 后台启动前端                        │
│     └─ npm run dev (后台)               │
│                                         │
│  4. 等待服务就绪                        │
│     └─ 健康检查轮询                     │
│                                         │
│  5. 运行 Playwright 测试                │
│     └─ 捕获错误和截图                   │
│                                         │
│  6. 分析测试结果                        │
│     ├─ 解析错误日志                     │
│     ├─ 查看截图                         │
│     └─ 查询数据库                       │
│                                         │
│  7. 自动修复 Bug                        │
│     ├─ 定位问题代码                     │
│     ├─ 修改代码                         │
│     └─ 重新测试                         │
│                                         │
│  8. 清理环境                            │
│     └─ 停止所有服务                     │
│                                         │
└─────────────────────────────────────────┘
```

## 实施步骤

### Step 1: 创建 Docker Compose 配置

**文件**: `docker-compose.test.yml`

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: dboptimizer
    ports:
      - "15432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: rootpass
      MYSQL_DATABASE: dboptimizer
    ports:
      - "15306:3306"
    healthcheck:
      test: ["CMD", "mysqladmin", "ping", "-h", "localhost"]
      interval: 5s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    ports:
      - "15379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 5s
      retries: 5
```

### Step 2: 创建自动化测试脚本

**文件**: `tests/DbOptimizer.E2ETests/run-automated-test.sh`

```bash
#!/bin/bash
set -e

echo "=== Claude Code 自动化测试开始 ==="

# 1. 启动 Docker 容器
echo "[1/8] 启动 Docker 容器..."
docker-compose -f ../../docker-compose.test.yml up -d
docker-compose -f ../../docker-compose.test.yml ps

# 2. 等待容器健康
echo "[2/8] 等待容器健康检查..."
timeout 60 bash -c 'until docker-compose -f ../../docker-compose.test.yml ps | grep -q "healthy"; do sleep 2; done'

# 3. 启动 API（后台）
echo "[3/8] 启动 API..."
cd ../../src/DbOptimizer.API
dotnet run > api.log 2>&1 &
API_PID=$!
echo "API PID: $API_PID"
cd -

# 4. 启动前端（后台）
echo "[4/8] 启动前端..."
cd ../../src/DbOptimizer.Web
npm run dev > web.log 2>&1 &
WEB_PID=$!
echo "Web PID: $WEB_PID"
cd -

# 5. 等待服务就绪
echo "[5/8] 等待服务就绪..."
timeout 120 bash -c 'until curl -s http://localhost:8669/health | grep -q "Healthy"; do echo "Waiting for API..."; sleep 5; done'
timeout 60 bash -c 'until curl -s http://localhost:5173 > /dev/null 2>&1; do echo "Waiting for Web..."; sleep 5; done'

echo "✅ 所有服务已就绪"

# 6. 运行测试
echo "[6/8] 运行 Playwright 测试..."
npx playwright test --reporter=json > test-results.json 2>&1 || true

# 7. 分析结果
echo "[7/8] 分析测试结果..."
if grep -q '"status":"passed"' test-results.json; then
    echo "✅ 测试通过"
    EXIT_CODE=0
else
    echo "❌ 测试失败，生成错误报告..."
    npx playwright show-report
    EXIT_CODE=1
fi

# 8. 清理环境
echo "[8/8] 清理环境..."
kill $API_PID $WEB_PID 2>/dev/null || true
docker-compose -f ../../docker-compose.test.yml down

echo "=== 自动化测试完成 ==="
exit $EXIT_CODE
```

### Step 3: 创建 Claude Code 自动化工作流

**文件**: `tests/DbOptimizer.E2ETests/claude-auto-test.md`

```markdown
# Claude Code 自动化测试工作流

## 触发方式

用户说："运行自动化测试" 或 "自动测试并修复"

## 执行流程

### 1. 启动测试环境
\`\`\`bash
cd tests/DbOptimizer.E2ETests
bash run-automated-test.sh
\`\`\`

### 2. 如果测试失败

#### 2.1 收集错误信息
- 读取 `test-results.json`
- 查看 `playwright-report/` 中的截图
- 读取 `src/DbOptimizer.API/api.log`
- 读取 `src/DbOptimizer.Web/web.log`

#### 2.2 分析错误类型

**前端错误**:
- 检查浏览器控制台错误
- 检查网络请求失败
- 检查 UI 元素缺失

**后端错误**:
- 检查 API 日志中的异常
- 检查数据库连接错误
- 检查业务逻辑错误

**数据库错误**:
- 连接 PostgreSQL 查询数据
- 检查数据一致性
- 检查事务问题

#### 2.3 定位问题代码

使用 Grep 搜索相关代码：
\`\`\`bash
# 搜索错误相关的函数
grep -r "functionName" src/

# 搜索 API 端点
grep -r "/api/endpoint" src/
\`\`\`

#### 2.4 修复代码

使用 Edit 工具修改代码：
- 修复逻辑错误
- 添加错误处理
- 修复数据验证

#### 2.5 重新测试

\`\`\`bash
bash run-automated-test.sh
\`\`\`

#### 2.6 验证修复

如果测试通过：
- 提交代码
- 生成修复报告

如果测试仍失败：
- 重复步骤 2.1-2.5
- 最多重试 3 次

### 3. 生成报告

创建 `test-report.md`：
- 测试结果摘要
- 发现的问题
- 修复的代码
- 测试覆盖率
```

### Step 4: 创建数据库查询工具

**文件**: `tests/DbOptimizer.E2ETests/db-query.sh`

```bash
#!/bin/bash

# PostgreSQL 查询
query_postgres() {
    docker exec -i $(docker ps -qf "name=postgres") \
        psql -U postgres -d dboptimizer -c "$1"
}

# MySQL 查询
query_mysql() {
    docker exec -i $(docker ps -qf "name=mysql") \
        mysql -uroot -prootpass dboptimizer -e "$1"
}

# Redis 查询
query_redis() {
    docker exec -i $(docker ps -qf "name=redis") \
        redis-cli "$@"
}

# 使用示例
case "$1" in
    postgres)
        query_postgres "$2"
        ;;
    mysql)
        query_mysql "$2"
        ;;
    redis)
        query_redis "${@:2}"
        ;;
    *)
        echo "Usage: $0 {postgres|mysql|redis} <query>"
        exit 1
        ;;
esac
```

## Claude Code 使用方式

### 方式 1: 命令触发

用户说：
```
运行自动化测试
```

Claude Code 执行：
```bash
cd tests/DbOptimizer.E2ETests
bash run-automated-test.sh
```

### 方式 2: 测试并修复

用户说：
```
自动测试并修复所有 Bug
```

Claude Code 执行完整流程：
1. 运行测试
2. 分析错误
3. 修复代码
4. 重新测试
5. 生成报告

### 方式 3: 持续监控

用户说：
```
每 10 分钟自动测试一次
```

Claude Code 使用 Monitor 工具：
```bash
while true; do
    bash run-automated-test.sh
    sleep 600
done
```

## 优势

### vs Aspire 方案
- ✅ 完全后台运行
- ✅ 不需要交互式终端
- ✅ 可以完全自动化
- ✅ 更简单的服务管理

### vs 手动测试
- ✅ 无需人工介入
- ✅ 自动发现问题
- ✅ 自动修复 Bug
- ✅ 持续集成友好

## 限制

1. **不使用 Aspire Dashboard**: 失去可视化监控
2. **需要 Docker**: 必须安装 Docker Desktop
3. **端口固定**: 不支持动态端口分配

## 下一步

1. 创建 `docker-compose.test.yml`
2. 创建 `run-automated-test.sh`
3. 测试完整流程
4. 优化错误分析逻辑
5. 添加更多测试用例
