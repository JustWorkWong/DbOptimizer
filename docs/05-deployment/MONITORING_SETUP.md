# 监控设置指南

**创建日期**: 2026-04-18  
**版本**: 1.0  
**监控平台**: Aspire Dashboard + OpenTelemetry

---

## 1. 监控架构

### 1.1 监控组件

```
┌─────────────────────────────────────────────────────────┐
│                   Aspire Dashboard                       │
│              (http://localhost:18888)                    │
└─────────────────────────────────────────────────────────┘
                          ▲
                          │ OTLP
                          │
┌─────────────────────────────────────────────────────────┐
│              OpenTelemetry Collector                     │
│  - Traces (分布式追踪)                                    │
│  - Metrics (性能指标)                                     │
│  - Logs (结构化日志)                                      │
└─────────────────────────────────────────────────────────┘
                          ▲
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
┌───────▼──────┐  ┌──────▼──────┐  ┌──────▼──────┐
│ DbOptimizer  │  │ PostgreSQL  │  │   Redis     │
│     API      │  │             │  │             │
└──────────────┘  └─────────────┘  └─────────────┘
```

### 1.2 监控层级

| 层级 | 监控内容 | 工具 |
|------|---------|------|
| 应用层 | Workflow 执行、API 性能、错误率 | OpenTelemetry |
| 数据层 | 数据库查询、连接池、缓存命中率 | EF Core Instrumentation |
| 基础设施层 | CPU、内存、磁盘、网络 | 系统监控 |
| 业务层 | 会话数、审核任务、优化建议 | 自定义指标 |

---

## 2. Aspire Dashboard 配置

### 2.1 启动 Dashboard

```bash
# 方式1: 通过 AppHost 启动（推荐）
cd /opt/dboptimizer
./DbOptimizer.AppHost

# 方式2: 独立启动 Dashboard
dotnet run --project Aspire.Dashboard

# 访问 Dashboard
open http://localhost:18888
```

### 2.2 Dashboard 环境变量

```bash
# 配置 OTLP 端点
export DOTNET_DASHBOARD_OTLP_ENDPOINT_URL="http://localhost:18889"

# 配置 Dashboard 端口
export ASPNETCORE_URLS="http://localhost:18888"

# 允许非安全传输（仅开发环境）
export ASPIRE_ALLOW_UNSECURED_TRANSPORT="true"

# 配置认证（生产环境）
export DASHBOARD_OTLP_AUTH_MODE="ApiKey"
export DASHBOARD_OTLP_PRIMARY_API_KEY="<your-api-key>"
```

### 2.3 Dashboard 功能

| 功能 | 说明 | 用途 |
|------|------|------|
| Resources | 查看所有服务资源 | 服务健康状态 |
| Traces | 分布式追踪 | 请求链路分析 |
| Metrics | 性能指标 | 性能监控 |
| Logs | 结构化日志 | 问题排查 |
| Console | 控制台输出 | 实时日志 |

---

## 3. 关键指标配置

### 3.1 Workflow 指标

```csharp
// 在 WorkflowApplicationService 中添加
private static readonly Histogram<double> WorkflowDuration = 
    Meter.CreateHistogram<double>(
        "dboptimizer.workflow.duration",
        "ms",
        "Workflow execution duration");

private static readonly Counter<long> WorkflowStarted = 
    Meter.CreateCounter<long>(
        "dboptimizer.workflow.started",
        "count",
        "Number of workflows started");

private static readonly Counter<long> WorkflowCompleted = 
    Meter.CreateCounter<long>(
        "dboptimizer.workflow.completed",
        "count",
        "Number of workflows completed");

private static readonly Counter<long> WorkflowFailed = 
    Meter.CreateCounter<long>(
        "dboptimizer.workflow.failed",
        "count",
        "Number of workflows failed");
```

### 3.2 Checkpoint 指标

```csharp
private static readonly Histogram<double> CheckpointSaveDuration = 
    Meter.CreateHistogram<double>(
        "dboptimizer.checkpoint.save_duration",
        "ms",
        "Checkpoint save duration");

private static readonly Histogram<long> CheckpointSize = 
    Meter.CreateHistogram<long>(
        "dboptimizer.checkpoint.size",
        "bytes",
        "Checkpoint size");

private static readonly Counter<long> CheckpointCompressionEnabled = 
    Meter.CreateCounter<long>(
        "dboptimizer.checkpoint.compression_enabled",
        "count",
        "Number of compressed checkpoints");
```

### 3.3 API 指标

```csharp
private static readonly Histogram<double> ApiRequestDuration = 
    Meter.CreateHistogram<double>(
        "dboptimizer.api.request_duration",
        "ms",
        "API request duration");

private static readonly Counter<long> ApiRequestTotal = 
    Meter.CreateCounter<long>(
        "dboptimizer.api.request_total",
        "count",
        "Total API requests");

private static readonly Counter<long> ApiRequestErrors = 
    Meter.CreateCounter<long>(
        "dboptimizer.api.request_errors",
        "count",
        "API request errors");
```

---

## 4. 告警规则

### 4.1 关键告警

| 告警名称 | 条件 | 级别 | 处理 |
|---------|------|------|------|
| 服务不可用 | Health check 失败 >2分钟 | P0 | 立即重启服务 |
| 数据库连接失败 | PostgreSQL 连接失败 >1分钟 | P0 | 检查数据库状态 |
| Redis 连接失败 | Redis 连接失败 >1分钟 | P1 | 检查 Redis 状态 |
| Workflow 失败率高 | 失败率 >10% | P1 | 检查错误日志 |
| API 错误率高 | 错误率 >5% | P1 | 检查 API 日志 |
| 响应时间慢 | P95 >2s | P2 | 性能分析 |

### 4.2 告警配置示例

```yaml
# Prometheus AlertManager 配置示例
groups:
  - name: dboptimizer
    interval: 30s
    rules:
      - alert: WorkflowFailureRateHigh
        expr: |
          rate(dboptimizer_workflow_failed[5m]) / 
          rate(dboptimizer_workflow_started[5m]) > 0.1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Workflow failure rate is high"
          description: "Failure rate is {{ $value | humanizePercentage }}"

      - alert: ApiResponseTimeSlow
        expr: |
          histogram_quantile(0.95, 
            rate(dboptimizer_api_request_duration_bucket[5m])) > 2000
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "API response time is slow"
          description: "P95 latency is {{ $value }}ms"
```

---

## 5. 日志配置

### 5.1 结构化日志

```json
// appsettings.Production.json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "DbOptimizer": "Information"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "/var/log/dboptimizer/log-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

### 5.2 关键日志点

| 日志点 | 级别 | 内容 |
|-------|------|------|
| Workflow 启动 | Information | SessionId, WorkflowType, DatabaseId |
| Workflow 完成 | Information | SessionId, Duration, Status |
| Workflow 失败 | Error | SessionId, ErrorMessage, StackTrace |
| Checkpoint 保存 | Debug | SessionId, Size, Compressed |
| Review 提交 | Information | TaskId, Action, Comment |
| MCP 调用 | Debug | Method, Parameters, Duration |

---

## 6. 分布式追踪

### 6.1 追踪配置

```csharp
// Program.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.EnrichWithHttpRequest = (activity, request) =>
                {
                    activity.SetTag("http.request.body.size", request.ContentLength);
                };
            })
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
                options.SetDbStatementForStoredProcedure = true;
            })
            .AddRedisInstrumentation()
            .AddSource("DbOptimizer.*")
            .AddOtlpExporter();
    });
```

### 6.2 自定义 Span

```csharp
using var activity = ActivitySource.StartActivity("WorkflowExecution");
activity?.SetTag("workflow.type", workflowType);
activity?.SetTag("workflow.session_id", sessionId);

try
{
    // 执行 workflow
    activity?.SetStatus(ActivityStatusCode.Ok);
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.RecordException(ex);
    throw;
}
```

---

## 7. 性能监控

### 7.1 关键性能指标

| 指标 | 目标值 | 告警阈值 |
|------|--------|---------|
| Workflow 执行时间 (SQL) | <5s | >10s |
| Workflow 执行时间 (Config) | <10s | >20s |
| API 响应时间 (P95) | <500ms | >2s |
| Checkpoint 保存时间 | <500ms | >2s |
| 数据库查询时间 (P95) | <100ms | >500ms |
| Redis 操作时间 (P95) | <10ms | >50ms |

### 7.2 性能监控查询

```promql
# Workflow 执行时间 P95
histogram_quantile(0.95, 
  rate(dboptimizer_workflow_duration_bucket[5m]))

# API 错误率
rate(dboptimizer_api_request_errors[5m]) / 
rate(dboptimizer_api_request_total[5m])

# Checkpoint 压缩率
rate(dboptimizer_checkpoint_compression_enabled[5m]) / 
rate(dboptimizer_checkpoint_save_total[5m])
```

---

## 8. 健康检查

### 8.1 健康检查端点

```csharp
// Program.cs
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
```

### 8.2 健康检查配置

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: new[] { "ready", "db" })
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: new[] { "ready", "cache" })
    .AddCheck<WorkflowHealthCheck>("workflow", tags: new[] { "ready" });
```

---

## 9. 监控仪表板

### 9.1 Aspire Dashboard 视图

**Resources 视图**:
- DbOptimizer.API: 运行状态、CPU、内存
- PostgreSQL: 连接状态、查询数
- Redis: 连接状态、命中率

**Traces 视图**:
- Workflow 执行链路
- API 请求链路
- 数据库查询链路

**Metrics 视图**:
- Workflow 执行时间分布
- API 请求速率
- 错误率趋势

**Logs 视图**:
- 实时日志流
- 错误日志过滤
- 日志搜索

### 9.2 自定义仪表板

```json
// Grafana Dashboard 配置示例
{
  "dashboard": {
    "title": "DbOptimizer Monitoring",
    "panels": [
      {
        "title": "Workflow Execution Time",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, rate(dboptimizer_workflow_duration_bucket[5m]))"
          }
        ]
      },
      {
        "title": "API Error Rate",
        "targets": [
          {
            "expr": "rate(dboptimizer_api_request_errors[5m]) / rate(dboptimizer_api_request_total[5m])"
          }
        ]
      }
    ]
  }
}
```

---

## 10. 监控检查清单

### 10.1 部署前
- [ ] Aspire Dashboard 可访问
- [ ] OpenTelemetry 配置正确
- [ ] 健康检查端点正常
- [ ] 日志输出正常
- [ ] 指标收集正常

### 10.2 运行中
- [ ] 每日检查 Dashboard
- [ ] 每周审查告警规则
- [ ] 每月分析性能趋势
- [ ] 每季度优化监控配置

### 10.3 告警响应
- [ ] P0 告警: 15分钟内响应
- [ ] P1 告警: 1小时内响应
- [ ] P2 告警: 4小时内响应

---

**最后更新**: 2026-04-18  
**负责人**: 运维团队  
**审核人**: 待定
