# API 接口规范

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [通用规范](#1-通用规范)
2. [Workflow API](#2-workflow-api)
3. [Review API](#3-review-api)
4. [Dashboard API](#4-dashboard-api)
5. [History API](#5-history-api)
6. [SSE 事件规范](#6-sse-事件规范)

---

## 1. 通用规范

### 1.1 响应包络

所有 API 响应使用统一的包络格式：

```json
{
  "success": true,
  "data": {},
  "error": null,
  "meta": {
    "requestId": "a1b2c3",
    "timestamp": "2026-04-15T10:00:00Z"
  }
}
```

**字段说明**：
- `success`: 请求是否成功
- `data`: 响应数据（成功时）
- `error`: 错误信息（失败时）
- `meta`: 元数据（请求 ID、时间戳）

### 1.2 分页结构

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "total": 356,
  "hasMore": true
}
```

### 1.3 错误码

| 错误码 | 说明 |
|-------|------|
| 400 | 请求参数错误 |
| 401 | 未授权 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 409 | 资源冲突 |
| 500 | 服务器内部错误 |
| 503 | 服务不可用 |

---

## 2. Workflow API

### 2.1 创建 SQL 分析 Workflow

**POST /api/workflows/sql-analysis**

**请求体**：
```json
{
  "sqlText": "SELECT * FROM users WHERE age > 18",
  "databaseId": "db-123",
  "options": {
    "enableIndexRecommendation": true,
    "enableSqlRewrite": true
  }
}
```

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "status": "Running",
    "startedAt": "2026-04-15T10:00:00Z"
  }
}
```

### 2.2 创建数据库配置优化 Workflow

**POST /api/workflows/db-config-optimization**

**请求体**：
```json
{
  "databaseId": "db-123",
  "targetMetrics": {
    "maxConnections": 1000,
    "cacheHitRate": 0.95
  }
}
```

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440001",
    "status": "Running",
    "startedAt": "2026-04-15T10:00:00Z"
  }
}
```

### 2.3 获取 Workflow 状态

**GET /api/workflows/{sessionId}**

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "workflowType": "SqlAnalysis",
    "status": "WaitingForReview",
    "currentExecutor": "HumanReviewExecutor",
    "progress": 75,
    "startedAt": "2026-04-15T10:00:00Z",
    "updatedAt": "2026-04-15T10:03:00Z",
    "result": {
      "recommendations": [
        {
          "type": "IndexRecommendation",
          "tableName": "users",
          "columns": ["age"],
          "indexType": "BTREE",
          "createDdl": "CREATE INDEX idx_users_age ON users(age)",
          "estimatedBenefit": 85.5,
          "confidence": 92.0,
          "reasoning": "全表扫描导致性能问题，添加索引可显著提升查询速度",
          "evidence": [
            {
              "type": "ExecutionPlan",
              "nodeId": "seq_scan_123",
              "metrics": {
                "cost": 1000,
                "rows": 50000
              }
            }
          ]
        }
      ]
    }
  }
}
```

### 2.4 取消 Workflow

**POST /api/workflows/{sessionId}/cancel**

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "status": "Cancelled"
  }
}
```

### 2.5 恢复 Workflow（从 Checkpoint）

**POST /api/workflows/{sessionId}/resume**

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "status": "Running",
    "resumedFrom": "IndexAdvisorExecutor"
  }
}
```

---

## 3. Review API

### 3.1 获取待审核任务列表

**GET /api/reviews?status=Pending&page=1&pageSize=20**

**响应**：
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "taskId": "task-123",
        "sessionId": "550e8400-e29b-41d4-a716-446655440000",
        "workflowType": "SqlAnalysis",
        "recommendations": [...],
        "createdAt": "2026-04-15T10:03:00Z"
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 5,
    "hasMore": false
  }
}
```

### 3.2 获取审核任务详情

**GET /api/reviews/{taskId}**

**响应**：
```json
{
  "success": true,
  "data": {
    "taskId": "task-123",
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "workflowType": "SqlAnalysis",
    "status": "Pending",
    "recommendations": [
      {
        "type": "IndexRecommendation",
        "tableName": "users",
        "columns": ["age"],
        "createDdl": "CREATE INDEX idx_users_age ON users(age)",
        "estimatedBenefit": 85.5,
        "confidence": 92.0,
        "reasoning": "全表扫描导致性能问题",
        "evidence": [...]
      }
    ],
    "createdAt": "2026-04-15T10:03:00Z"
  }
}
```

### 3.3 提交审核结果

**POST /api/reviews/{taskId}/submit**

**请求体（批准）**：
```json
{
  "action": "approve",
  "comment": "建议合理，批准执行"
}
```

**请求体（拒绝）**：
```json
{
  "action": "reject",
  "comment": "索引列选择不当，建议重新分析",
  "adjustments": {
    "preferredColumns": ["age", "status"]
  }
}
```

**请求体（调整）**：
```json
{
  "action": "adjust",
  "comment": "微调索引名称",
  "adjustments": {
    "indexName": "idx_users_age_v2"
  }
}
```

**响应**：
```json
{
  "success": true,
  "data": {
    "taskId": "task-123",
    "status": "Approved",
    "reviewedAt": "2026-04-15T10:05:00Z"
  }
}
```

---

## 4. Dashboard API

### 4.1 获取 Dashboard 统计

**GET /api/dashboard/stats**

**响应**：
```json
{
  "success": true,
  "data": {
    "totalTasks": 356,
    "runningTasks": 12,
    "pendingReview": 5,
    "completedTasks": 339,
    "recentTasks": [
      {
        "sessionId": "550e8400-e29b-41d4-a716-446655440000",
        "workflowType": "SqlAnalysis",
        "status": "Completed",
        "startedAt": "2026-04-15T10:00:00Z",
        "completedAt": "2026-04-15T10:05:00Z"
      }
    ],
    "performanceTrend": {
      "dates": ["2026-04-01", "2026-04-02", "2026-04-03"],
      "taskCounts": [10, 15, 12],
      "successRates": [0.95, 0.92, 0.98],
      "avgDurations": [300, 280, 320]
    }
  }
}
```

---

## 5. History API

### 5.1 获取历史任务列表

**GET /api/history?workflowType=SqlAnalysis&status=Completed&page=1&pageSize=20**

**查询参数**：
- `workflowType`: 可选，过滤 Workflow 类型
- `status`: 可选，过滤状态
- `startDate`: 可选，开始日期
- `endDate`: 可选，结束日期
- `page`: 页码
- `pageSize`: 每页数量

**响应**：
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "sessionId": "550e8400-e29b-41d4-a716-446655440000",
        "workflowType": "SqlAnalysis",
        "status": "Completed",
        "startedAt": "2026-04-15T10:00:00Z",
        "completedAt": "2026-04-15T10:05:00Z",
        "duration": 300,
        "recommendationCount": 3
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 339,
    "hasMore": true
  }
}
```

### 5.2 获取历史任务详情

**GET /api/history/{sessionId}**

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "workflowType": "SqlAnalysis",
    "status": "Completed",
    "startedAt": "2026-04-15T10:00:00Z",
    "completedAt": "2026-04-15T10:05:00Z",
    "duration": 300,
    "executors": [
      {
        "executorName": "SqlParserExecutor",
        "status": "Completed",
        "startedAt": "2026-04-15T10:00:00Z",
        "completedAt": "2026-04-15T10:01:00Z",
        "duration": 60
      }
    ],
    "result": {
      "recommendations": [...]
    },
    "tokenUsage": {
      "prompt": 5000,
      "completion": 2000,
      "total": 7000,
      "cost": 0.14
    }
  }
}
```

### 5.3 获取回放数据

**GET /api/history/{sessionId}/replay**

**响应**：
```json
{
  "success": true,
  "data": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "events": [
      {
        "sequence": 1,
        "timestamp": "2026-04-15T10:00:00Z",
        "eventType": "WorkflowStarted",
        "payload": {...}
      },
      {
        "sequence": 2,
        "timestamp": "2026-04-15T10:00:30Z",
        "eventType": "ExecutorStarted",
        "payload": {
          "executorName": "SqlParserExecutor"
        }
      },
      {
        "sequence": 3,
        "timestamp": "2026-04-15T10:01:00Z",
        "eventType": "ExecutorCompleted",
        "payload": {
          "executorName": "SqlParserExecutor",
          "result": {...}
        }
      }
    ]
  }
}
```

---

## 6. SSE 事件规范

### 6.1 连接 SSE

**GET /api/workflows/{sessionId}/events**

**响应头**：
```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive
```

### 6.2 事件格式

```
event: WorkflowEvent
data: {"eventType":"ExecutorStarted","sessionId":"550e8400-e29b-41d4-a716-446655440000","sequence":1,"timestamp":"2026-04-15T10:00:00Z","payload":{"executorName":"SqlParserExecutor"}}

event: WorkflowEvent
data: {"eventType":"ExecutorCompleted","sessionId":"550e8400-e29b-41d4-a716-446655440000","sequence":2,"timestamp":"2026-04-15T10:01:00Z","payload":{"executorName":"SqlParserExecutor","result":{...}}}
```

### 6.3 事件类型

| 事件类型 | 说明 | Payload |
|---------|------|---------|
| **WorkflowStarted** | Workflow 开始 | `{ workflowType, startedAt }` |
| **ExecutorStarted** | Executor 开始 | `{ executorName, startedAt }` |
| **ExecutorCompleted** | Executor 完成 | `{ executorName, result, completedAt }` |
| **ExecutorFailed** | Executor 失败 | `{ executorName, error, failedAt }` |
| **ToolCallStarted** | Tool 调用开始 | `{ toolName, arguments, startedAt }` |
| **ToolCallCompleted** | Tool 调用完成 | `{ toolName, result, completedAt }` |
| **AgentThinking** | Agent 思考中 | `{ agentName, thinking }` |
| **WorkflowWaitingReview** | 等待审核 | `{ recommendations }` |
| **WorkflowCompleted** | Workflow 完成 | `{ result, completedAt }` |
| **WorkflowFailed** | Workflow 失败 | `{ error, failedAt }` |
| **CheckpointSaved** | Checkpoint 保存 | `{ checkpointVersion, savedAt }` |

### 6.4 心跳事件

每 30 秒发送一次心跳，保持连接：

```
event: heartbeat
data: {"timestamp":"2026-04-15T10:00:30Z"}
```

### 6.5 断线重连机制

**Last-Event-ID 机制**：

客户端断线重连时，通过 `Last-Event-ID` 请求头告知服务器上次接收的事件序列号：

```typescript
// 客户端实现
const eventSource = new EventSource(
  `/api/workflows/${sessionId}/events`,
  {
    headers: {
      'Last-Event-ID': lastEventId
    }
  }
)

eventSource.onmessage = (event) => {
  lastEventId = event.lastEventId
  // 处理事件
}

eventSource.onerror = () => {
  // 自动重连，浏览器会自动带上 Last-Event-ID
}
```

**服务器行为**：
- 收到 `Last-Event-ID` 后，从该序列号之后的事件开始推送
- 如果事件已过期（超过 1 小时），返回完整状态快照

### 6.6 SSE 降级策略

如果 SSE 连接失败（如企业防火墙阻止），自动降级为轮询：

```typescript
// 前端自动降级
let usePolling = false

eventSource.onerror = (error) => {
  if (error.readyState === EventSource.CLOSED) {
    usePolling = true
    startPolling()
  }
}

function startPolling() {
  setInterval(async () => {
    const response = await fetch(`/api/workflows/${sessionId}`)
    const data = await response.json()
    updateWorkflowState(data)
  }, 3000)  // 每 3 秒轮询
}
```

---

## 7. WebSocket 规范（备用）

**注意**：第一版使用 SSE，WebSocket 作为备用方案。

### 7.1 连接 WebSocket

**WS /api/workflows/{sessionId}/ws**

```typescript
const ws = new WebSocket(`ws://localhost:5000/api/workflows/${sessionId}/ws`)

ws.onopen = () => {
  console.log('WebSocket connected')
}

ws.onmessage = (event) => {
  const data = JSON.parse(event.data)
  handleWorkflowEvent(data)
}

ws.onerror = (error) => {
  console.error('WebSocket error:', error)
}

ws.onclose = () => {
  console.log('WebSocket closed')
}
```

### 7.2 消息格式

与 SSE 事件格式相同：

```json
{
  "eventType": "ExecutorStarted",
  "sessionId": "550e8400-e29b-41d4-a716-446655440000",
  "sequence": 1,
  "timestamp": "2026-04-15T10:00:00Z",
  "payload": {
    "executorName": "SqlParserExecutor"
  }
}
```

---

## 8. 错误响应详细说明

### 8.1 错误响应格式

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "WORKFLOW_NOT_FOUND",
    "message": "Workflow session not found",
    "details": {
      "sessionId": "550e8400-e29b-41d4-a716-446655440000"
    }
  },
  "meta": {
    "requestId": "a1b2c3",
    "timestamp": "2026-04-15T10:00:00Z"
  }
}
```

### 8.2 业务错误码

| 错误码 | HTTP 状态码 | 说明 |
|-------|------------|------|
| `WORKFLOW_NOT_FOUND` | 404 | Workflow 不存在 |
| `WORKFLOW_ALREADY_RUNNING` | 409 | Workflow 已在运行 |
| `WORKFLOW_CANCELLED` | 409 | Workflow 已取消 |
| `INVALID_SQL_SYNTAX` | 400 | SQL 语法错误 |
| `DATABASE_CONNECTION_FAILED` | 503 | 数据库连接失败 |
| `MCP_TIMEOUT` | 504 | MCP 调用超时 |
| `MCP_ERROR` | 500 | MCP 调用错误 |
| `REVIEW_TASK_NOT_FOUND` | 404 | 审核任务不存在 |
| `REVIEW_ALREADY_SUBMITTED` | 409 | 审核已提交 |
| `CHECKPOINT_NOT_FOUND` | 404 | Checkpoint 不存在 |
| `CHECKPOINT_EXPIRED` | 410 | Checkpoint 已过期 |

### 8.3 错误处理示例

```typescript
// 前端错误处理
async function createWorkflow(sqlText: string) {
  try {
    const response = await fetch('/api/workflows/sql-analysis', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sqlText, databaseId: 'db-123' })
    })

    const result = await response.json()

    if (!result.success) {
      // 处理业务错误
      switch (result.error.code) {
        case 'INVALID_SQL_SYNTAX':
          showError('SQL 语法错误，请检查后重试')
          break
        case 'DATABASE_CONNECTION_FAILED':
          showError('数据库连接失败，请检查配置')
          break
        default:
          showError(result.error.message)
      }
      return null
    }

    return result.data
  } catch (error) {
    // 处理网络错误
    showError('网络错误，请稍后重试')
    return null
  }
}
```

---

## 9. 文档映射关系

- **需求文档**: `docs/REQUIREMENTS.md`
- **架构设计**: `docs/ARCHITECTURE.md`
- **Workflow 设计**: `docs/WORKFLOW_DESIGN.md`
- **数据模型**: `docs/DATA_MODEL.md`
- **前端架构**: `docs/FRONTEND_ARCHITECTURE.md`
- **页面设计**: `docs/PAGE_DESIGN.md`
- **组件规范**: `docs/COMPONENT_SPEC.md`
- **开发环境搭建**: `docs/DEV_SETUP.md`
- **C# 编码规范**: `docs/CODING_STANDARDS_CSHARP.md`
- **TypeScript 编码规范**: `docs/CODING_STANDARDS_TYPESCRIPT.md`
- **Git 工作流**: `docs/GIT_WORKFLOW.md`

---

## 10. 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|---------|
| v1.0 | 2026-04-15 | 初始版本，定义核心 API 规范 |
| v1.0 | 2026-04-15 | 补充 SSE 断线重连、降级策略、错误码详细说明 |
