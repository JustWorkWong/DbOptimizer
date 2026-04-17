# 前端详细设计索引

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0

---

## 文档说明

本文档是前端详细设计的索引页面。详细内容已拆分为多个专项文档，便于维护和查阅。

---

## 前端设计文档目录

1. **[前端架构设计](./FRONTEND_ARCHITECTURE.md)**
   - 全局 UI 框架
   - 状态管理（Pinia）
   - 路由设计
   - 公共组件
   - SSE 集成

2. **[页面详细设计](./PAGE_DESIGN.md)**
   - 总览页面
   - SQL 调优页面
   - 实例调优页面
   - 审核工作台页面
   - 历史任务页面
   - 运行回放页面

3. **[组件规范](./COMPONENT_SPEC.md)**
   - SSE 连接器
   - Monaco 编辑器
   - Workflow 进度条
   - 建议卡片
   - 证据查看器
   - 日志查看器

---

## 设计目标

- 覆盖 6 个核心页面：总览、SQL 调优、实例调优、审核工作台、历史任务、运行回放
- 每个页面明确：布局结构、交互路径、接口调用、出入参
- 与 P0/P1 对齐：
  - P0：Checkpoint 恢复、MCP 异常可视化
  - P1：SSE 断线重连、轮询降级、成本与证据链展示

---

## 快速导航

**前端架构**：
- [FRONTEND_ARCHITECTURE.md](./FRONTEND_ARCHITECTURE.md) - 全局框架、状态管理、路由

**页面开发**：
- [PAGE_DESIGN.md](./PAGE_DESIGN.md) - 6 个核心页面的详细设计

**组件开发**：
- [COMPONENT_SPEC.md](./COMPONENT_SPEC.md) - 公共组件规范

**接口对接**：
- [API_SPEC.md](./API_SPEC.md) - API 接口规范

---

## 与其他文档的映射关系

- 需求基线：[REQUIREMENTS.md](./REQUIREMENTS.md)
- 总体架构：[ARCHITECTURE.md](./ARCHITECTURE.md)
- P0/P1 技术补充：[P0_P1_DESIGN.md](./P0_P1_DESIGN.md)
- 本文档：前端可视化与接口契约细化（用于前后端联调）

---

## 文档维护

**更新原则**：
- 页面变更需同步更新 PAGE_DESIGN.md
- 组件变更需同步更新 COMPONENT_SPEC.md
- 状态管理变更需同步更新 FRONTEND_ARCHITECTURE.md

**文档版本**：
- 当前版本：v1.0
- 最后更新：2026-04-15

---

## 原始内容（已拆分）

以下内容已拆分到专项文档，保留此处仅供参考。

### 2. 全局 UI 框架

```ascii
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Logo | DbOptimizer                                   用户/环境 | 全局告警/状态   │
├──────────────────────────────────────────────────────────────────────────────────┤
│ 侧边导航                                                                        │
│ ┌──────────────┐  ┌────────────────────────────────────────────────────────────┐ │
│ │ 1. 总览      │  │ 页面 Header: 标题 + 面包屑 + 当前数据库连接状态          │ │
│ │ 2. SQL 调优  │  ├────────────────────────────────────────────────────────────┤ │
│ │ 3. 实例调优  │  │ 页面主内容区（按页面不同）                               │ │
│ │ 4. 审核工作台│  │                                                            │ │
│ │ 5. 历史任务  │  │                                                            │ │
│ │ 6. 运行回放  │  │                                                            │ │
│ └──────────────┘  └────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 全局状态模型（Pinia）

```typescript
interface AppState {
  selectedDatabaseId: string | null;
  activeSessionId: string | null;
  sseConnection: {
    status: 'connected' | 'reconnecting' | 'polling' | 'disconnected';
    reconnectAttempts: number;
    lastEventAt: string | null;
  };
  ui: {
    isLoading: boolean;
    globalError: string | null;
  };
}
```

---

## 3. 公共接口约定

### 3.1 响应包络

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

### 3.2 分页结构

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "total": 356,
  "totalPages": 18
}
```

### 3.3 SSE 事件结构（含断线续传）

```json
{
  "eventType": "executor.completed",
  "sessionId": "wf_123",
  "sequence": 18,
  "timestamp": "2026-04-15T10:01:12Z",
  "payload": {}
}
```

`eventType` 枚举（首版）：
- `workflow.started`
- `executor.started`
- `executor.completed`
- `executor.failed`
- `review.required`
- `review.submitted`
- `workflow.completed`
- `workflow.failed`
- `heartbeat`

SSE 协议约束（P1 必须）：
- 服务端每条事件输出 `id: {sequence}`，与 `data.sequence` 保持一致
- 客户端重连时浏览器自动携带 `Last-Event-ID`
- 服务端收到 `Last-Event-ID` 后，从 `sequence = Last-Event-ID + 1` 开始补发事件
- 若补发窗口已过期，返回 `409` 并提示前端走 `GET /api/workflows/{sessionId}/timeline` 做增量补齐

---

## 4. 页面设计（ASCII + 接口）

## 4.1 总览页（Dashboard）

### 4.1.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: 运营总览                                   [时间范围: 最近7天 v]      │
├──────────────────────────────────────────────────────────────────────────────┤
│ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌───────────────────┐ │
│ │ 今日分析任务  │ │ 运行中任务     │ │ 待审核任务     │ │ 今日Token成本     │ │
│ │  128          │ │  6            │ │  14           │ │  $12.38           │ │
│ └───────────────┘ └───────────────┘ └───────────────┘ └───────────────────┘ │
├──────────────────────────────────────────────────────────────────────────────┤
│ 慢查询趋势（ECharts 折线图）                                                │
│ ──────────────────────────────────────────────────────────────────────────── │
├───────────────────────────────┬──────────────────────────────────────────────┤
│ 待审核任务（Top N）            │ 最近完成任务                                 │
│ - wf_101  2m前                │ - wf_098 SQL优化 已完成                      │
│ - wf_102  5m前                │ - wf_097 配置优化 已完成                     │
└───────────────────────────────┴──────────────────────────────────────────────┘
```

### 4.1.2 调用接口

#### A. 获取总览指标
- **GET** `/api/dashboard/overview`

**入参（Query）**：
- `from` (ISO8601, 可选)
- `to` (ISO8601, 可选)

**出参（data）**：
```json
{
  "todayTaskCount": 128,
  "runningTaskCount": 6,
  "pendingReviewCount": 14,
  "todayTokenCostUsd": 12.38
}
```

#### B. 获取慢查询趋势
- **GET** `/api/dashboard/slow-query-trend`

**入参（Query）**：
- `from` (必填)
- `to` (必填)
- `interval` (`hour` | `day`，默认 `day`)

**出参（data）**：
```json
{
  "points": [
    { "time": "2026-04-09", "slowQueryCount": 12 },
    { "time": "2026-04-10", "slowQueryCount": 18 }
  ]
}
```

#### C. 获取待审核任务
- **GET** `/api/reviews/pending`

**入参（Query）**：
- `page`、`pageSize`、`databaseType`(可选)

**出参（data）**：分页结构 + `reviewId/sessionId/workflowType/createdAt/confidence`

---

## 4.2 SQL 调优页（SqlAnalysisView）

### 4.2.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: SQL 调优                                                               │
├──────────────────────────────────────────────────────────────────────────────┤
│ 数据库: [MySQL-Prod-1 v]  Schema: [app_db v]  [连接测试] [历史SQL]          │
├──────────────────────────────────────────────────────────────────────────────┤
│ SQL Editor (Monaco)                                                          │
│ SELECT u.id, u.email FROM users u WHERE u.email = ?;                        │
│                                                                              │
│ [开始分析] [格式化] [清空]                                                   │
├──────────────────────────────────────────────────────────────────────────────┤
│ 实时执行流（SSE）                                                            │
│ ● SqlParserExecutor      completed  1.2s                                     │
│ ● ExecutionPlanExecutor  running    ...                                      │
├──────────────────────────────────────────────────────────────────────────────┤
│ 优化建议卡片                                                                  │
│ 1) 索引建议 idx_users_email  置信度: 0.91  预估收益: 43%                    │
│ 2) SQL 重写建议 ...                                                          │
│ [提交审核]                                                                   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 4.2.2 调用接口

#### A. 创建 SQL 分析任务
- **POST** `/api/workflows/sql-analysis`

**入参（Body）**：
```json
{
  "databaseId": "db_001",
  "schema": "app_db",
  "sql": "SELECT ...",
  "analysisOptions": {
    "enableIndexRecommendation": true,
    "enableSqlRewrite": true,
    "maxTokenBudget": 50000
  }
}
```

**出参（data）**：
```json
{
  "sessionId": "wf_123",
  "status": "Running",
  "startedAt": "2026-04-15T10:00:00Z"
}
```

#### B. 订阅实时事件（支持断线续传）
- **GET (SSE)** `/api/workflows/{sessionId}/events`

**入参（Path）**：
- `sessionId` (必填)

**Header（浏览器自动）**：
- `Last-Event-ID`（可选，断线重连时由浏览器附带）

**事件 payload 示例**：
```json
{
  "eventType": "executor.completed",
  "payload": {
    "executorName": "ExecutionPlanExecutor",
    "durationMs": 1342,
    "confidence": 0.87
  }
}
```

#### C. 查询任务快照（P0 恢复 / 轮询降级）
- **GET** `/api/workflows/{sessionId}`

**出参（data）**：
```json
{
  "sessionId": "wf_123",
  "status": "WaitingForReview",
  "currentExecutor": "HumanReviewExecutor",
  "checkpointVersion": 12,
  "result": {
    "recommendations": []
  }
}
```


#### D. 取消任务
- **POST** `/api/workflows/{sessionId}/cancel`

**入参（Body）**：
```json
{ "reason": "用户主动取消" }
```

**出参（data）**：
```json
{ "sessionId": "wf_123", "status": "Cancelled" }
```

---

## 4.3 实例调优页（DbConfigView）

### 4.3.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: 实例调优                                                               │
├──────────────────────────────────────────────────────────────────────────────┤
│ 连接: [PostgreSQL-Prod v] [连接测试]                                         │
├──────────────────────────────┬───────────────────────────────────────────────┤
│ 当前配置参数                 │ 服务器资源                                    │
│ shared_buffers = 1GB         │ CPU: 8核   内存: 32GB   磁盘: SSD            │
│ work_mem = 4MB               │ QPS: 1200  并发连接: 180                     │
├──────────────────────────────┴───────────────────────────────────────────────┤
│ [开始配置分析]                                                               │
├──────────────────────────────────────────────────────────────────────────────┤
│ 配置建议                                                                     │
│ - shared_buffers: 1GB -> 8GB (收益: 25%, 风险: 中)                          │
│ - work_mem: 4MB -> 16MB (收益: 12%, 风险: 低)                               │
│ [提交审核]                                                                   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 4.3.2 调用接口

#### A. 获取实例快照
- **GET** `/api/databases/{databaseId}/config-snapshot`

**出参（data）**：
```json
{
  "databaseId": "db_002",
  "databaseType": "postgresql",
  "config": [
    { "name": "shared_buffers", "currentValue": "1GB" }
  ],
  "serverResource": {
    "cpuCores": 8,
    "memoryGb": 32,
    "diskType": "SSD"
  },
  "workload": {
    "qps": 1200,
    "activeConnections": 180
  }
}
```

#### B. 创建配置分析任务
- **POST** `/api/workflows/db-config-analysis`

**入参（Body）**：
```json
{
  "databaseId": "db_002",
  "analysisScope": ["memory", "connection", "checkpoint"],
  "customConstraints": {
    "maxMemoryUsagePercent": 70
  }
}
```

**出参（data）**：
```json
{ "sessionId": "wf_456", "status": "Running" }
```

---

## 4.4 审核工作台（ReviewWorkspaceView）

### 4.4.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: 审核工作台                                                             │
├───────────────────────────────┬──────────────────────────────────────────────┤
│ 待审核列表                     │ 审核详情                                     │
│ - review_101 (SQL优化)         │ Session: wf_123                              │
│ - review_102 (配置优化)         │ 建议1: CREATE INDEX ...                      │
│ - review_103 (SQL优化)         │ 置信度: 0.91                                 │
│                               │ 证据链: explain + index_stats + slow_log      │
│                               │ 审核意见: [文本输入框]                        │
│                               │ [同意] [驳回] [调整后重跑]                    │
└───────────────────────────────┴──────────────────────────────────────────────┘
```

### 4.4.2 调用接口

#### A. 获取待审核列表
- **GET** `/api/reviews/pending`

**入参（Query）**：
- `page`、`pageSize`
- `workflowType` (`sql` | `config`, 可选)

**出参（data.items[i]）**：
```json
{
  "reviewId": "review_101",
  "sessionId": "wf_123",
  "workflowType": "SqlAnalysis",
  "createdAt": "2026-04-15T10:12:00Z",
  "confidence": 0.91
}
```

#### B. 获取审核详情
- **GET** `/api/reviews/{reviewId}`

**出参（data）**：
```json
{
  "reviewId": "review_101",
  "sessionId": "wf_123",
  "recommendations": [],
  "confidence": 0.91,
  "reasoning": "...",
  "evidenceReferences": [
    { "type": "execution_plan", "refId": "plan_1" }
  ]
}
```

#### C. 提交审核动作
- **POST** `/api/workflows/{sessionId}/review`

**入参（Body）**：
```json
{
  "action": "approve",
  "comment": "建议可执行",
  "adjustments": null
}
```

`action` 可选：`approve` | `reject` | `adjust`

调整重跑示例：
```json
{
  "action": "adjust",
  "comment": "索引列改为 (tenant_id, email)",
  "adjustments": {
    "indexColumns": ["tenant_id", "email"],
    "maxIndexCount": 2
  }
}
```

**出参（data）**：
```json
{
  "sessionId": "wf_123",
  "status": "Running",
  "nextStep": "RegenerationExecutor"
}
```

---

## 4.5 历史任务页（HistoryView）

### 4.5.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: 历史任务                                                               │
├──────────────────────────────────────────────────────────────────────────────┤
│ 筛选: [时间范围] [状态v] [数据库类型v] [关键词] [查询] [重置]               │
├──────────────────────────────────────────────────────────────────────────────┤
│ 表格                                                                          │
│ SessionId  类型   状态        创建时间              Token成本   操作          │
│ wf_123     SQL    Completed   2026-04-15 10:00      $0.42      [详情][对比]   │
│ wf_122     CFG    Failed      2026-04-15 09:50      $0.15      [详情]          │
├──────────────────────────────────────────────────────────────────────────────┤
│ 分页                                                                          │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 4.5.2 调用接口

#### A. 查询历史任务
- **GET** `/api/workflows`

**入参（Query）**：
- `page`、`pageSize`
- `status` (`Running|WaitingForReview|Completed|Failed|Cancelled`)
- `workflowType` (`SqlAnalysis|DbConfigOptimization`)
- `databaseType` (`mysql|postgresql`)
- `from`、`to`
- `keyword`

**出参（data）**：分页结构 + `sessionId/workflowType/status/createdAt/costUsd`

#### B. 查询任务详情
- **GET** `/api/workflows/{sessionId}`

**出参（data）**：
```json
{
  "sessionId": "wf_123",
  "status": "Completed",
  "input": {},
  "output": {},
  "review": {
    "status": "Approved",
    "comment": "通过"
  },
  "tokenUsage": {
    "totalTokens": 18342,
    "costUsd": 0.42
  }
}
```

#### C. 查询版本列表
- **GET** `/api/workflows/{sessionId}/versions`

**出参（data.items）**：
```json
[
  { "version": 1, "createdAt": "...", "changeReason": "initial" },
  { "version": 2, "createdAt": "...", "changeReason": "review_adjust" }
]
```

#### D. 版本对比
- **GET** `/api/workflows/{sessionId}/versions/compare?left=1&right=2`

**出参（data）**：
```json
{
  "leftVersion": 1,
  "rightVersion": 2,
  "diff": {
    "addedRecommendations": [],
    "removedRecommendations": [],
    "changedFields": []
  }
}
```

---

## 4.6 运行回放页（TimelineView）

### 4.6.1 页面结构

```ascii
┌──────────────────────────────────────────────────────────────────────────────┐
│ 标题: 运行回放                            Session: [wf_123 v] [实时跟踪开关] │
├──────────────────────────────────────────┬───────────────────────────────────┤
│ 时间线                                   │ 详情面板                           │
│ 10:00:01 workflow.started                │ 节点: ExecutionPlanExecutor        │
│ 10:00:02 executor.started(SqlParser)     │ 耗时: 1342ms                        │
│ 10:00:03 executor.completed(SqlParser)   │ 输入: ...                           │
│ 10:00:05 executor.started(Plan)          │ 输出: ...                           │
│ 10:00:06 tool.call(explain)              │ Tool调用: explain(query=...)        │
│ 10:00:07 executor.completed(Plan)        │ 错误/告警: ...                      │
└──────────────────────────────────────────┴───────────────────────────────────┘
```

### 4.6.2 调用接口

#### A. 获取时间线（历史）
- **GET** `/api/workflows/{sessionId}/timeline`

**入参（Query）**：
- `cursor` (可选，增量加载)
- `limit` (默认 200)

**出参（data）**：
```json
{
  "events": [
    {
      "sequence": 1,
      "timestamp": "2026-04-15T10:00:01Z",
      "eventType": "executor.started",
      "executorName": "SqlParserExecutor",
      "payload": {}
    }
  ],
  "nextCursor": "seq_201"
}
```

#### B. 获取 Executor 详情
- **GET** `/api/workflows/{sessionId}/executors/{executionId}`

**出参（data）**：
```json
{
  "executionId": "exe_789",
  "executorName": "ExecutionPlanExecutor",
  "status": "Completed",
  "durationMs": 1342,
  "inputData": {},
  "outputData": {},
  "toolCalls": []
}
```

#### C. 实时增量（运行中）
- **GET (SSE)** `/api/workflows/{sessionId}/events`

---

## 5. P0/P1 前端落地要点

### 5.1 P0：刷新恢复（Checkpoint）

页面进入逻辑：
1. 读取 URL `sessionId`
2. 调用 `GET /api/workflows/{sessionId}`
3. 若状态为 `Running/WaitingForReview`，自动恢复页面态并订阅 SSE
4. 若 `Completed/Failed/Cancelled`，进入只读回放态

```typescript
if (sessionId) {
  const snapshot = await workflowApi.getWorkflow(sessionId);
  restorePageState(snapshot);
  if (snapshot.status === 'Running' || snapshot.status === 'WaitingForReview') {
    sse.connect(sessionId);
  }
}
```

### 5.2 P1：SSE 重连与降级

- 优先 SSE
- 连续 5 次失败后降级轮询 `GET /api/workflows/{sessionId}`（每 3 秒）
- 检测到 `Completed/Failed/Cancelled` 后停止轮询
- 维护本地 `lastSequence`
- 收到事件若 `sequence > lastSequence + 1`，调用 `GET /api/workflows/{sessionId}/timeline?cursor=seq_{lastSequence}` 增量补齐
- Timeline 补齐成功后再处理当前事件，避免事件乱序导致 UI 状态回退

### 5.3 P1：错误呈现规范

- MCP 超时：黄色告警条（可继续，结果来自 fallback）
- MCP 权限错误：红色错误条（不可继续，需用户修正连接权限）
- 审核驳回重跑：蓝色信息条（展示驳回原因 + 已重跑）

---

## 6. 前端 API TypeScript 类型（建议）

```typescript
type WorkflowStatus =
  | 'Running'
  | 'WaitingForReview'
  | 'Completed'
  | 'Failed'
  | 'Cancelled';

interface CreateSqlAnalysisRequest {
  databaseId: string;
  schema?: string;
  sql: string;
  analysisOptions?: {
    enableIndexRecommendation?: boolean;
    enableSqlRewrite?: boolean;
    maxTokenBudget?: number;
  };
}

interface CreateWorkflowResponse {
  sessionId: string;
  status: WorkflowStatus;
  startedAt: string;
}

interface ReviewSubmitRequest {
  action: 'approve' | 'reject' | 'adjust';
  comment?: string;
  adjustments?: Record<string, unknown> | null;
}

interface SseEvent<TPayload = unknown> {
  eventType: string;
  sessionId: string;
  sequence: number;
  timestamp: string;
  payload: TPayload;
}

interface WorkflowSnapshot {
  sessionId: string;
  status: WorkflowStatus;
  currentExecutor?: string;
  checkpointVersion: number;
  result?: {
    recommendations?: unknown[];
  };
}
```

---

## 7. 与文档映射关系

- 需求基线：`docs/REQUIREMENTS.md`
- 总体架构：`docs/DESIGN.md`
- P0/P1 技术补充：`docs/P0_P1_DESIGN.md`
- 本文档：前端可视化与接口契约细化（用于前后端联调）
