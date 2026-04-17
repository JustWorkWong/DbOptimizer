using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * CoordinatorExecutor
 * 职责：
 * 1) 汇总 ParsedSql / ExecutionPlan / IndexRecommendations
 * 2) 输出最终可审阅的 OptimizationReport
 * 3) 维护简洁的 summary、confidence 与 evidence chain
 * ========================= */
public sealed class CoordinatorExecutor(ILogger<CoordinatorExecutor> logger) : IWorkflowExecutor
{
    public string Name => "CoordinatorExecutor";

    public Task<WorkflowExecutorResult> ExecuteAsync(WorkflowContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveInput(context, out var parsedSql, out var executionPlan, out var indexRecommendations))
        {
            return Task.FromResult(WorkflowExecutorResult.Failure("CoordinatorExecutor 缺少汇总所需的上下文数据。"));
        }

        var report = BuildReport(parsedSql, executionPlan, indexRecommendations);
        context.Set(WorkflowContextKeys.FinalResult, report);

        logger.LogInformation(
            "Coordinator executor completed. SessionId={SessionId}, RecommendationCount={RecommendationCount}, OverallConfidence={OverallConfidence}",
            context.SessionId,
            report.IndexRecommendations.Count,
            report.OverallConfidence);

        return Task.FromResult(WorkflowExecutorResult.Success(report));
    }

    private static bool TryResolveInput(
        WorkflowContext context,
        out ParsedSqlResult parsedSql,
        out ExecutionPlanResult executionPlan,
        out List<IndexRecommendation> indexRecommendations)
    {
        parsedSql = new ParsedSqlResult();
        executionPlan = new ExecutionPlanResult();
        indexRecommendations = new List<IndexRecommendation>();

        return context.TryGet<ParsedSqlResult>(WorkflowContextKeys.ParsedSql, out var parsed) &&
               parsed is not null &&
               context.TryGet<ExecutionPlanResult>(WorkflowContextKeys.ExecutionPlan, out var plan) &&
               plan is not null &&
               context.TryGet<List<IndexRecommendation>>(WorkflowContextKeys.IndexRecommendations, out var recommendations) &&
               recommendations is not null &&
               Assign(parsed, plan, recommendations, out parsedSql, out executionPlan, out indexRecommendations);
    }

    private static bool Assign(
        ParsedSqlResult parsed,
        ExecutionPlanResult plan,
        List<IndexRecommendation> recommendations,
        out ParsedSqlResult parsedSql,
        out ExecutionPlanResult executionPlan,
        out List<IndexRecommendation> indexRecommendations)
    {
        parsedSql = parsed;
        executionPlan = plan;
        indexRecommendations = recommendations;
        return true;
    }

    private static OptimizationReport BuildReport(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyList<IndexRecommendation> indexRecommendations)
    {
        var report = new OptimizationReport
        {
            Summary = BuildSummary(parsedSql, executionPlan, indexRecommendations),
            IndexRecommendations = indexRecommendations.ToList(),
            OverallConfidence = CalculateOverallConfidence(parsedSql, executionPlan, indexRecommendations),
            EvidenceChain = BuildEvidenceChain(parsedSql, executionPlan, indexRecommendations),
            Warnings = BuildWarnings(parsedSql, executionPlan, indexRecommendations)
        };

        report.Metadata["queryType"] = parsedSql.QueryType;
        report.Metadata["tableCount"] = parsedSql.Tables.Count;
        report.Metadata["planIssueCount"] = executionPlan.Issues.Count;
        report.Metadata["recommendationCount"] = indexRecommendations.Count;
        report.Metadata["usedFallback"] = executionPlan.UsedFallback;
        report.Metadata["elapsedMs"] = executionPlan.ElapsedMs;

        return report;
    }

    private static string BuildSummary(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyList<IndexRecommendation> indexRecommendations)
    {
        if (indexRecommendations.Count == 0)
        {
            return $"已分析 {parsedSql.Tables.Count} 张表的 {parsedSql.QueryType} 语句，当前未生成明确索引建议，建议结合原始执行计划继续人工复核。";
        }

        var issueTypes = executionPlan.Issues
            .Select(issue => issue.Type)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var issueSummary = issueTypes.Count > 0
            ? $"执行计划中识别到 {string.Join("、", issueTypes)}。"
            : "执行计划未识别出强特征瓶颈。";

        return $"已分析 {parsedSql.Tables.Count} 张表，生成 {indexRecommendations.Count} 条索引建议。{issueSummary}";
    }

    private static double CalculateOverallConfidence(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyList<IndexRecommendation> indexRecommendations)
    {
        var baseScore = indexRecommendations.Count == 0
            ? 0.55
            : indexRecommendations.Average(recommendation => recommendation.Confidence);

        baseScore -= Math.Min(0.15, parsedSql.Warnings.Count * 0.02);
        baseScore -= Math.Min(0.15, executionPlan.Warnings.Count * 0.03);

        if (executionPlan.UsedFallback)
        {
            baseScore -= 0.03;
        }

        return Math.Max(0.35, Math.Round(baseScore, 2));
    }

    private static List<EvidenceItem> BuildEvidenceChain(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyList<IndexRecommendation> indexRecommendations)
    {
        var evidence = new List<EvidenceItem>();

        foreach (var table in parsedSql.Tables)
        {
            evidence.Add(new EvidenceItem
            {
                SourceType = "ParsedSql",
                Reference = $"table:{table.TableName}",
                Description = $"SQL 解析识别到目标表 {table.TableName}。",
                Confidence = table.Confidence,
                Snippet = table.SourceFragment
            });
        }

        foreach (var issue in executionPlan.Issues)
        {
            evidence.Add(new EvidenceItem
            {
                SourceType = "ExecutionPlan",
                Reference = issue.Type,
                Description = issue.Description,
                Confidence = Math.Min(1, Math.Round(issue.ImpactScore / 100, 2)),
                Snippet = issue.Evidence
            });
        }

        foreach (var recommendation in indexRecommendations)
        {
            evidence.Add(new EvidenceItem
            {
                SourceType = "IndexRecommendation",
                Reference = recommendation.TableName,
                Description = recommendation.Reasoning,
                Confidence = recommendation.Confidence,
                Snippet = recommendation.CreateDdl
            });
        }

        return evidence;
    }

    private static List<string> BuildWarnings(
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyList<IndexRecommendation> indexRecommendations)
    {
        var warnings = new List<string>();
        warnings.AddRange(parsedSql.Warnings);
        warnings.AddRange(executionPlan.Warnings);

        if (indexRecommendations.Count == 0)
        {
            warnings.Add("当前未生成索引建议，建议结合执行计划与业务场景人工复核。");
        }

        return warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
