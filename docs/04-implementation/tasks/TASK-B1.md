# TASK-B1

## Goal

在 infrastructure 层引入 MAF 包，并建立最小 runtime/factory/state-store 结构。

## Dependencies

- TASK-A2

## Read First

1. [../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md](../../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md)
2. [../IMPLEMENTATION_TECHNICAL_PLAN.md](../IMPLEMENTATION_TECHNICAL_PLAN.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/Maf/Runtime/MafWorkflowRuntimeOptions.cs`
   - 属性：`CheckpointFlushEnabled` `MaxConcurrentRuns`
2. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafWorkflowFactory.cs`
   - `Workflow BuildSqlAnalysisWorkflow();`
   - `Workflow BuildDbConfigWorkflow();`
3. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafWorkflowRuntime.cs`
   - `Task<WorkflowStartResponse> StartSqlAnalysisAsync(SqlAnalysisWorkflowCommand command, CancellationToken cancellationToken = default);`
   - `Task<WorkflowStartResponse> StartDbConfigOptimizationAsync(DbConfigWorkflowCommand command, CancellationToken cancellationToken = default);`
   - `Task<WorkflowResumeResponse> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);`
   - `Task<WorkflowCancelResponse> CancelAsync(Guid sessionId, CancellationToken cancellationToken = default);`
4. `src/DbOptimizer.Infrastructure/Maf/Runtime/IMafRunStateStore.cs`
   - `Task SaveAsync(MafCheckpointEnvelope checkpoint, CancellationToken cancellationToken = default);`
   - `Task<MafCheckpointEnvelope?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);`
   - `Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);`
5. `src/DbOptimizer.Infrastructure/Mcp/IMcpFallbackStrategy.cs`
   - `Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> mcpCall, Func<Task<T>> fallback, CancellationToken cancellationToken = default);`

## Files To Modify

- `src/DbOptimizer.Infrastructure/DbOptimizer.Infrastructure.csproj`
- `src/DbOptimizer.API/Program.cs`

## Steps

1. 引入 `Microsoft.Agents.AI.Workflows` 包（版本 1.0.0-rc4，参考 MAF_WORKFLOW_ARCHITECTURE.md）。
2. 新建 runtime 接口与 options。
3. 新建 MCP fallback strategy 接口（用于超时/错误处理）。
4. 在 `Program.cs` 注册 runtime/factory/state-store 接口。
5. 不要删除旧 runner，先并行保留。

## Verification

1. `dotnet restore`
2. `dotnet build e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.Infrastructure\\DbOptimizer.Infrastructure.csproj`
3. 依赖注入容器能解析新增接口

## Done Criteria

- MAF 包已进入项目依赖
- runtime 相关接口和 options 文件齐备
- API 项目可注册这些服务
