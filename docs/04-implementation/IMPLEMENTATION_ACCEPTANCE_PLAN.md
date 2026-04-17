# Implementation Acceptance Plan

## 1. 环境前置条件

最小验证环境：

- PostgreSQL
- Redis
- 可选 MySQL
- `dotnet restore` 可访问 NuGet prerelease 包

若缺少真实数据库：

- SQL workflow 可用固定 SQL 输入做验证
- 配置 workflow 可用 fallback snapshot 做验证
- slow query 闭环可用测试数据插入 `slow_queries`

## 2. Epic 级验收

### Epic A

验收点：

1. `workflow/history/review` 都返回 `WorkflowResultEnvelope`
2. DTO 不再内嵌在路由文件
3. 数据库实体包含 `engine_*` 与 `source_*`

### Epic B

验收点：

1. 已引入 `Microsoft.Agents.AI.Workflows`
2. API 创建 workflow 后实际进入 MAF runtime
3. cancel/resume 通过新 runtime 生效

### Epic C

验收点：

1. SQL workflow 从创建到完成可跑通
2. 审核后 workflow 可继续
3. `history`、`SSE`、`review` 可看到 SQL 结果

### Epic D

验收点：

1. 配置调优可以从前端发起
2. 审核与 history 可以展示配置结果
3. `resultType=db-config-optimization-report`

### Epic E

验收点：

1. slow query 自动触发 SQL workflow
2. `slow_queries.latest_analysis_session_id` 有值
3. history 能反查 `sourceType=slow-query`

### Epic F

验收点：

1. `slow-query-trends` API 可返回趋势点
2. `slow-query-alerts` API 可返回告警
3. 前端能查看趋势与告警

## 3. 建议验证命令

```powershell
dotnet build e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.API\\DbOptimizer.API.csproj
dotnet build e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.Infrastructure\\DbOptimizer.Infrastructure.csproj
npm --prefix e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.Web run build
```

## 4. 关键 API 验证

1. `POST /api/workflows/sql-analysis`
2. `POST /api/workflows/db-config-optimization`
3. `GET /api/workflows/{sessionId}`
4. `POST /api/reviews/{taskId}/submit`
5. `GET /api/history/{sessionId}`
6. `GET /api/dashboard/slow-query-trends`
7. `GET /api/dashboard/slow-query-alerts`

## 5. 数据校验

检查：

```sql
select session_id, workflow_type, engine_type, result_type, source_type, source_ref_id
from workflow_sessions
order by created_at desc;

select query_id, latest_analysis_session_id
from slow_queries
order by updated_at desc;
```
