# AI Execution Playbook

## 1. 任务边界

一次只执行一个 `TASK-*`。

禁止：

- 同时改两个 Epic
- 在没有契约文档的情况下自由发挥 API 结构
- 绕过 MAF 继续扩展旧 `WorkflowRunner`

## 2. 必读材料

每个任务最少读取：

1. `00-overview/SYSTEM_SCOPE_AND_STATUS.md`
2. 当前任务对应的 `03-design` 文档
3. `04-implementation/IMPLEMENTATION_TASK_CHECKLIST.md`

如果任务涉及 API，必须额外读取对应 endpoint contract：

- workflow -> `03-design/api/WORKFLOW_API_CONTRACT.md`
- review -> `03-design/api/REVIEW_API_CONTRACT.md`
- dashboard/slow-query -> `03-design/api/DASHBOARD_API_CONTRACT.md`
- SSE -> `03-design/api/SSE_EVENT_CONTRACT.md`

## 3. 上下文控制

单次任务只保留：

- 当前 Epic 的 1 个任务
- 相关 API 契约文档
- 相关 workflow 设计文档
- 直接要改的代码文件

如果任务跨越超过 6 个核心文件，先做摘要再继续。

## 4. 摘要与交接

任务结束必须输出：

1. 本任务新增类
2. 每个类新增的方法与签名
3. API 变更
4. 数据库变更
5. 验证命令
6. 未解决问题

如果中途停止，也必须输出这 6 项。

## 5. MAF 约束

1. 新 workflow 逻辑必须落在 MAF executor/factory/runtime 体系下。
2. 不允许新增第二套自定义 workflow engine。
3. review gate 必须走 request/response，不允许用轮询模拟暂停。

## 6. 结果契约约束

前后端只能按 `WorkflowResultEnvelope.resultType` 做分发，禁止：

- 用 `workflowType` 和 `taskType` 双重猜测
- 直接把 `OptimizationReport` 暴露到 API

## 7. 提交前自检

1. 是否使用 MAF。
2. 是否改动了契约文档定义。
3. 是否补了验证。
4. 是否记录了新增类和方法。
5. 当前 Epic 依赖是否满足。
