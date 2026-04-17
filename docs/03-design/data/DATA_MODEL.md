# Data Model

## 1. 设计原则

数据模型以 MAF 迁移方案为准，不再沿用“单体 checkpoint JSON 即全部 workflow 状态”的旧口径。

状态分层：

1. `workflow_sessions`
   - 业务查询主表
   - 保存状态、结果类型、来源、MAF 运行态引用
2. `review_tasks`
   - 人工审核业务表
   - 保存 `WorkflowResultEnvelope` payload
3. `slow_queries`
   - 慢 SQL 采集表
   - 保存最近一次关联分析 session

## 2. `workflow_sessions`

| 字段 | 类型 | 说明 |
|---|---|---|
| `session_id` | `uuid pk` | workflow 会话 ID |
| `workflow_type` | `varchar(50)` | `SqlAnalysis` / `DbConfigOptimization` |
| `status` | `varchar(20)` | 业务状态 |
| `state` | `jsonb` | 业务可查询快照，不是 MAF 原始 checkpoint |
| `engine_type` | `varchar(50)` | 固定为 `maf` |
| `engine_run_id` | `varchar(100)` | MAF run ID |
| `engine_checkpoint_ref` | `varchar(200)` | 最近一次 checkpoint 引用 |
| `engine_state` | `jsonb` | 运行期恢复附加信息 |
| `result_type` | `varchar(100)` | `sql-optimization-report` / `db-config-optimization-report` |
| `source_type` | `varchar(50)` | `manual` / `slow-query` |
| `source_ref_id` | `uuid null` | 关联 slow query 等来源 |
| `error_message` | `text null` | 错误信息 |
| `created_at` | `timestamptz` | 创建时间 |
| `updated_at` | `timestamptz` | 更新时间 |
| `completed_at` | `timestamptz null` | 完成时间 |

## 3. `review_tasks`

| 字段 | 类型 | 说明 |
|---|---|---|
| `task_id` | `uuid pk` | review task ID |
| `session_id` | `uuid fk` | 关联 workflow |
| `task_type` | `varchar(50)` | 保留索引与报表用途 |
| `request_id` | `varchar(100)` | MAF request correlation key |
| `engine_run_id` | `varchar(100)` | MAF run ID |
| `engine_checkpoint_ref` | `varchar(200)` | MAF checkpoint reference |
| `recommendations` | `jsonb` | 存储 `WorkflowResultEnvelope` payload |
| `status` | `varchar(20)` | `Pending` / `Approved` / `Rejected` / `Adjusted` |
| `reviewer_comment` | `text null` | 审核意见 |
| `adjustments` | `jsonb null` | reviewer 调整参数 |
| `created_at` | `timestamptz` | 创建时间 |
| `reviewed_at` | `timestamptz null` | 审核时间 |

## 4. `slow_queries`

| 字段 | 类型 | 说明 |
|---|---|---|
| `query_id` | `uuid pk` | 慢 SQL ID |
| `database_id` | `varchar(100)` | 数据库标识 |
| `database_type` | `varchar(50)` | 数据库类型 |
| `query_hash` | `varchar(64)` | 指纹 hash |
| `sql_fingerprint` | `text` | 归一化 SQL |
| `original_sql` | `text` | 原 SQL |
| `avg_execution_time` | `numeric` | 平均耗时 |
| `execution_count` | `int` | 执行次数 |
| `last_seen_at` | `timestamptz` | 最近出现时间 |
| `latest_analysis_session_id` | `uuid null` | 最近一次分析 workflow |

## 5. 状态对象

`state` / `engine_state` 对应的应用层对象定义见：

- [../workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md](../workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md)
- [../api/API_OVERVIEW.md](../api/API_OVERVIEW.md)

不要再按旧式 `currentExecutor/completedExecutors/context` 结构设计新代码。

`review_tasks.request_id + engine_run_id + engine_checkpoint_ref` 是必填恢复字段，不是扩展字段。
