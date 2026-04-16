# DbOptimizer 问题诊断与解决方案

## 问题 1: Aspire Dashboard 无遥测数据

### 根本原因
API 项目未配置 OpenTelemetry，Aspire Dashboard 无法接收 Logs/Metrics/Traces。

### 解决方案
已添加 OpenTelemetry 配置到 `DbOptimizer.API/Program.cs`：

```csharp
// 添加的 NuGet 包
- OpenTelemetry.Exporter.OpenTelemetryProtocol (1.11.0)
- OpenTelemetry.Extensions.Hosting (1.11.0)
- OpenTelemetry.Instrumentation.AspNetCore (1.11.0)
- OpenTelemetry.Instrumentation.Http (1.11.0)
- OpenTelemetry.Instrumentation.Runtime (1.11.0)
- OpenTelemetry.Instrumentation.EntityFrameworkCore (1.10.0-beta.1)

// 配置代码
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("DbOptimizer.API", serviceVersion: "1.0.0"));
    logging.AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri(otlpEndpoint);
    });
});

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => ...)
    .WithTracing(tracing => ...);
```

### 验证方法
1. 重启 Aspire 应用
2. 访问 Aspire Dashboard (http://localhost:15888)
3. 查看 Structured logs / Metrics / Traces 页面

---

## 问题 2: SQL 调优测试数据不可用

### 根本原因
之前的测试 SQL 查询的是 DbOptimizer 自己的元数据表 `agent_executions`，不是真实业务场景。

### 解决方案
创建了真实的测试数据库和慢查询场景：

#### MySQL 测试数据 (002_test_data.sql)
- **场景**: 电商订单系统
- **表结构**: 
  - `orders` (10000 条订单，无索引)
  - `order_items` (30000 条订单明细)
- **慢查询示例**:
  ```sql
  -- 按用户查询订单（缺少 user_id 索引）
  SELECT * FROM orders WHERE user_id = 123;
  
  -- 按状态和时间范围查询（缺少复合索引）
  SELECT * FROM orders 
  WHERE status = 'pending' 
    AND created_at >= '2025-01-01';
  
  -- 订单关联查询（缺少外键索引）
  SELECT o.*, oi.* 
  FROM orders o 
  JOIN order_items oi ON o.order_id = oi.order_id 
  WHERE o.user_id = 123;
  ```

#### PostgreSQL 测试数据 (002_test_data.sql)
- **场景**: 用户行为日志分析系统
- **表结构**:
  - `users` (1000 个用户)
  - `user_events` (50000 条事件记录，无索引)
- **慢查询示例**:
  ```sql
  -- 按用户查询事件（缺少 user_id 索引）
  SELECT * FROM user_events WHERE user_id = 123;
  
  -- 按事件类型和时间范围查询（缺少复合索引）
  SELECT * FROM user_events 
  WHERE event_type = 'page_view' 
    AND created_at >= NOW() - INTERVAL '7 days';
  
  -- JSONB 字段查询（缺少 GIN 索引）
  SELECT * FROM user_events 
  WHERE event_data @> '{"success": true}';
  ```

### 验证方法
1. 重启 Aspire 应用（会自动执行初始化脚本）
2. 使用上述慢查询 SQL 进行调优测试
3. 验证生成的索引建议是否合理

---

## 问题 3: MCP Server 调用失败

### 当前状态
从数据库记录看到：
```json
{
  "usedFallback": true,
  "diagnosticTag": "mcp_error",
  "warnings": [
    "未找到 PostgreSQL Plan 节点",
    "未识别出明确的执行计划瓶颈"
  ]
}
```

### 可能原因
1. MCP Server 未正确启动
2. npx 命令执行失败
3. 执行计划解析逻辑有问题

### 排查步骤
1. 检查 MCP Server 配置：
   ```json
   {
     "ExecutionPlan": {
       "PostgreSql": {
         "Enabled": true,
         "Transport": "stdio",
         "Command": "npx",
         "Arguments": "-y @modelcontextprotocol/server-postgres"
       }
     }
   }
   ```

2. 手动测试 MCP Server：
   ```bash
   npx -y @modelcontextprotocol/server-postgres
   ```

3. 查看 API 日志中的详细错误信息

### 降级方案
当前已启用直连数据库降级：
```csharp
ExecutionPlanOptions.EnableDirectDbFallback = true
```

即使 MCP 失败，也会通过直连数据库获取执行计划。

---

## 重启应用步骤

1. **停止当前运行的应用**
   - 在 Aspire Dashboard 中停止所有服务
   - 或者关闭 Visual Studio / Rider 中的调试会话

2. **清理构建产物**（可选）
   ```bash
   dotnet clean
   ```

3. **重新构建**
   ```bash
   cd src/DbOptimizer.AppHost
   dotnet build
   ```

4. **启动应用**
   ```bash
   dotnet run
   ```

5. **验证测试数据**
   - MySQL: `docker exec -it mysql-xxx mysql -uroot -ppostgres dboptimizer -e "SELECT COUNT(*) FROM orders;"`
   - PostgreSQL: `docker exec -it postgres-xxx psql -U postgres -d dboptimizer -c "SELECT COUNT(*) FROM user_events;"`

---

## 测试流程

### 1. 测试 PostgreSQL 慢查询
```bash
curl -X POST http://localhost:5000/api/workflows/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "sqlText": "SELECT * FROM user_events WHERE user_id = 123 AND event_type = '\''page_view'\'' AND created_at >= NOW() - INTERVAL '\''7 days'\''",
    "databaseId": "postgres-local",
    "databaseType": "postgresql"
  }'
```

### 2. 测试 MySQL 慢查询
```bash
curl -X POST http://localhost:5000/api/workflows/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "sqlText": "SELECT o.*, oi.* FROM orders o JOIN order_items oi ON o.order_id = oi.order_id WHERE o.user_id = 123 AND o.status = '\''pending'\''",
    "databaseId": "mysql-local",
    "databaseType": "mysql"
  }'
```

### 3. 验证结果
- 检查 Aspire Dashboard 中的 Logs/Traces
- 查看生成的索引建议
- 验证置信度和证据链

---

## 已知问题

1. **OpenTelemetry.Api 安全漏洞警告**
   - 当前版本 1.11.1 有已知漏洞
   - 建议升级到最新版本（等待官方修复）

2. **构建时文件锁定**
   - 应用运行时无法重新构建
   - 需要先停止应用再构建

---

## 下一步优化

1. 添加自定义 Metrics（SQL 执行时间、索引命中率等）
2. 添加自定义 Traces（Workflow 执行链路）
3. 优化 MCP Server 错误处理和重试逻辑
4. 添加更多真实业务场景的测试数据
