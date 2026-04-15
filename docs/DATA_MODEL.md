# 数据模型设计

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [数据库选型](#1-数据库选型)
2. [表结构设计](#2-表结构设计)
3. [实体关系图](#3-实体关系图)
4. [JSONB 字段设计](#4-jsonb-字段设计)
5. [索引策略](#5-索引策略)

---

## 1. 数据库选型

### 1.1 PostgreSQL 作为主存储

**选择理由**：
- **JSONB 支持**：高效存储 Agent 上下文、执行计划等半结构化数据
- **全文搜索**：支持 SQL 文本搜索
- **成熟稳定**：生产环境广泛使用
- **Aspire 原生支持**：开箱即用

### 1.2 Redis 作为缓存

**用途**：
- SSE 会话管理
- Workflow 热点数据缓存
- 分布式锁

---

## 2. 表结构设计

### 2.1 核心表

#### 2.1.1 workflow_sessions（Workflow 会话）

```sql
CREATE TABLE workflow_sessions (
    session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    workflow_type VARCHAR(50) NOT NULL,  -- 'SqlAnalysis' / 'DbConfigOptimization'
    status VARCHAR(20) NOT NULL,         -- 'Running' / 'WaitingForReview' / 'Completed' / 'Failed'
    state JSONB NOT NULL DEFAULT '{}',   -- Checkpoint 快照
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    error_message TEXT,
    
    -- 索引
    INDEX idx_status (status),
    INDEX idx_created_at (created_at DESC),
    INDEX idx_workflow_type (workflow_type)
);

COMMENT ON TABLE workflow_sessions IS 'Workflow 会话表，记录每次优化任务';
COMMENT ON COLUMN workflow_sessions.state IS 'JSONB 格式的 Checkpoint 快照';
```

**state 字段结构**：
```json
{
  "currentExecutor": "IndexAdvisorExecutor",
  "completedExecutors": ["SqlParserExecutor", "ExecutionPlanExecutor"],
  "context": {
    "parsedSql": { ... },
    "executionPlan": { ... }
  },
  "checkpointVersion": 3
}
```

#### 2.1.2 agent_executions（Agent 执行记录）

```sql
CREATE TABLE agent_executions (
    execution_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES workflow_sessions(session_id) ON DELETE CASCADE,
    agent_name VARCHAR(100) NOT NULL,    -- 'SqlParserAgent' / 'IndexAdvisorAgent'
    executor_name VARCHAR(100) NOT NULL, -- 'SqlParserExecutor'
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    status VARCHAR(20) NOT NULL,         -- 'Running' / 'Completed' / 'Failed'
    input_data JSONB,
    output_data JSONB,
    error_message TEXT,
    token_usage JSONB,                   -- { "prompt": 1000, "completion": 500, "total": 1500 }
    
    INDEX idx_session_id (session_id),
    INDEX idx_agent_name (agent_name),
    INDEX idx_started_at (started_at DESC)
);

COMMENT ON TABLE agent_executions IS 'Agent 执行记录，用于调试和成本追踪';
```

#### 2.1.3 tool_calls（Tool 调用记录）

```sql
CREATE TABLE tool_calls (
    call_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_id UUID NOT NULL REFERENCES agent_executions(execution_id) ON DELETE CASCADE,
    tool_name VARCHAR(100) NOT NULL,     -- 'GetExecutionPlan' / 'GetTableIndexes'
    arguments JSONB NOT NULL,
    result JSONB,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    status VARCHAR(20) NOT NULL,         -- 'Running' / 'Completed' / 'Failed'
    error_message TEXT,
    
    INDEX idx_execution_id (execution_id),
    INDEX idx_tool_name (tool_name),
    INDEX idx_started_at (started_at DESC)
);

COMMENT ON TABLE tool_calls IS 'Tool 调用记录，追踪每个 Tool 的执行';
```

#### 2.1.4 agent_messages（Agent 消息）

```sql
CREATE TABLE agent_messages (
    message_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_id UUID NOT NULL REFERENCES agent_executions(execution_id) ON DELETE CASCADE,
    role VARCHAR(20) NOT NULL,           -- 'system' / 'user' / 'assistant' / 'tool'
    content TEXT NOT NULL,
    metadata JSONB,                      -- 额外信息（如 tool_call_id）
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    INDEX idx_execution_id (execution_id),
    INDEX idx_created_at (created_at DESC)
);

COMMENT ON TABLE agent_messages IS 'Agent 对话消息，用于调试和 Prompt 优化';
```

#### 2.1.5 decision_records（决策记录）

```sql
CREATE TABLE decision_records (
    decision_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_id UUID NOT NULL REFERENCES agent_executions(execution_id) ON DELETE CASCADE,
    decision_type VARCHAR(50) NOT NULL,  -- 'IndexRecommendation' / 'ConfigChange'
    reasoning TEXT NOT NULL,             -- Agent 的推理过程
    confidence DECIMAL(5,2) NOT NULL,    -- 置信度 0-100
    evidence JSONB NOT NULL,             -- 证据引用
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    INDEX idx_execution_id (execution_id),
    INDEX idx_decision_type (decision_type)
);

COMMENT ON TABLE decision_records IS '决策记录，记录 Agent 的推理过程和证据';
```

**evidence 字段结构**：
```json
{
  "executionPlanRef": "node_id_123",
  "metrics": {
    "currentCost": 1000,
    "estimatedCost": 100
  },
  "reasoning": "全表扫描导致性能问题"
}
```

#### 2.1.6 review_tasks（审核任务）

```sql
CREATE TABLE review_tasks (
    task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES workflow_sessions(session_id) ON DELETE CASCADE,
    recommendations JSONB NOT NULL,      -- 待审核的建议
    status VARCHAR(20) NOT NULL,         -- 'Pending' / 'Approved' / 'Rejected'
    reviewer_comment TEXT,
    adjustments JSONB,                   -- 用户调整的参数
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reviewed_at TIMESTAMPTZ,
    
    INDEX idx_session_id (session_id),
    INDEX idx_status (status),
    INDEX idx_created_at (created_at DESC)
);

COMMENT ON TABLE review_tasks IS '人工审核任务';
```

#### 2.1.7 prompt_versions（Prompt 版本管理）

```sql
CREATE TABLE prompt_versions (
    version_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    agent_name VARCHAR(100) NOT NULL,
    version_number INT NOT NULL,
    prompt_template TEXT NOT NULL,
    variables JSONB,                     -- Prompt 变量定义
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(100),
    
    UNIQUE (agent_name, version_number),
    INDEX idx_agent_name_active (agent_name, is_active)
);

COMMENT ON TABLE prompt_versions IS 'Prompt 版本管理，支持 A/B 测试';
```

### 2.2 辅助表

#### 2.2.1 sse_connections（SSE 连接管理）

```sql
CREATE TABLE sse_connections (
    connection_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES workflow_sessions(session_id) ON DELETE CASCADE,
    client_id VARCHAR(100) NOT NULL,
    connected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_heartbeat_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    INDEX idx_session_id (session_id),
    INDEX idx_last_heartbeat (last_heartbeat_at)
);

COMMENT ON TABLE sse_connections IS 'SSE 连接管理，用于断线检测';
```

#### 2.2.2 error_logs（错误日志）

```sql
CREATE TABLE error_logs (
    log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID REFERENCES workflow_sessions(session_id) ON DELETE SET NULL,
    execution_id UUID REFERENCES agent_executions(execution_id) ON DELETE SET NULL,
    error_type VARCHAR(50) NOT NULL,     -- 'McpTimeout' / 'AgentError' / 'ValidationError'
    error_message TEXT NOT NULL,
    stack_trace TEXT,
    context JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    INDEX idx_session_id (session_id),
    INDEX idx_error_type (error_type),
    INDEX idx_created_at (created_at DESC)
);

COMMENT ON TABLE error_logs IS '错误日志，用于调试和监控';
```

---

## 3. 实体关系图

```
workflow_sessions (1) ──< (N) agent_executions
                  │
                  └──< (N) review_tasks
                  │
                  └──< (N) sse_connections

agent_executions (1) ──< (N) tool_calls
                 │
                 └──< (N) agent_messages
                 │
                 └──< (N) decision_records

prompt_versions (独立表，通过 agent_name 关联)

error_logs (弱关联，可选外键)
```

---

## 4. JSONB 字段设计

### 4.1 workflow_sessions.state

**用途**：存储 Checkpoint 快照

**结构**：
```typescript
interface WorkflowState {
  currentExecutor: string;
  completedExecutors: string[];
  context: {
    [key: string]: unknown;  // 各 Executor 的输出
  };
  checkpointVersion: number;
}
```

**查询示例**：
```sql
-- 查询当前正在执行 IndexAdvisorExecutor 的会话
SELECT * FROM workflow_sessions
WHERE state->>'currentExecutor' = 'IndexAdvisorExecutor';

-- 查询包含特定表名的会话
SELECT * FROM workflow_sessions
WHERE state->'context'->'parsedSql'->'tables' ? 'users';
```

### 4.2 agent_executions.token_usage

**用途**：记录 Token 消耗

**结构**：
```json
{
  "prompt": 1000,
  "completion": 500,
  "total": 1500,
  "model": "gpt-4",
  "cost": 0.045
}
```

### 4.3 decision_records.evidence

**用途**：存储决策证据

**结构**：
```json
{
  "executionPlanRef": "node_id_123",
  "metrics": {
    "currentCost": 1000,
    "estimatedCost": 100,
    "rowsScanned": 1000000
  },
  "reasoning": "全表扫描导致性能问题，建议在 user_id 列创建索引"
}
```

### 4.4 review_tasks.recommendations

**用途**：存储待审核的建议

**结构**：
```json
{
  "indexRecommendations": [
    {
      "tableName": "users",
      "columns": ["email"],
      "indexType": "BTREE",
      "createDdl": "CREATE INDEX idx_users_email ON users(email)",
      "estimatedBenefit": 85.5,
      "confidence": 92.0
    }
  ],
  "sqlRewriteSuggestions": [
    {
      "original": "SELECT * FROM users WHERE ...",
      "optimized": "SELECT id, name FROM users WHERE ...",
      "reasoning": "避免 SELECT *"
    }
  ]
}
```

---

## 5. 索引策略

### 5.1 查询模式分析

| 查询场景 | 频率 | 索引需求 |
|---------|------|---------|
| 按 session_id 查询 | 高 | 外键索引 |
| 按状态查询会话 | 中 | status 索引 |
| 按时间范围查询 | 中 | created_at 降序索引 |
| 按 agent_name 统计 | 低 | agent_name 索引 |
| JSONB 字段查询 | 低 | GIN 索引（按需） |

### 5.2 JSONB 索引

**场景 1**：查询包含特定表名的会话
```sql
CREATE INDEX idx_workflow_sessions_state_tables 
ON workflow_sessions USING GIN ((state->'context'->'parsedSql'->'tables'));
```

**场景 2**：查询特定 Executor 的会话
```sql
CREATE INDEX idx_workflow_sessions_current_executor 
ON workflow_sessions ((state->>'currentExecutor'));
```

### 5.3 分区策略（可选）

**按时间分区**（适用于历史数据量大的场景）：
```sql
CREATE TABLE workflow_sessions (
    ...
) PARTITION BY RANGE (created_at);

CREATE TABLE workflow_sessions_2026_04 PARTITION OF workflow_sessions
FOR VALUES FROM ('2026-04-01') TO ('2026-05-01');
```

---

## 6. 数据保留策略

### 6.1 归档策略

| 表 | 保留时长 | 归档方式 |
|---|---------|---------|
| workflow_sessions | 90 天 | 移动到归档表 |
| agent_executions | 30 天 | 级联删除 |
| tool_calls | 30 天 | 级联删除 |
| agent_messages | 30 天 | 级联删除 |
| error_logs | 60 天 | 直接删除 |

### 6.2 归档脚本

```sql
-- 归档 90 天前的会话
INSERT INTO workflow_sessions_archive
SELECT * FROM workflow_sessions
WHERE created_at < NOW() - INTERVAL '90 days';

DELETE FROM workflow_sessions
WHERE created_at < NOW() - INTERVAL '90 days';
```

---

## 7. 与其他文档的映射关系

- **架构设计**：[ARCHITECTURE.md](./ARCHITECTURE.md)
- **Workflow 设计**：[WORKFLOW_DESIGN.md](./WORKFLOW_DESIGN.md)
- **API 规范**：[API_SPEC.md](./API_SPEC.md)
- **需求文档**：[REQUIREMENTS.md](./REQUIREMENTS.md)
