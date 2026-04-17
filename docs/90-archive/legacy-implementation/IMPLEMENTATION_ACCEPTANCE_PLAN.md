# DbOptimizer 验收计划

**目的**: 为本轮实施提供统一验收口径，确保任务完成后可验证、可收口。  
**原则**: 每完成一个任务做局部验收；每完成一个 Sprint 做阶段验收。

---

## 1. 验收分层

### 第一层: 任务级验收

- 每个任务都必须有自己的最小验证动作

### 第二层: Sprint 级验收

- 每个 Sprint 完成后要跑对应闭环

### 第三层: 版本级验收

- 所有 P0/P1 目标完成后做整体验收

---

## 2. Sprint-A 验收

目标:
- 实例调优闭环打通

### 验收流程

1. 前端进入实例调优区域
2. 输入 `databaseId` 与 `databaseType`
3. 发起配置调优任务
4. 查询工作流状态
5. 等待进入待审核状态
6. 打开审核页
7. 提交 `approve`
8. 打开历史页确认结果

### 通过标准

- 能创建 `DbConfigOptimization` session
- 状态查询接口能正确返回配置调优结果
- 审核页能渲染配置建议
- 历史页能查看配置调优详情

---

## 3. Sprint-B 验收

目标:
- 慢 SQL 自动分析闭环打通

### 验收流程

1. 启用慢 SQL 采集服务
2. 在测试数据库制造慢 SQL
3. 等待采集周期触发
4. 检查 `slow_queries`
5. 检查是否自动创建 SQL 分析任务
6. 查看历史页

### 通过标准

- 慢 SQL 被采集并落库
- 自动创建 SQL 分析任务
- 能追踪到分析结果
- 同一慢 SQL 不会在短时间内无限重复创建任务

---

## 4. Sprint-C 验收

目标:
- SQL Rewrite 与 Dashboard 增强落地

### 验收流程

1. 输入包含 `SELECT *` 的 SQL
2. 输入包含 `%like` 的 SQL
3. 输入包含函数包裹列的 SQL
4. 检查是否出现 rewrite 建议
5. 打开 dashboard
6. 检查慢 SQL 趋势数据

### 通过标准

- 至少 3 类 rewrite 规则有效
- dashboard 能展示慢 SQL 趋势

---

## 5. API 验收清单

当前关键 API:

- `POST /api/workflows/sql-analysis`
- `POST /api/workflows/db-config-optimization`
- `GET /api/workflows/{sessionId}`
- `GET /api/reviews`
- `GET /api/reviews/{taskId}`
- `POST /api/reviews/{taskId}/submit`
- `GET /api/history`
- `GET /api/history/{sessionId}`
- `GET /api/workflows/{sessionId}/events`

新增后关键 API:

- `GET /api/slow-queries`
- `GET /api/dashboard/slow-query-trend`
- `GET /api/dashboard/slow-query-alerts`

### 验收重点

1. `SqlAnalysis` 与 `DbConfigOptimization` 的结果结构正确
2. 配置调优结果不再误按 SQL 报告解析
3. 审核接口对配置调优任务可正常工作
4. 历史接口能查询配置调优与慢 SQL 关联结果

---

## 6. 数据库验收清单

重点表:

- `workflow_sessions`
- `agent_executions`
- `review_tasks`
- `tool_calls`
- `error_logs`
- `slow_queries`

### 验收重点

1. `workflow_sessions.workflow_type` 正确
2. `review_tasks.task_type` 能区分 `SqlOptimization` 与 `ConfigOptimization`
3. 慢 SQL 采集后 `slow_queries` 正常更新
4. 自动分析后 `workflow_sessions` 能看到对应任务

---

## 7. 构建与最小验证

推荐命令:

```powershell
dotnet build src\DbOptimizer.API\DbOptimizer.API.csproj
dotnet build src\DbOptimizer.AppHost\DbOptimizer.AppHost.csproj
```

```powershell
cd src\DbOptimizer.Web
npm run build
```

推荐补充:

```powershell
dotnet test
```

### 验收重点

- API 构建通过
- 前端构建通过
- 没有新增明显类型错误

---

## 8. 回归检查

每完成一个 Sprint，回归检查以下能力：

1. 现有 SQL 调优流程不能坏
2. 审核 approve / reject / adjust 不能回归
3. history / replay 不能崩
4. SSE 不能因为新结果模型出错

---

## 9. 版本级完成标准

认为“本轮目标完成”至少要满足：

1. SQL 调优闭环可用
2. 实例调优闭环可用
3. 慢 SQL 自动分析闭环可用
4. SQL Rewrite 有真实建议输出
5. dashboard 有慢 SQL 趋势
6. 文档与实现边界统一
