# TASK-C1

## Goal

使用 MAF executor 重建 SQL workflow 主链，直到生成 draft 结果。

## Dependencies

- TASK-A1
- TASK-B1
- TASK-B2

## Read First

1. [../../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md](../../03-design/workflow/SQL_ANALYSIS_WORKFLOW_DESIGN.md)
2. [../../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md](../../03-design/workflow/WORKFLOW_CONTEXT_AND_CHECKPOINTS.md)

## New Files

- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/SqlAnalysisWorkflowMessages.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlInputValidationExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlParserMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/ExecutionPlanMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/IndexAdvisorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlRewriteMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/Executors/SqlCoordinatorMafExecutor.cs`
- `src/DbOptimizer.Infrastructure/Maf/SqlAnalysis/ISqlRewriteAdvisor.cs`

## Required Methods

1. `HandleAsync(SqlAnalysisWorkflowCommand, IWorkflowContext, CancellationToken)`
2. `HandleAsync(SqlParsingCompletedMessage, IWorkflowContext, CancellationToken)`
3. `HandleAsync(ExecutionPlanCompletedMessage, IWorkflowContext, CancellationToken)`
4. `HandleAsync(IndexRecommendationCompletedMessage, IWorkflowContext, CancellationToken)`
5. `HandleAsync(SqlRewriteCompletedMessage, IWorkflowContext, CancellationToken)`

## Files To Modify

- `src/DbOptimizer.API/Program.cs`

## Option Rules

- `EnableIndexRecommendation = false` -> 输出空 `IndexRecommendations`
- `EnableSqlRewrite = false` -> 输出空 `SqlRewriteSuggestions`
- 不改变 graph 结构，只改变 gate 节点行为

## Steps

1. 新建 SQL workflow message contracts。
2. 实现 validation/parser/plan/index/rewrite/coordinator executors。
3. 复用现有 deterministic domain service。
4. 先不处理 review gate。

## Verification

1. SQL workflow 可执行到 `SqlOptimizationDraftReadyMessage`
2. `WorkflowResultEnvelope.resultType == sql-optimization-report`
3. 关闭 index/rewrite 开关时，消息流不中断

## Done Criteria

- SQL workflow 主链完成 MAF 化
- draft 结果可生成
- option 开关语义可执行
