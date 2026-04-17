# TASK-G1

## Goal

补齐 PromptVersion 的后端 API 和前端管理页面。

## Dependencies

- TASK-A1

## Read First

1. [../../03-design/data/DATA_MODEL.md](../../03-design/data/DATA_MODEL.md)
2. [../../04-implementation/MASTER_DELIVERY_ROADMAP.md](../MASTER_DELIVERY_ROADMAP.md)

## New Classes

1. `src/DbOptimizer.Infrastructure/Prompts/IPromptVersionService.cs`
   - `Task<PagedResponse<PromptVersionItemDto>> ListAsync(...)`
   - `Task<Guid> CreateAsync(CreatePromptVersionRequest request, CancellationToken cancellationToken = default)`
   - `Task ActivateAsync(Guid versionId, CancellationToken cancellationToken = default)`
   - `Task RollbackAsync(string agentName, int versionNumber, CancellationToken cancellationToken = default)`
2. `src/DbOptimizer.Infrastructure/Prompts/PromptVersionService.cs`
3. `src/DbOptimizer.API/Api/PromptVersionApi.cs`

## Files To Modify

- `src/DbOptimizer.API/Program.cs`
- `src/DbOptimizer.Web/src/api.ts`
- `src/DbOptimizer.Web/src/App.vue`

## API Endpoints

- `GET /api/prompt-versions`
- `POST /api/prompt-versions`
- `POST /api/prompt-versions/{versionId}/activate`
- `POST /api/prompt-versions/rollback`

## Steps

1. 新建 prompt version service。
2. 暴露最小 API。
3. 在前端增加管理入口。

## Verification

1. 可列出 prompt versions
2. 可切换 active version
3. 可执行 rollback

## Done Criteria

- PromptVersion 不再只是表结构
- 前后端都具备最小管理能力
