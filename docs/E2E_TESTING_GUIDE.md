# DbOptimizer 端到端测试指南

**创建日期**：2026-04-17  
**版本**：v1.0  
**作者**：tengfengsu

---

## 1. 概述

本文档描述如何配置和执行 DbOptimizer 的端到端（E2E）真实浏览器测试，包括：
- 前端 Vue 应用通过浏览器交互
- 后端 API 处理请求
- 数据库（PostgreSQL/MySQL/Redis）数据验证
- 应用日志和遥测数据查看

---

## 2. 测试架构

```
┌─────────────────┐
│   Playwright    │ ← 自动化浏览器操作
│   Test Runner   │
└────────┬────────┘
         │
         ↓
┌─────────────────┐
│  Vue 3 前端     │ ← http://localhost:5173
│  (Vite Dev)     │
└────────┬────────┘
         │ /api/* 代理
         ↓
┌─────────────────┐
│  ASP.NET Core   │ ← http://localhost:8669
│  API            │
└────────┬────────┘
         │
    ┌────┴────┬────────┬────────┐
    ↓         ↓        ↓        ↓
┌────────┐┌────────┐┌────────┐┌────────┐
│Postgres││ MySQL  ││ Redis  ││ OTLP   │
│:15432  ││ :15306 ││ :15379 ││ :4317  │
└────────┘└────────┘└────────┘└────────┘
```

---

## 3. 前置条件

### 3.1 必需软件

- **.NET 10 SDK**：`dotnet --version` 应显示 10.x
- **Node.js 20+**：`node --version` 应显示 v20.x+
- **Docker Desktop**：用于运行数据库容器（Aspire 自动管理）
- **浏览器**：Chrome/Edge/Firefox（Playwright 会自动下载）

### 3.2 环境配置

创建 `src/DbOptimizer.AppHost/appsettings.Local.json`（已在 .gitignore）：

```json
{
  "DbOptimizer": {
    "Databases": {
      "PostgreSql": {
        "Port": 15432,
        "Username": "postgres",
        "Password": "your_postgres_password",
        "Database": "dboptimizer"
      },
      "MySql": {
        "Port": 15306,
        "Password": "your_mysql_password",
        "Database": "dboptimizer"
      },
      "Redis": {
        "Port": 15379
      }
    }
  }
}
```

**安全提示**：不要提交真实密码到 Git。

---

## 4. 安装 Playwright

### 4.1 添加测试项目

```bash
cd E:\wfcodes\DbOptimizer

# 创建测试项目目录
mkdir -p tests/DbOptimizer.E2ETests
cd tests/DbOptimizer.E2ETests

# 初始化 Playwright 项目
npm init playwright@latest
```

选择配置：
- TypeScript：Yes
- 测试目录：`tests`
- GitHub Actions：No（可选）
- 安装浏览器：Yes

### 4.2 配置 Playwright

编辑 `tests/DbOptimizer.E2ETests/playwright.config.ts`：

```typescript
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false, // 串行执行，避免数据库冲突
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1, // 单线程执行
  reporter: 'html',
  
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  webServer: {
    command: 'echo "请手动启动 Aspire: cd ../../src/DbOptimizer.AppHost && dotnet run"',
    url: 'http://localhost:5173',
    reuseExistingServer: true, // 使用已运行的服务
    timeout: 120 * 1000,
  },
});
```

---

## 5. 启动应用

### 5.1 启动 Aspire（推荐方式）

```bash
cd E:\wfcodes\DbOptimizer\src\DbOptimizer.AppHost
dotnet run
```

**Aspire 会自动启动**：
- PostgreSQL 容器（端口 15432）
- MySQL 容器（端口 15306）
- Redis 容器（端口 15379）
- API 服务（端口 8669）
- Vue 前端（端口 5173）
- Aspire Dashboard（端口 18888）

**等待所有服务启动**（约 30-60 秒），浏览器会自动打开 Aspire Dashboard。

### 5.2 验证服务状态

访问以下 URL 确认服务正常：

| 服务 | URL | 预期结果 |
|------|-----|---------|
| Aspire Dashboard | http://localhost:18888 | 显示所有服务状态 |
| Vue 前端 | http://localhost:5173 | 显示 DbOptimizer 首页 |
| API 健康检查 | http://localhost:8669/health | 返回 `Healthy` |
| API Swagger | http://localhost:8669/swagger | 显示 API 文档 |
| pgAdmin | http://localhost:15050 | PostgreSQL 管理界面 |
| phpMyAdmin | http://localhost:15051 | MySQL 管理界面 |
| RedisInsight | http://localhost:15540 | Redis 管理界面 |

---

## 6. 编写测试用例

### 6.1 示例：SQL 分析流程测试

创建 `tests/DbOptimizer.E2ETests/tests/sql-analysis.spec.ts`：

```typescript
import { test, expect } from '@playwright/test';

test.describe('SQL 分析流程', () => {
  test('用户提交 SQL 并查看分析结果', async ({ page }) => {
    // 1. 访问首页
    await page.goto('/');
    await expect(page.locator('h1')).toContainText('DbOptimizer');

    // 2. 导航到 SQL 分析页面
    await page.click('text=SQL 调优');
    await expect(page).toHaveURL(/.*sql-analysis/);

    // 3. 输入 SQL
    const sqlEditor = page.locator('.monaco-editor textarea').first();
    await sqlEditor.fill('SELECT * FROM users WHERE email = "test@example.com"');

    // 4. 选择数据库类型
    await page.selectOption('select[name="dbType"]', 'mysql');

    // 5. 提交分析
    await page.click('button:has-text("开始分析")');

    // 6. 等待分析完成（SSE 实时更新）
    await page.waitForSelector('text=分析完成', { timeout: 30000 });

    // 7. 验证结果展示
    await expect(page.locator('.analysis-result')).toBeVisible();
    await expect(page.locator('.confidence-score')).toContainText(/\d+%/);
    
    // 8. 验证索引推荐
    const indexRecommendation = page.locator('.index-recommendation').first();
    await expect(indexRecommendation).toContainText('CREATE INDEX');

    // 9. 截图保存
    await page.screenshot({ path: 'test-results/sql-analysis-success.png' });
  });

  test('验证数据库中保存了分析记录', async ({ page, request }) => {
    // 1. 提交 SQL 分析（复用上面的步骤）
    await page.goto('/sql-analysis');
    // ... 省略提交步骤 ...

    // 2. 获取 session ID
    const sessionId = await page.locator('[data-session-id]').getAttribute('data-session-id');

    // 3. 调用 API 验证数据
    const response = await request.get(`http://localhost:8669/api/workflows/${sessionId}`);
    expect(response.ok()).toBeTruthy();
    
    const data = await response.json();
    expect(data.status).toBe('completed');
    expect(data.result).toBeDefined();
  });
});
```

### 6.2 示例：数据库数据验证

创建 `tests/DbOptimizer.E2ETests/tests/database-verification.spec.ts`：

```typescript
import { test, expect } from '@playwright/test';
import { Client } from 'pg'; // npm install pg @types/pg

test.describe('数据库数据验证', () => {
  let pgClient: Client;

  test.beforeAll(async () => {
    // 连接到 PostgreSQL
    pgClient = new Client({
      host: 'localhost',
      port: 15432,
      user: 'postgres',
      password: 'your_postgres_password',
      database: 'dboptimizer',
    });
    await pgClient.connect();
  });

  test.afterAll(async () => {
    await pgClient.end();
  });

  test('验证 workflow_sessions 表记录', async ({ page }) => {
    // 1. 前端提交分析
    await page.goto('/sql-analysis');
    // ... 提交 SQL ...

    // 2. 获取 session ID
    const sessionId = await page.locator('[data-session-id]').textContent();

    // 3. 查询数据库
    const result = await pgClient.query(
      'SELECT * FROM workflow_sessions WHERE session_id = $1',
      [sessionId]
    );

    expect(result.rows.length).toBe(1);
    expect(result.rows[0].status).toBe('completed');
    expect(result.rows[0].workflow_type).toBe('sql_analysis');
  });

  test('验证 agent_executions 表记录', async () => {
    const result = await pgClient.query(
      'SELECT COUNT(*) as count FROM agent_executions WHERE created_at > NOW() - INTERVAL \'1 hour\''
    );

    expect(parseInt(result.rows[0].count)).toBeGreaterThan(0);
  });
});
```

---

## 7. 运行测试

### 7.1 运行所有测试

```bash
cd tests/DbOptimizer.E2ETests
npx playwright test
```

### 7.2 运行单个测试文件

```bash
npx playwright test tests/sql-analysis.spec.ts
```

### 7.3 调试模式（带 UI）

```bash
npx playwright test --ui
```

### 7.4 查看测试报告

```bash
npx playwright show-report
```

---

## 8. 查看应用日志和遥测

### 8.1 Aspire Dashboard

访问 http://localhost:18888，可以查看：

- **Traces**：分布式追踪，查看请求链路
- **Metrics**：性能指标（请求数、延迟、错误率）
- **Logs**：结构化日志，支持过滤和搜索
- **Resources**：所有服务的状态和资源使用

**查看 SQL 分析的完整链路**：
1. 点击 **Traces** 标签
2. 搜索 `POST /api/workflows/sql-analysis`
3. 点击 Trace ID 查看详细时间线
4. 可以看到：API 请求 → Agent 执行 → MCP 调用 → 数据库查询

### 8.2 结构化日志查询

在 Aspire Dashboard 的 **Logs** 标签：

```
# 查看所有 SQL 分析日志
Resource: api
Level: Information
Message contains: SqlAnalysisWorkflow

# 查看错误日志
Level: Error
Time range: Last 1 hour

# 查看特定 Session 的日志
Message contains: session_id=abc123
```

### 8.3 数据库管理工具

**PostgreSQL（pgAdmin）**：
- URL: http://localhost:15050
- 默认邮箱：admin@admin.com
- 默认密码：admin
- 连接信息：
  - Host: postgres（容器内）或 localhost（宿主机）
  - Port: 5432（容器内）或 15432（宿主机）
  - Username: postgres
  - Password: 配置文件中的密码

**MySQL（phpMyAdmin）**：
- URL: http://localhost:15051
- Username: root
- Password: 配置文件中的密码

**Redis（RedisInsight）**：
- URL: http://localhost:15540
- 自动连接到 localhost:15379

---

## 9. 测试数据准备

### 9.1 初始化测试数据

Aspire 会自动执行 `src/DbOptimizer.AppHost/DatabaseInit/` 下的 SQL 脚本：

- `postgresql/init.sql`：PostgreSQL 测试数据
- `mysql/init.sql`：MySQL 测试数据

**示例测试数据**（`postgresql/init.sql`）：

```sql
-- 创建测试用户表
CREATE TABLE IF NOT EXISTS test_users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) NOT NULL,
    name VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 插入测试数据
INSERT INTO test_users (email, name) VALUES
    ('test1@example.com', 'Test User 1'),
    ('test2@example.com', 'Test User 2'),
    ('test3@example.com', 'Test User 3');

-- 创建慢查询场景（无索引）
CREATE TABLE IF NOT EXISTS test_orders (
    id SERIAL PRIMARY KEY,
    user_id INTEGER,
    amount DECIMAL(10, 2),
    status VARCHAR(50),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 插入大量数据（模拟慢查询）
INSERT INTO test_orders (user_id, amount, status)
SELECT 
    (random() * 1000)::INTEGER,
    (random() * 1000)::DECIMAL(10, 2),
    CASE (random() * 3)::INTEGER
        WHEN 0 THEN 'pending'
        WHEN 1 THEN 'completed'
        ELSE 'cancelled'
    END
FROM generate_series(1, 10000);
```

### 9.2 测试前清理数据

创建 `tests/DbOptimizer.E2ETests/tests/setup.ts`：

```typescript
import { test as setup } from '@playwright/test';
import { Client } from 'pg';

setup('清理测试数据', async () => {
  const client = new Client({
    host: 'localhost',
    port: 15432,
    user: 'postgres',
    password: 'your_postgres_password',
    database: 'dboptimizer',
  });

  await client.connect();
  
  // 清理上次测试的数据
  await client.query('TRUNCATE workflow_sessions CASCADE');
  await client.query('TRUNCATE agent_executions CASCADE');
  
  await client.end();
});
```

在 `playwright.config.ts` 中配置：

```typescript
export default defineConfig({
  // ...
  globalSetup: './tests/setup.ts',
});
```

---

## 10. 常见问题排查

### 10.1 服务启动失败

**问题**：`dotnet run` 报错 "端口已被占用"

**解决**：
```bash
# 查找占用端口的进程
netstat -ano | findstr :5173
netstat -ano | findstr :8669

# 杀死进程
taskkill /PID <进程ID> /F
```

### 10.2 数据库连接失败

**问题**：API 日志显示 "Connection refused"

**检查**：
1. Docker Desktop 是否运行
2. Aspire Dashboard 中数据库容器状态是否 "Running"
3. 端口映射是否正确（15432/15306/15379）

**解决**：
```bash
# 重启 Aspire
cd src/DbOptimizer.AppHost
dotnet run
```

### 10.3 前端无法访问 API

**问题**：浏览器控制台显示 "ERR_CONNECTION_REFUSED"

**检查**：
1. Vite 代理配置是否正确（`vite.config.ts`）
2. API 是否启动（访问 http://localhost:8669/health）
3. CORS 配置是否包含前端地址

**解决**：
```bash
# 查看 API 日志
cd src/DbOptimizer.API
dotnet run

# 检查 CORS 配置
# appsettings.json -> DbOptimizer:Cors:AllowedOrigins
```

### 10.4 Playwright 测试超时

**问题**：测试等待元素超时

**调试**：
```bash
# 使用调试模式
npx playwright test --debug

# 增加超时时间
npx playwright test --timeout=60000
```

**检查**：
1. 元素选择器是否正确
2. SSE 事件是否正常推送（浏览器 DevTools -> Network -> EventStream）
3. API 是否返回预期数据

### 10.5 数据库数据不一致

**问题**：测试查询不到预期数据

**检查**：
1. 事务是否提交（EF Core 默认自动提交）
2. 时区问题（PostgreSQL 使用 UTC）
3. 数据是否被其他测试清理

**解决**：
```sql
-- 手动查询验证
SELECT * FROM workflow_sessions ORDER BY created_at DESC LIMIT 10;
SELECT * FROM agent_executions WHERE session_id = 'xxx';
```

---

## 11. CI/CD 集成

### 11.1 GitHub Actions 示例

创建 `.github/workflows/e2e-tests.yml`：

```yaml
name: E2E Tests

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  e2e:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_PASSWORD: test_password
        ports:
          - 15432:5432
      
      mysql:
        image: mysql:8.0
        env:
          MYSQL_ROOT_PASSWORD: test_password
        ports:
          - 15306:3306
      
      redis:
        image: redis:7
        ports:
          - 15379:6379

    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'
      
      - name: Install dependencies
        run: |
          cd src/DbOptimizer.Web
          npm ci
          cd ../../tests/DbOptimizer.E2ETests
          npm ci
      
      - name: Build API
        run: |
          cd src/DbOptimizer.API
          dotnet build
      
      - name: Start API
        run: |
          cd src/DbOptimizer.API
          dotnet run &
          sleep 10
      
      - name: Start Frontend
        run: |
          cd src/DbOptimizer.Web
          npm run dev &
          sleep 5
      
      - name: Run Playwright tests
        run: |
          cd tests/DbOptimizer.E2ETests
          npx playwright test
      
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: playwright-report
          path: tests/DbOptimizer.E2ETests/playwright-report/
```

---

## 12. 最佳实践

### 12.1 测试隔离

- 每个测试使用独立的 session ID
- 测试前清理相关数据
- 避免测试间的数据依赖

### 12.2 等待策略

```typescript
// ❌ 不推荐：固定等待
await page.waitForTimeout(5000);

// ✅ 推荐：等待特定元素
await page.waitForSelector('text=分析完成');

// ✅ 推荐：等待网络请求
await page.waitForResponse(resp => 
  resp.url().includes('/api/workflows') && resp.status() === 200
);
```

### 12.3 截图和录屏

```typescript
test('SQL 分析', async ({ page }) => {
  // 关键步骤截图
  await page.screenshot({ path: 'step1-input.png' });
  
  // 失败时自动截图（配置在 playwright.config.ts）
  // screenshot: 'only-on-failure'
  
  // 录屏（配置在 playwright.config.ts）
  // video: 'retain-on-failure'
});
```

### 12.4 测试数据管理

- 使用 Fixture 准备测试数据
- 测试后清理数据（或使用事务回滚）
- 避免硬编码测试数据

---

## 13. 快速检查清单

启动测试前，确认以下项目：

- [ ] Docker Desktop 正在运行
- [ ] `appsettings.Local.json` 已配置
- [ ] `dotnet run` 在 AppHost 目录执行
- [ ] Aspire Dashboard 显示所有服务 "Running"
- [ ] http://localhost:5173 可访问
- [ ] http://localhost:8669/health 返回 "Healthy"
- [ ] pgAdmin/phpMyAdmin 可以连接数据库
- [ ] Playwright 浏览器已安装（`npx playwright install`）

---

## 14. 总结

本指南提供了完整的端到端测试配置流程：

1. **环境准备**：.NET 10 + Node.js + Docker + Playwright
2. **启动应用**：`dotnet run` 启动 Aspire，自动编排所有服务
3. **编写测试**：使用 Playwright 模拟真实用户操作
4. **数据验证**：直接查询 PostgreSQL/MySQL/Redis 验证数据
5. **日志查看**：Aspire Dashboard 查看 Traces/Metrics/Logs
6. **问题排查**：常见问题和解决方案

**下一步**：
- 编写更多测试用例覆盖核心功能
- 集成到 CI/CD 流程
- 配置测试报告和通知
