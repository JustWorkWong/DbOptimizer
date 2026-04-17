using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

public interface IIndexRecommendationGenerator
{
    IReadOnlyList<IndexRecommendation> Generate(
        DatabaseOptimizationEngine databaseEngine,
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyDictionary<string, TableIndexMetadata> tableIndexes);
}

/* =========================
 * 规则式索引建议生成器
 * v1 先覆盖最有价值的几类场景：
 * - WHERE 等值过滤列
 * - JOIN 关联列
 * - ORDER BY / GROUP BY 列
 * 同时尽量避开“已有索引已覆盖”的重复建议。
 * ========================= */
public sealed class IndexRecommendationGenerator : IIndexRecommendationGenerator
{
    public IReadOnlyList<IndexRecommendation> Generate(
        DatabaseOptimizationEngine databaseEngine,
        ParsedSqlResult parsedSql,
        ExecutionPlanResult executionPlan,
        IReadOnlyDictionary<string, TableIndexMetadata> tableIndexes)
    {
        var recommendations = new List<IndexRecommendation>();
        var tables = parsedSql.Tables
            .GroupBy(table => table.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        foreach (var table in tables)
        {
            var candidateColumns = CollectCandidateColumns(table, parsedSql);
            if (candidateColumns.Count == 0)
            {
                continue;
            }

            tableIndexes.TryGetValue(table.TableName, out var metadata);
            metadata ??= new TableIndexMetadata { TableName = table.TableName };

            if (IsCoveredByExistingIndexes(candidateColumns, metadata.ExistingIndexes))
            {
                continue;
            }

            var relatedIssues = executionPlan.Issues
                .Where(issue => string.Equals(issue.TableName, table.TableName, StringComparison.OrdinalIgnoreCase) ||
                                issue.TableName is null)
                .ToList();

            var estimatedBenefit = EstimateBenefit(relatedIssues, candidateColumns.Count);
            var evidenceRefs = BuildEvidenceRefs(table, candidateColumns, relatedIssues, metadata);

            recommendations.Add(new IndexRecommendation
            {
                TableName = table.TableName,
                Columns = candidateColumns,
                IndexType = "BTREE",
                CreateDdl = BuildCreateDdl(databaseEngine, table.TableName, candidateColumns),
                EstimatedBenefit = estimatedBenefit,
                Reasoning = BuildReasoning(table.TableName, candidateColumns, relatedIssues, metadata),
                EvidenceRefs = evidenceRefs,
                Confidence = CalculateConfidence(relatedIssues, metadata)
            });
        }

        return recommendations;
    }

    private static List<string> CollectCandidateColumns(ParsedTableReference table, ParsedSqlResult parsedSql)
    {
        var tableAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            table.TableName
        };

        if (!string.IsNullOrWhiteSpace(table.Alias))
        {
            tableAliases.Add(table.Alias);
        }

        var columns = new List<string>();

        AddDistinct(columns, parsedSql.WhereConditions
            .Where(predicate => MatchesAlias(tableAliases, predicate.TableAlias) && !string.IsNullOrWhiteSpace(predicate.ColumnName))
            .Select(predicate => predicate.ColumnName!));

        AddDistinct(columns, parsedSql.Joins
            .SelectMany(join => join.ConditionColumns)
            .Where(column => MatchesAlias(tableAliases, column.TableAlias))
            .Select(column => column.ColumnName));

        AddDistinct(columns, parsedSql.GroupBy
            .Where(item => MatchesAlias(tableAliases, item.TableAlias) && !string.IsNullOrWhiteSpace(item.ColumnName))
            .Select(item => item.ColumnName!));

        AddDistinct(columns, parsedSql.OrderBy
            .Where(item => MatchesAlias(tableAliases, item.TableAlias) && !string.IsNullOrWhiteSpace(item.ColumnName))
            .Select(item => item.ColumnName!));

        return columns;
    }

    private static bool MatchesAlias(IReadOnlySet<string> aliases, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return aliases.Contains(candidate);
    }

    private static void AddDistinct(List<string> target, IEnumerable<string> source)
    {
        foreach (var item in source.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (target.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Add(item);
        }
    }

    private static bool IsCoveredByExistingIndexes(
        IReadOnlyList<string> candidateColumns,
        IReadOnlyCollection<ExistingIndexDefinition> existingIndexes)
    {
        foreach (var existingIndex in existingIndexes)
        {
            if (existingIndex.Columns.Count == 0)
            {
                continue;
            }

            var covered = candidateColumns.Count <= existingIndex.Columns.Count &&
                          candidateColumns
                              .Select((column, index) => string.Equals(column, existingIndex.Columns[index], StringComparison.OrdinalIgnoreCase))
                              .All(isMatch => isMatch);

            if (covered)
            {
                return true;
            }
        }

        return false;
    }

    private static double EstimateBenefit(IReadOnlyCollection<ExecutionPlanIssue> issues, int candidateColumnCount)
    {
        var score = 15d + candidateColumnCount * 8;

        foreach (var issue in issues)
        {
            score += issue.Type switch
            {
                "FullTableScan" => 35,
                "IndexNotUsed" => 20,
                "Filesort" => 12,
                "TempTable" => 10,
                _ => 6
            };
        }

        return Math.Min(95, Math.Round(score, 2));
    }

    private static List<string> BuildEvidenceRefs(
        ParsedTableReference table,
        IReadOnlyList<string> candidateColumns,
        IReadOnlyCollection<ExecutionPlanIssue> issues,
        TableIndexMetadata metadata)
    {
        var references = new List<string>
        {
            $"parsedSql.table:{table.TableName}",
            $"parsedSql.columns:{string.Join(",", candidateColumns)}"
        };

        references.AddRange(issues.Select(issue => $"executionPlan.issue:{issue.Type}"));

        if (metadata.ExistingIndexes.Count > 0)
        {
            references.Add($"showIndexes.count:{metadata.ExistingIndexes.Count}");
        }

        return references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildReasoning(
        string tableName,
        IReadOnlyList<string> candidateColumns,
        IReadOnlyCollection<ExecutionPlanIssue> issues,
        TableIndexMetadata metadata)
    {
        var reasons = new List<string>
        {
            $"表 {tableName} 的过滤/关联/排序列集中在 {string.Join(", ", candidateColumns)}。"
        };

        if (issues.Count > 0)
        {
            reasons.Add($"执行计划中识别到 {string.Join("、", issues.Select(issue => issue.Type).Distinct())}。");
        }

        if (metadata.ExistingIndexes.Count == 0)
        {
            reasons.Add("当前未识别到覆盖这些列的已有索引。");
        }
        else
        {
            reasons.Add("已结合已有索引前缀覆盖情况做去重判断。");
        }

        return string.Join(" ", reasons);
    }

    private static double CalculateConfidence(
        IReadOnlyCollection<ExecutionPlanIssue> issues,
        TableIndexMetadata metadata)
    {
        var confidence = issues.Count > 0 ? 0.82 : 0.62;

        if (metadata.Warnings.Count > 0)
        {
            confidence -= 0.12;
        }

        return Math.Max(0.35, Math.Round(confidence, 2));
    }

    private static string BuildCreateDdl(
        DatabaseOptimizationEngine databaseEngine,
        string tableName,
        IReadOnlyList<string> columns)
    {
        var indexName = $"idx_{tableName}_{string.Join("_", columns)}";

        return databaseEngine switch
        {
            DatabaseOptimizationEngine.MySql =>
                $"CREATE INDEX `{indexName}` ON `{tableName}` ({string.Join(", ", columns.Select(column => $"`{column}`"))});",
            _ =>
                $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" ({string.Join(", ", columns.Select(column => $"\"{column}\""))});"
        };
    }
}
