# Backend E2E Tests

## 概述

后端 E2E 测试套件，使用 Testcontainers 提供隔离的测试环境，覆盖 MAF 工作流的关键场景。

## 测试覆盖

### 1. SQL 工作流测试 (`SqlWorkflowE2ETests`)
- ✅ 完整工作流（提交 → 审核 → 完成）
- ✅ 审核驳回流程
- ✅ 工作流取消
- ✅ Checkpoint 恢复
- ✅ 并行执行

### 2. 配置优化工作流测试 (`ConfigWorkflowE2ETests`)
- ✅ 完整工作流
- ✅ 审核流程（通过/驳回）
- ✅ MCP 超时降级
- ✅ 无需审核自动完成
- ✅ 数据库连接失败处理
- ✅ 并行执行

### 3. 慢查询自动化测试 (`SlowQueryE2ETests`)
- ✅ 慢查询上报触发工作流
- ✅ 双向关联（慢查询 ↔ 工作流）
- ✅ 多慢查询追踪一致性
- ✅ 工作流完成更新慢查询状态

### 4. SSE 事件流测试 (`SseStreamingE2ETests`)
- ✅ 实时事件推送
- ✅ 断线重连（Last-Event-ID）
- ✅ 事件顺序保证
- ✅ 多客户端并发

### 5. 错误处理测试 (`ErrorHandlingE2ETests`)
- ✅ MCP 超时重试和降级
- ✅ 数据库连接失败
- ✅ Redis 故障降级
- ✅ 部分组件失败的优雅降级
- ✅ 并发错误隔离
- ✅ 资源清理

## 环境要求

- .NET 10 SDK
- Docker（用于 Testcontainers）
- 至少 4GB 可用内存

## 运行测试

### 运行所有测试
```bash
dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj
```

### 运行特定测试类
```bash
# SQL 工作流测试
dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj --filter "FullyQualifiedName~SqlWorkflowE2ETests"

# 错误处理测试
dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj --filter "FullyQualifiedName~ErrorHandlingE2ETests"
```

### 运行单个测试
```bash
dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj --filter "FullyQualifiedName~SqlWorkflow_CompleteFlow_ShouldSucceed"
```

### 详细输出
```bash
dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj --logger "console;verbosity=detailed"
```

## 测试架构

### 基类 (`E2ETestBase`)
- 自动启动 PostgreSQL 和 Redis 容器
- 配置 WebApplicationFactory
- 运行数据库迁移
- 提供 HttpClient 实例
- 测试结束后自动清理资源

### 测试 DTO (`Models/TestDtos.cs`)
- `WorkflowSubmitResponse`: 工作流提交响应
- `WorkflowStatusResponse`: 工作流状态
- `SlowQueryReportResponse`: 慢查询上报响应
- `WorkflowSummary`: 工作流摘要
- `WorkflowLogEntry`: 日志条目
- `WorkflowResult`: 工作流结果
- `ErrorResponse`: 错误响应

## 注意事项

### 1. 容器启动时间
首次运行时，Testcontainers 需要拉取 Docker 镜像，可能需要几分钟。

### 2. 端口冲突
Testcontainers 会自动分配随机端口，避免冲突。

### 3. 并行执行
测试类之间可以并行执行，但同一类内的测试按顺序执行（因为共享容器）。

### 4. 超时设置
部分测试包含 `Task.Delay()` 等待异步操作完成，如果环境较慢可能需要调整延迟时间。

### 5. 资源清理
每个测试类结束后会自动停止和删除容器，无需手动清理。

## CI/CD 集成

### GitHub Actions 示例
```yaml
name: E2E Tests

on: [push, pull_request]

jobs:
  e2e-tests:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    
    - name: Run E2E Tests
      run: dotnet test tests/DbOptimizer.BackendE2ETests/DbOptimizer.BackendE2ETests.csproj --logger "trx;LogFileName=e2e-results.trx"
    
    - name: Publish Test Results
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: E2E Test Results
        path: '**/e2e-results.trx'
        reporter: dotnet-trx
```

## 故障排查

### 容器启动失败
```bash
# 检查 Docker 是否运行
docker ps

# 清理旧容器
docker container prune -f
```

### 测试超时
增加测试方法的超时时间：
```csharp
[Fact(Timeout = 60000)] // 60 秒
public async Task MyTest() { ... }
```

### 数据库迁移失败
确保 EF Core 迁移文件存在：
```bash
dotnet ef migrations list --project src/DbOptimizer.Infrastructure
```

## 性能基准

在标准开发机器上（16GB RAM, 8 核 CPU）：
- 单个测试类：~30-60 秒
- 完整测试套件：~5-8 分钟

## 扩展测试

### 添加新测试类
1. 继承 `E2ETestBase`
2. 使用 `Client` 发送 HTTP 请求
3. 使用 FluentAssertions 进行断言

示例：
```csharp
public sealed class MyWorkflowE2ETests : E2ETestBase
{
    [Fact]
    public async Task MyTest()
    {
        var response = await Client.GetAsync("/api/my-endpoint");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

## 相关文档

- [MAF 迁移清单](../../docs/04-implementation/MAF_MIGRATION_CHECKLIST.md)
- [工作流设计](../../docs/DESIGN.md)
- [API 文档](../../docs/API.md)
