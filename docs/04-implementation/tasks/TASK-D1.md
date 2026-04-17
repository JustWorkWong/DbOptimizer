# TASK-D1

## Goal

使用 MAF executor 重建配置调优 workflow，直到生成 draft 或 final 结果。

## Dependencies

- TASK-A1
- TASK-A2
- TASK-B1
- TASK-B2

## Read First

1. [../../03-design/workflow/DB_CONFIG_WORKFLOW_DESIGN.md](../../03-design/workflow/DB_CONFIG_WORKFLOW_DESIGN.md)
2. [../../03-design/data/DATA_MODEL.md](../../03-design/data/DATA_MODEL.md)

## New Files

- `src/DbOptimizer.Infrastructure/Maf/DbConfig/DbConfigWorkflowMessages.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/DbConfigInputValidationExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigCollectorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigAnalyzerMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigCoordinatorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/Executors/ConfigHumanReviewGateExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/DbConfig/IConfigReviewAdjustmentService.cs`

## Required Methods

1. `HandleAsync(DbConfigWorkflowCommand, IWorkflowContext, CancellationToken)`
2. `HandleAsync(ConfigSnapshotCollectedMessage, IWorkflowContext, CancellationToken)`
3. `HandleAsync(ConfigRecommendationsGeneratedMessage, IWorkflowContext, CancellationToken)`
4. `HandleAsync(ReviewDecisionResponseMessage, IWorkflowContext)`

## Option Rules

- `AllowFallbackSnapshot = true` -> 采集失败允许 fallback snapshot
- `AllowFallbackSnapshot = false` -> 采集失败直接 failed
- `RequireHumanReview = false` -> review gate 直接 completed

## Files To Modify

- `src/DbOptimizer.API/Program.cs`

## Steps

1. 新建 DB config 消息合同。
2. 实现 validation/collector/analyzer/coordinator/review gate executors。
3. 复用现有 `IConfigCollectionProvider` 和 `IConfigRuleEngine`。
4. 将最终结果序列化为 `db-config-optimization-report`。

## Verification

1. 配置 workflow 可跑到 draft 结果
2. `resultType == db-config-optimization-report`
3. `requireHumanReview=false` 时可直接 completed

## Done Criteria

- DB config workflow 已 MAF 化
- 前后链路能产生统一结果壳
