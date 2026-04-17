# TASK-A1

## Status

- `Completed`
- Completed on: `2026-04-17`

## Implementation Result

1. Added `src/DbOptimizer.Core/Models/WorkflowResultEnvelope.cs`
2. Added `src/DbOptimizer.Infrastructure/Workflows/Serialization/WorkflowResultSerializer.cs`
3. Updated `src/DbOptimizer.API/Api/WorkflowApi.cs`
4. Updated `src/DbOptimizer.API/Api/ReviewApi.cs`
5. Updated `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
6. Updated `src/DbOptimizer.API/Program.cs`
7. Updated `src/DbOptimizer.Web/src/api.ts`
8. Updated `src/DbOptimizer.Web/src/App.vue`

## Verification Result

1. `dotnet build e:\wfcodes\DbOptimizer\src\DbOptimizer.API\DbOptimizer.API.csproj`
2. `npm --prefix e:\wfcodes\DbOptimizer\src\DbOptimizer.Web run build`
3. Review pass requested before close-out

## Goal

统一 workflow、review、history 三条 API 面的结果结构，所有最终结果都改为 `WorkflowResultEnvelope`。

## Dependencies

- 无

## Read First

1. [../../03-design/api/API_OVERVIEW.md](../../03-design/api/API_OVERVIEW.md)
2. [../../03-design/api/WORKFLOW_API_CONTRACT.md](../../03-design/api/WORKFLOW_API_CONTRACT.md)
3. [../../03-design/api/REVIEW_API_CONTRACT.md](../../03-design/api/REVIEW_API_CONTRACT.md)
4. [../../03-design/api/DASHBOARD_API_CONTRACT.md](../../03-design/api/DASHBOARD_API_CONTRACT.md)

## New Classes

1. `src/DbOptimizer.API/Contracts/Common/WorkflowResultEnvelope.cs`
   - `public sealed record WorkflowResultEnvelope(string ResultType, string DisplayName, string Summary, JsonElement Data, IReadOnlyDictionary<string, JsonElement>? Metadata);`
2. `src/DbOptimizer.Infrastructure/Workflows/Serialization/IWorkflowResultSerializer.cs`
   - `WorkflowResultEnvelope ToEnvelope(OptimizationReport report, string databaseId, string databaseType);`
   - `WorkflowResultEnvelope ToEnvelope(ConfigOptimizationReport report);`
   - `JsonElement ToJsonElement<T>(T value);`
3. `src/DbOptimizer.Infrastructure/Workflows/Serialization/WorkflowResultSerializer.cs`
   - 实现上述接口

## Files To Modify

- `src/DbOptimizer.API/Api/WorkflowApi.cs`
- `src/DbOptimizer.API/Api/ReviewApi.cs`
- `src/DbOptimizer.API/Api/DashboardAndHistoryApi.cs`
- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.API/Program.cs`

## API Changes

1. `WorkflowStatusResponse.Result` 改为 `WorkflowResultEnvelope?`
2. `ReviewListItemResponse.Recommendations` 改为 `WorkflowResultEnvelope`
3. `ReviewDetailResponse.Recommendations` 改为 `WorkflowResultEnvelope`
4. `HistoryDetailResponse.Result` 改为 `WorkflowResultEnvelope?`

## Database Changes

- 无数据库字段新增
- 仅调整 `review_tasks.recommendations` 的序列化语义为 `WorkflowResultEnvelope`

## Steps

1. 新增 common contracts 文件。
2. 新增 serializer 接口和实现。
3. 替换 API response DTO 中旧 `OptimizationReport` 暴露点。
4. 替换 review/history 反序列化逻辑。
5. 更新前端 `api.ts` 类型定义。

## Verification

1. `dotnet build e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.API\\DbOptimizer.API.csproj`
2. `dotnet build e:\\wfcodes\\DbOptimizer\\src\\DbOptimizer.Infrastructure\\DbOptimizer.Infrastructure.csproj`
3. 检查 `src/DbOptimizer.Web/src/api.ts` 不再把 `OptimizationReport` 作为 API 出口类型

## Done Criteria

- API 层对外不再直接暴露 `OptimizationReport`
- 前端可按 `resultType` 渲染结果
- SQL 与配置调优都可被 serializer 转成统一壳
