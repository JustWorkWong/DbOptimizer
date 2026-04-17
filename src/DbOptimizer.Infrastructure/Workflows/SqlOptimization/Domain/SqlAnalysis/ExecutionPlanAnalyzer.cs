using DbOptimizer.Core.Models;
using System.Text.Json;

namespace DbOptimizer.Infrastructure.Workflows;

internal interface IExecutionPlanAnalyzer
{
    ExecutionPlanResult Analyze(
        DatabaseOptimizationEngine databaseEngine,
        ParsedSqlResult parsedSql,
        ExecutionPlanInvocationResult invocationResult);
}

/* =========================
 * 执行计划分析器
 * 先抽取最小可用的瓶颈信息：
 * - FullTableScan / IndexNotUsed / Filesort / TempTable / NestedLoop
 * - 基础指标：rows / cost / fallback / elapsed
 * 对无法结构化识别的内容保留 rawPlan 与 warning，保证后续可继续演进。
 * ========================= */
internal sealed class ExecutionPlanAnalyzer : IExecutionPlanAnalyzer
{
    public ExecutionPlanResult Analyze(
        DatabaseOptimizationEngine databaseEngine,
        ParsedSqlResult parsedSql,
        ExecutionPlanInvocationResult invocationResult)
    {
        var result = new ExecutionPlanResult
        {
            DatabaseEngine = databaseEngine.ToString(),
            ToolName = invocationResult.ToolName,
            RawPlan = invocationResult.RawText,
            UsedFallback = invocationResult.UsedFallback,
            AttemptCount = invocationResult.AttemptCount,
            DiagnosticTag = invocationResult.DiagnosticTag,
            ElapsedMs = invocationResult.ElapsedMs
        };

        result.Metrics["elapsedMs"] = invocationResult.ElapsedMs;
        result.Metrics["usedFallback"] = invocationResult.UsedFallback;

        if (string.IsNullOrWhiteSpace(invocationResult.RawText))
        {
            result.IsPartial = true;
            result.Warnings.Add("执行计划为空，无法提取性能问题。");
            return result;
        }

        switch (databaseEngine)
        {
            case DatabaseOptimizationEngine.MySql:
                AnalyzeMySqlPlan(parsedSql, invocationResult.RawText, result);
                break;
            case DatabaseOptimizationEngine.PostgreSql:
                AnalyzePostgreSqlPlan(parsedSql, invocationResult.RawText, result);
                break;
            default:
                result.IsPartial = true;
                result.Warnings.Add("未知数据库类型，当前仅保留原始执行计划。");
                break;
        }

        if (result.Issues.Count == 0)
        {
            result.Warnings.Add("未识别出明确的执行计划瓶颈，当前保留原始执行计划供后续执行器继续判断。");
        }

        return result;
    }

    private static void AnalyzeMySqlPlan(ParsedSqlResult parsedSql, string rawPlan, ExecutionPlanResult result)
    {
        if (!TryParseJsonArray(rawPlan, out var rows))
        {
            result.IsPartial = true;
            result.Warnings.Add("MySQL 执行计划不是标准 JSON 数组，当前仅保留原始文本。");
            return;
        }

        long totalRows = 0;
        foreach (var row in rows)
        {
            var tableName = GetString(row, "table") ?? parsedSql.Tables.FirstOrDefault()?.TableName;
            var accessType = GetString(row, "type");
            var key = GetString(row, "key");
            var extra = GetString(row, "Extra") ?? GetString(row, "extra");
            totalRows += GetLong(row, "rows");

            if (string.Equals(accessType, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ExecutionPlanIssue
                {
                    Type = "FullTableScan",
                    TableName = tableName,
                    ImpactScore = 85,
                    Description = $"表 {tableName ?? "Unknown"} 使用全表扫描。",
                    Evidence = JsonSerializer.Serialize(row)
                });
            }

            if (string.IsNullOrWhiteSpace(key) && !string.Equals(accessType, "const", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ExecutionPlanIssue
                {
                    Type = "IndexNotUsed",
                    TableName = tableName,
                    ImpactScore = 70,
                    Description = $"表 {tableName ?? "Unknown"} 未命中索引。",
                    Evidence = JsonSerializer.Serialize(row)
                });
            }

            if (!string.IsNullOrWhiteSpace(extra) &&
                extra.Contains("filesort", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ExecutionPlanIssue
                {
                    Type = "Filesort",
                    TableName = tableName,
                    ImpactScore = 55,
                    Description = $"表 {tableName ?? "Unknown"} 存在 filesort。",
                    Evidence = extra
                });
            }

            if (!string.IsNullOrWhiteSpace(extra) &&
                extra.Contains("temporary", StringComparison.OrdinalIgnoreCase))
            {
                result.Issues.Add(new ExecutionPlanIssue
                {
                    Type = "TempTable",
                    TableName = tableName,
                    ImpactScore = 50,
                    Description = $"表 {tableName ?? "Unknown"} 触发临时表。",
                    Evidence = extra
                });
            }
        }

        result.Metrics["estimatedRows"] = totalRows;
        result.Metrics["planNodeCount"] = rows.Count;
    }

    private static void AnalyzePostgreSqlPlan(ParsedSqlResult parsedSql, string rawPlan, ExecutionPlanResult result)
    {
        if (!TryParseJsonDocument(rawPlan, out var document))
        {
            result.IsPartial = true;
            result.Warnings.Add("PostgreSQL 执行计划不是标准 JSON，当前仅做关键词分析。");
            AnalyzePostgreSqlTextFallback(parsedSql, rawPlan, result);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var planElement = TryResolvePostgreSqlPlanElement(root);
            if (planElement is null)
            {
                result.IsPartial = true;
                result.Warnings.Add("未找到 PostgreSQL Plan 节点，当前仅保留原始执行计划。");
                return;
            }

            var nodeCount = 0;
            WalkPostgreSqlPlan(planElement.Value, result, ref nodeCount);
            result.Metrics["planNodeCount"] = nodeCount;
        }
    }

    private static void WalkPostgreSqlPlan(JsonElement plan, ExecutionPlanResult result, ref int nodeCount)
    {
        nodeCount++;

        var nodeType = GetString(plan, "Node Type");
        var relationName = GetString(plan, "Relation Name");
        var totalCost = GetDouble(plan, "Total Cost");
        var planRows = GetLong(plan, "Plan Rows");

        if (!string.IsNullOrWhiteSpace(nodeType) &&
            nodeType.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(new ExecutionPlanIssue
            {
                Type = "FullTableScan",
                TableName = relationName,
                ImpactScore = 85,
                Description = $"表 {relationName ?? "Unknown"} 存在 Seq Scan。",
                Evidence = plan.GetRawText()
            });
        }

        if (!string.IsNullOrWhiteSpace(nodeType) &&
            nodeType.Contains("Nested Loop", StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(new ExecutionPlanIssue
            {
                Type = "NestedLoop",
                TableName = relationName,
                ImpactScore = 60,
                Description = "执行计划中出现 Nested Loop，需要结合驱动表与过滤条件继续关注。",
                Evidence = plan.GetRawText()
            });
        }

        if (totalCost > 0)
        {
            result.Metrics["totalCost"] = totalCost;
        }

        if (planRows > 0)
        {
            result.Metrics["estimatedRows"] = planRows;
        }

        if (plan.TryGetProperty("Plans", out var childPlans) && childPlans.ValueKind == JsonValueKind.Array)
        {
            foreach (var childPlan in childPlans.EnumerateArray())
            {
                WalkPostgreSqlPlan(childPlan, result, ref nodeCount);
            }
        }
    }

    private static void AnalyzePostgreSqlTextFallback(ParsedSqlResult parsedSql, string rawPlan, ExecutionPlanResult result)
    {
        if (rawPlan.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(new ExecutionPlanIssue
            {
                Type = "FullTableScan",
                TableName = parsedSql.Tables.FirstOrDefault()?.TableName,
                ImpactScore = 80,
                Description = "执行计划文本中检测到 Seq Scan。",
                Evidence = rawPlan
            });
        }
    }

    private static JsonElement? TryResolvePostgreSqlPlanElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return null;
        }

        var firstRow = root[0];
        if (firstRow.ValueKind == JsonValueKind.Object &&
            firstRow.TryGetProperty("QUERY PLAN", out var queryPlan))
        {
            if (queryPlan.ValueKind == JsonValueKind.Array && queryPlan.GetArrayLength() > 0)
            {
                var firstPlan = queryPlan[0];
                if (firstPlan.ValueKind == JsonValueKind.Object &&
                    firstPlan.TryGetProperty("Plan", out var plan))
                {
                    return plan;
                }
            }

            if (queryPlan.ValueKind == JsonValueKind.String &&
                TryParseJsonDocument(queryPlan.GetString() ?? string.Empty, out var nestedDocument))
            {
                using (nestedDocument)
                {
                    var nestedRoot = nestedDocument.RootElement.Clone();
                    return TryResolvePostgreSqlPlanElement(nestedRoot);
                }
            }
        }

        return null;
    }

    private static bool TryParseJsonArray(string rawText, out List<Dictionary<string, JsonElement>> rows)
    {
        rows = new List<Dictionary<string, JsonElement>>();
        if (!TryParseJsonDocument(rawText, out var document))
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    row[property.Name] = property.Value.Clone();
                }

                rows.Add(row);
            }
        }

        return rows.Count > 0;
    }

    private static bool TryParseJsonDocument(string rawText, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(rawText);
            return true;
        }
        catch
        {
            document = null!;
            return false;
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> row, string key)
    {
        return row.TryGetValue(key, out var value) ? GetString(value) : null;
    }

    private static long GetLong(IReadOnlyDictionary<string, JsonElement> row, string key)
    {
        return row.TryGetValue(key, out var value) ? GetLong(value) : 0;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? GetString(property) : null;
    }

    private static string? GetString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) ? GetLong(property) : 0;
    }

    private static long GetLong(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
        {
            return value;
        }

        return long.TryParse(GetString(element), out var parsed) ? parsed : 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        return double.TryParse(GetString(property), out var parsed) ? parsed : 0;
    }
}
