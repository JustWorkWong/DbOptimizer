using System.Text.RegularExpressions;

namespace DbOptimizer.Core.Models;

public interface ISqlParser
{
    ParsedSqlResult Parse(string sqlText, string? dialect = null);
}

/* =========================
 * 轻量 SQL 解析器
 * 设计取向：
 * 1) 优先支持最常见的 SELECT / JOIN / WHERE 场景
 * 2) 对 CTE、子查询、窗口函数、多语句等复杂特性采用“部分解析 + warning”
 * 3) 输出稳定中间表示，供后续 ExecutionPlan / IndexAdvisor 继续消费
 * ========================= */
public sealed class LightweightSqlParser : ISqlParser
{
    private static readonly Regex QualifiedColumnRegex = new(
        @"(?<table>[A-Za-z_][\w$]*)\s*\.\s*(?<column>[A-Za-z_][\w$]*|\*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SimpleIdentifierRegex = new(
        @"^(?<column>[A-Za-z_][\w$]*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TableReferenceRegex = new(
        @"^(?<table>(?:[`""\[]?[A-Za-z_][\w$]*[`""\]]?\.)?[`""\[]?[A-Za-z_][\w$]*[`""\]]?)(?:\s+(?:AS\s+)?(?<alias>[`""\[]?[A-Za-z_][\w$]*[`""\]]?))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OutputAliasRegex = new(
        @"^(?<expr>.+?)\s+(?:AS\s+)?(?<alias>[A-Za-z_][\w$]*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex OrderByDirectionRegex = new(
        @"^(?<expr>.+?)\s+(?<direction>ASC|DESC)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] JoinKeywords =
    {
        "LEFT OUTER JOIN",
        "RIGHT OUTER JOIN",
        "FULL OUTER JOIN",
        "LEFT JOIN",
        "RIGHT JOIN",
        "FULL JOIN",
        "INNER JOIN",
        "CROSS JOIN",
        "JOIN"
    };

    private static readonly string[] PredicateOperators =
    {
        " IS NOT NULL",
        " IS NULL",
        " NOT IN ",
        " BETWEEN ",
        " ILIKE ",
        " LIKE ",
        " IN ",
        ">=",
        "<=",
        "<>",
        "!=",
        "=",
        ">",
        "<"
    };

    public ParsedSqlResult Parse(string sqlText, string? dialect = null)
    {
        var rawSql = sqlText?.Trim() ?? string.Empty;
        var result = new ParsedSqlResult
        {
            Dialect = string.IsNullOrWhiteSpace(dialect) ? "Unknown" : dialect.Trim(),
            RawSql = rawSql
        };

        if (string.IsNullOrWhiteSpace(rawSql))
        {
            result.IsPartial = true;
            result.Confidence = 0;
            result.Warnings.Add("SQL 文本为空，无法执行解析。");
            return result;
        }

        var withoutComments = StripComments(rawSql);
        var statements = SplitStatements(withoutComments);
        var sqlToParse = statements[0];

        if (statements.Count > 1)
        {
            result.IsPartial = true;
            result.FeatureFlags.HasMultiStatement = true;
            result.UnsupportedFeatures.Add("MultiStatement");
            result.Warnings.Add("检测到多语句 SQL，当前仅解析第一条语句。");
        }

        result.NormalizedSql = NormalizeWhitespace(sqlToParse);
        result.FeatureFlags = DetectFeatureFlags(result.NormalizedSql, result.FeatureFlags.HasMultiStatement);
        result.QueryType = ResolveQueryType(result.NormalizedSql);

        if (result.FeatureFlags.HasCte)
        {
            result.IsPartial = true;
            result.UnsupportedFeatures.Add("Cte");
            result.Warnings.Add("检测到 CTE，当前仅做部分解析。");
        }

        if (result.FeatureFlags.HasSubquery)
        {
            result.IsPartial = true;
            result.UnsupportedFeatures.Add("Subquery");
            result.Warnings.Add("检测到子查询或派生表，当前仅做部分解析。");
        }

        if (result.FeatureFlags.HasWindowFunction)
        {
            result.IsPartial = true;
            result.UnsupportedFeatures.Add("WindowFunction");
            result.Warnings.Add("检测到窗口函数，当前仅保留原始表达式。");
        }

        if (!string.Equals(result.QueryType, "Select", StringComparison.OrdinalIgnoreCase))
        {
            result.IsPartial = true;
            result.Warnings.Add($"当前仅完整支持 SELECT 语句，检测到类型为 {result.QueryType}。");
            result.Confidence = CalculateConfidence(result);
            return result;
        }

        ParseSelectStatement(result);
        result.Confidence = CalculateConfidence(result);
        return result;
    }

    private static void ParseSelectStatement(ParsedSqlResult result)
    {
        var sql = result.NormalizedSql;
        var selectIndex = FindTopLevelKeyword(sql, "SELECT");
        var fromIndex = FindTopLevelKeyword(sql, "FROM", selectIndex + "SELECT".Length);

        if (selectIndex < 0 || fromIndex < 0)
        {
            result.IsPartial = true;
            result.Warnings.Add("未找到完整的 SELECT/FROM 结构，无法提取表与字段。");
            return;
        }

        var whereIndex = FindTopLevelKeyword(sql, "WHERE", fromIndex + "FROM".Length);
        var groupByIndex = FindTopLevelKeyword(sql, "GROUP BY", fromIndex + "FROM".Length);
        var havingIndex = FindTopLevelKeyword(sql, "HAVING", fromIndex + "FROM".Length);
        var orderByIndex = FindTopLevelKeyword(sql, "ORDER BY", fromIndex + "FROM".Length);
        var limitIndex = FindTopLevelKeyword(sql, "LIMIT", fromIndex + "FROM".Length);
        var offsetIndex = FindTopLevelKeyword(sql, "OFFSET", fromIndex + "FROM".Length);

        var selectClause = sql[(selectIndex + "SELECT".Length)..fromIndex].Trim();
        var fromClauseEnd = FindNextIndex(sql.Length, whereIndex, groupByIndex, havingIndex, orderByIndex, limitIndex, offsetIndex);
        var fromClause = sql[(fromIndex + "FROM".Length)..fromClauseEnd].Trim();

        ParseSelectColumns(selectClause, result);
        ParseFromClause(fromClause, result);
        ParseWhereClause(ExtractClause(sql, whereIndex, "WHERE", groupByIndex, havingIndex, orderByIndex, limitIndex, offsetIndex), result);
        ParseExpressionList(ExtractClause(sql, groupByIndex, "GROUP BY", havingIndex, orderByIndex, limitIndex, offsetIndex), result.GroupBy, result, "GroupBy");
        ParseOrderByClause(ExtractClause(sql, orderByIndex, "ORDER BY", limitIndex, offsetIndex), result);
    }

    private static void ParseSelectColumns(string selectClause, ParsedSqlResult result)
    {
        foreach (var item in SplitTopLevel(selectClause, ','))
        {
            var expression = item.Trim();
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            var outputAlias = TryExtractOutputAlias(expression, out var expressionWithoutAlias);
            var columns = ExtractColumnReferences(expressionWithoutAlias, "Select", outputAlias);

            if (columns.Count > 0)
            {
                result.Columns.AddRange(columns);
                continue;
            }

            result.OpaqueExpressions.Add(expression);
            result.Warnings.Add($"SELECT 表达式未完全解析，已保留原始片段：{expression}");
        }
    }

    private static void ParseFromClause(string fromClause, ParsedSqlResult result)
    {
        if (string.IsNullOrWhiteSpace(fromClause))
        {
            result.IsPartial = true;
            result.Warnings.Add("FROM 片段为空，无法提取表信息。");
            return;
        }

        var firstJoin = FindNextJoin(fromClause, 0);
        var baseSegment = firstJoin.Index >= 0 ? fromClause[..firstJoin.Index].Trim() : fromClause;
        var baseTables = SplitTopLevel(baseSegment, ',');

        if (baseTables.Count > 1)
        {
            result.IsPartial = true;
            result.UnsupportedFeatures.Add("ImplicitJoin");
            result.Warnings.Add("检测到逗号分隔的隐式 JOIN，当前按多表 FROM 做部分解析。");
        }

        foreach (var baseTable in baseTables)
        {
            var table = ParseTableReference(baseTable, "Base", result);
            if (table is not null)
            {
                result.Tables.Add(table);
            }
        }

        if (firstJoin.Index < 0)
        {
            return;
        }

        foreach (var join in ExtractJoinSegments(fromClause[firstJoin.Index..], result))
        {
            var table = ParseTableReference(join.TableSegment, "Join", result);
            if (table is not null)
            {
                result.Tables.Add(table);
            }

            result.Joins.Add(new ParsedJoinClause
            {
                JoinType = join.JoinType,
                TableName = table?.TableName ?? string.Empty,
                Schema = table?.Schema,
                Alias = table?.Alias,
                Condition = join.Condition,
                ConditionColumns = ExtractColumnReferences(join.Condition, "Join", null),
                IsPartial = join.IsPartial || table is null,
                Confidence = table is null ? 0.55 : 0.85
            });
        }
    }

    private static void ParseWhereClause(string whereClause, ParsedSqlResult result)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
        {
            return;
        }

        foreach (var segment in SplitConditions(whereClause))
        {
            result.WhereConditions.Add(ParsePredicate(segment.Expression, segment.LogicalOperator, result));
        }
    }

    private static void ParseExpressionList(
        string clause,
        List<ParsedExpressionReference> target,
        ParsedSqlResult result,
        string source)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return;
        }

        foreach (var item in SplitTopLevel(clause, ','))
        {
            var expression = item.Trim();
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            var columns = ExtractColumnReferences(expression, source, null);
            if (columns.Count == 0)
            {
                result.OpaqueExpressions.Add(expression);
                continue;
            }

            foreach (var column in columns)
            {
                target.Add(new ParsedExpressionReference
                {
                    Expression = expression,
                    TableAlias = column.TableAlias,
                    ColumnName = column.ColumnName,
                    Confidence = column.Confidence
                });
            }
        }
    }

    private static void ParseOrderByClause(string clause, ParsedSqlResult result)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return;
        }

        foreach (var item in SplitTopLevel(clause, ','))
        {
            var expression = item.Trim();
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            var direction = "ASC";
            var match = OrderByDirectionRegex.Match(expression);
            if (match.Success)
            {
                direction = match.Groups["direction"].Value.ToUpperInvariant();
                expression = match.Groups["expr"].Value.Trim();
            }

            var columns = ExtractColumnReferences(expression, "OrderBy", null);
            if (columns.Count == 0)
            {
                result.OpaqueExpressions.Add(item.Trim());
                continue;
            }

            foreach (var column in columns)
            {
                result.OrderBy.Add(new ParsedSortExpression
                {
                    Expression = expression,
                    TableAlias = column.TableAlias,
                    ColumnName = column.ColumnName,
                    Direction = direction,
                    Confidence = column.Confidence
                });
            }
        }
    }

    private static ParsedWherePredicate ParsePredicate(string expression, string? logicalOperator, ParsedSqlResult result)
    {
        var trimmed = expression.Trim();
        var predicate = new ParsedWherePredicate
        {
            Expression = trimmed,
            LogicalOperator = logicalOperator
        };

        if (!TrySplitPredicate(trimmed, out var left, out var @operator, out var right))
        {
            result.IsPartial = true;
            result.OpaqueExpressions.Add(trimmed);
            result.Warnings.Add($"WHERE 条件未完全解析，已保留原始片段：{trimmed}");
            predicate.LeftExpression = trimmed;
            predicate.Operator = "Unknown";
            predicate.Confidence = 0.45;
            return predicate;
        }

        var columns = ExtractColumnReferences(left, "Where", null);
        var primaryColumn = columns.FirstOrDefault();

        predicate.LeftExpression = left;
        predicate.Operator = @operator;
        predicate.RightExpression = right;
        predicate.TableAlias = primaryColumn?.TableAlias;
        predicate.ColumnName = primaryColumn?.ColumnName;
        predicate.Confidence = primaryColumn is null ? 0.55 : 0.9;
        return predicate;
    }

    private static ParsedTableReference? ParseTableReference(string segment, string role, ParsedSqlResult result)
    {
        var value = segment.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith('('))
        {
            result.IsPartial = true;
            result.UnsupportedFeatures.Add("DerivedTable");
            result.OpaqueExpressions.Add(value);
            result.Warnings.Add($"检测到派生表或子查询，当前仅保留原始片段：{value}");
            return null;
        }

        var match = TableReferenceRegex.Match(value);
        if (!match.Success)
        {
            result.IsPartial = true;
            result.UnresolvedReferences.Add(value);
            result.Warnings.Add($"无法识别表引用：{value}");
            return null;
        }

        var identifier = SplitIdentifierPath(match.Groups["table"].Value);
        var alias = NormalizeIdentifier(match.Groups["alias"].Value);

        return new ParsedTableReference
        {
            TableName = identifier.TableName,
            Schema = identifier.Schema,
            Alias = string.IsNullOrWhiteSpace(alias) ? null : alias,
            Role = role,
            SourceFragment = value,
            Confidence = 0.95
        };
    }

    private static List<JoinSegment> ExtractJoinSegments(string joinSql, ParsedSqlResult result)
    {
        var segments = new List<JoinSegment>();
        var remaining = joinSql.Trim();

        while (!string.IsNullOrWhiteSpace(remaining))
        {
            var join = FindNextJoin(remaining, 0);
            if (join.Index < 0)
            {
                break;
            }

            var afterJoin = remaining[(join.Index + join.Keyword.Length)..].TrimStart();
            var onIndex = FindTopLevelKeyword(afterJoin, "ON");
            var usingIndex = FindTopLevelKeyword(afterJoin, "USING");

            if (usingIndex >= 0 && (onIndex < 0 || usingIndex < onIndex))
            {
                var nextJoinIndex = FindNextJoin(afterJoin, usingIndex + "USING".Length).Index;
                var endIndex = nextJoinIndex >= 0 ? nextJoinIndex : afterJoin.Length;

                result.IsPartial = true;
                result.UnsupportedFeatures.Add("JoinUsing");
                result.Warnings.Add($"检测到 USING JOIN 条件，当前仅保留原始片段：{afterJoin[usingIndex..endIndex].Trim()}");

                segments.Add(new JoinSegment(
                    join.Keyword,
                    afterJoin[..usingIndex].Trim(),
                    afterJoin[usingIndex..endIndex].Trim(),
                    true));

                remaining = nextJoinIndex >= 0 ? afterJoin[endIndex..].TrimStart() : string.Empty;
                continue;
            }

            if (onIndex < 0)
            {
                result.IsPartial = true;
                result.Warnings.Add($"JOIN 条件缺少 ON 子句，当前仅保留原始片段：{remaining}");
                segments.Add(new JoinSegment(join.Keyword, afterJoin, string.Empty, true));
                break;
            }

            var nextJoin = FindNextJoin(afterJoin, onIndex + "ON".Length);
            var end = nextJoin.Index >= 0 ? nextJoin.Index : afterJoin.Length;

            segments.Add(new JoinSegment(
                join.Keyword,
                afterJoin[..onIndex].Trim(),
                afterJoin[(onIndex + "ON".Length)..end].Trim(),
                false));

            remaining = nextJoin.Index >= 0 ? afterJoin[end..].TrimStart() : string.Empty;
        }

        return segments;
    }

    private static List<ParsedColumnReference> ExtractColumnReferences(string expression, string source, string? outputAlias)
    {
        var result = new List<ParsedColumnReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in QualifiedColumnRegex.Matches(expression))
        {
            var key = $"{match.Groups["table"].Value}.{match.Groups["column"].Value}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(new ParsedColumnReference
            {
                ColumnName = NormalizeIdentifier(match.Groups["column"].Value),
                TableAlias = NormalizeIdentifier(match.Groups["table"].Value),
                Source = source,
                Expression = expression,
                OutputAlias = string.IsNullOrWhiteSpace(outputAlias) ? null : outputAlias,
                Confidence = 0.95
            });
        }

        if (result.Count > 0)
        {
            return result;
        }

        var simpleMatch = SimpleIdentifierRegex.Match(expression.Trim());
        if (simpleMatch.Success)
        {
            result.Add(new ParsedColumnReference
            {
                ColumnName = NormalizeIdentifier(simpleMatch.Groups["column"].Value),
                Source = source,
                Expression = expression,
                OutputAlias = string.IsNullOrWhiteSpace(outputAlias) ? null : outputAlias,
                Confidence = 0.72
            });
        }

        return result;
    }

    private static bool TrySplitPredicate(string expression, out string left, out string @operator, out string? right)
    {
        foreach (var candidate in PredicateOperators)
        {
            var index = FindTopLevelKeyword(expression, candidate.Trim());
            if (index < 0)
            {
                index = FindLiteralAtTopLevel(expression, candidate);
            }

            if (index < 0)
            {
                continue;
            }

            left = expression[..index].Trim();
            @operator = candidate.Trim().ToUpperInvariant();
            right = index + candidate.Length <= expression.Length
                ? expression[(index + candidate.Length)..].Trim()
                : null;

            if (@operator is "IS NULL" or "IS NOT NULL")
            {
                right = null;
            }

            return !string.IsNullOrWhiteSpace(left);
        }

        left = string.Empty;
        @operator = string.Empty;
        right = null;
        return false;
    }

    private static int FindLiteralAtTopLevel(string value, string candidate)
    {
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index <= value.Length - candidate.Length; index++)
        {
            ToggleQuoteState(value, index, ref inSingleQuote, ref inDoubleQuote);
            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (value[index] == '(')
            {
                depth++;
                continue;
            }

            if (value[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 &&
                value.AsSpan(index, candidate.Length).Equals(candidate.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static List<ConditionSegment> SplitConditions(string clause)
    {
        var result = new List<ConditionSegment>();
        var start = 0;
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        string? logicalOperator = null;

        for (var index = 0; index < clause.Length; index++)
        {
            ToggleQuoteState(clause, index, ref inSingleQuote, ref inDoubleQuote);
            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (clause[index] == '(')
            {
                depth++;
                continue;
            }

            if (clause[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (IsKeywordAt(clause, index, "AND"))
            {
                result.Add(new ConditionSegment(clause[start..index].Trim(), logicalOperator));
                start = index + "AND".Length;
                logicalOperator = "AND";
                continue;
            }

            if (IsKeywordAt(clause, index, "OR"))
            {
                result.Add(new ConditionSegment(clause[start..index].Trim(), logicalOperator));
                start = index + "OR".Length;
                logicalOperator = "OR";
            }
        }

        var tail = clause[start..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            result.Add(new ConditionSegment(tail, logicalOperator));
        }

        return result;
    }

    private static List<string> SplitTopLevel(string value, char delimiter)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < value.Length; index++)
        {
            ToggleQuoteState(value, index, ref inSingleQuote, ref inDoubleQuote);
            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (value[index] == '(')
            {
                depth++;
                continue;
            }

            if (value[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && value[index] == delimiter)
            {
                result.Add(value[start..index]);
                start = index + 1;
            }
        }

        result.Add(value[start..]);
        return result;
    }

    private static int FindTopLevelKeyword(string value, string keyword, int startIndex = 0)
    {
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = startIndex; index <= value.Length - keyword.Length; index++)
        {
            ToggleQuoteState(value, index, ref inSingleQuote, ref inDoubleQuote);
            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (value[index] == '(')
            {
                depth++;
                continue;
            }

            if (value[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && IsKeywordAt(value, index, keyword))
            {
                return index;
            }
        }

        return -1;
    }

    private static JoinMatch FindNextJoin(string value, int startIndex)
    {
        var matches = JoinKeywords
            .Select(keyword => new JoinMatch(FindTopLevelKeyword(value, keyword, startIndex), keyword))
            .Where(match => match.Index >= 0)
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Keyword.Length)
            .ToList();

        return matches.Count > 0 ? matches[0] : new JoinMatch(-1, string.Empty);
    }

    private static ParsedSqlFeatureFlags DetectFeatureFlags(string sql, bool hasMultiStatement)
    {
        return new ParsedSqlFeatureFlags
        {
            HasCte = sql.StartsWith("WITH ", StringComparison.OrdinalIgnoreCase),
            HasSubquery = Regex.IsMatch(sql, @"\(\s*SELECT\b", RegexOptions.IgnoreCase),
            HasGroupBy = FindTopLevelKeyword(sql, "GROUP BY") >= 0,
            HasHaving = FindTopLevelKeyword(sql, "HAVING") >= 0,
            HasOrderBy = FindTopLevelKeyword(sql, "ORDER BY") >= 0,
            HasDistinct = Regex.IsMatch(sql, @"\bSELECT\s+DISTINCT\b", RegexOptions.IgnoreCase),
            HasWindowFunction = Regex.IsMatch(sql, @"\bOVER\s*\(", RegexOptions.IgnoreCase),
            HasMultiStatement = hasMultiStatement
        };
    }

    private static string ResolveQueryType(string sql)
    {
        foreach (var type in new[] { "SELECT", "UPDATE", "DELETE", "INSERT", "MERGE" })
        {
            if (FindTopLevelKeyword(sql, type) >= 0)
            {
                return char.ToUpperInvariant(type[0]) + type[1..].ToLowerInvariant();
            }
        }

        return "Unknown";
    }

    private static string ExtractClause(string sql, int startIndex, string keyword, params int[] endCandidates)
    {
        if (startIndex < 0)
        {
            return string.Empty;
        }

        var endIndex = FindNextIndex(sql.Length, endCandidates);
        return sql[(startIndex + keyword.Length)..endIndex].Trim();
    }

    private static int FindNextIndex(int defaultIndex, params int[] candidates)
    {
        return candidates.Where(value => value >= 0).DefaultIfEmpty(defaultIndex).Min();
    }

    private static string StripComments(string value)
    {
        var noBlockComments = Regex.Replace(value, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(noBlockComments, @"--.*?(?=\r?\n|$)", " ");
    }

    private static List<string> SplitStatements(string value)
    {
        var statements = new List<string>();
        var start = 0;
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < value.Length; index++)
        {
            ToggleQuoteState(value, index, ref inSingleQuote, ref inDoubleQuote);
            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (value[index] == '(')
            {
                depth++;
                continue;
            }

            if (value[index] == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth == 0 && value[index] == ';')
            {
                var statement = value[start..index].Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    statements.Add(statement);
                }

                start = index + 1;
            }
        }

        var tail = value[start..].Trim();
        if (!string.IsNullOrWhiteSpace(tail))
        {
            statements.Add(tail);
        }

        return statements.Count > 0 ? statements : new List<string> { value.Trim() };
    }

    private static string NormalizeWhitespace(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static void ToggleQuoteState(string value, int index, ref bool inSingleQuote, ref bool inDoubleQuote)
    {
        if (value[index] == '\'' && !inDoubleQuote && (index == 0 || value[index - 1] != '\\'))
        {
            inSingleQuote = !inSingleQuote;
        }
        else if (value[index] == '"' && !inSingleQuote && (index == 0 || value[index - 1] != '\\'))
        {
            inDoubleQuote = !inDoubleQuote;
        }
    }

    private static bool IsKeywordAt(string value, int index, string keyword)
    {
        if (index < 0 || index + keyword.Length > value.Length)
        {
            return false;
        }

        if (!value.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var beforeValid = index == 0 || !IsIdentifierChar(value[index - 1]);
        var afterIndex = index + keyword.Length;
        var afterValid = afterIndex >= value.Length || !IsIdentifierChar(value[afterIndex]);
        return beforeValid && afterValid;
    }

    private static bool IsIdentifierChar(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '$';
    }

    private static string? TryExtractOutputAlias(string expression, out string expressionWithoutAlias)
    {
        var match = OutputAliasRegex.Match(expression);
        if (match.Success)
        {
            expressionWithoutAlias = match.Groups["expr"].Value.Trim();
            return NormalizeIdentifier(match.Groups["alias"].Value);
        }

        expressionWithoutAlias = expression;
        return null;
    }

    private static (string? Schema, string TableName) SplitIdentifierPath(string value)
    {
        var parts = value
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeIdentifier)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length switch
        {
            >= 2 => (parts[^2], parts[^1]),
            1 => (null, parts[0]),
            _ => (null, value.Trim())
        };
    }

    private static string NormalizeIdentifier(string value)
    {
        return value.Trim().Trim('`', '"', '[', ']');
    }

    private static double CalculateConfidence(ParsedSqlResult result)
    {
        var confidence = 1d;
        confidence -= Math.Min(0.45, result.Warnings.Count * 0.05);
        confidence -= Math.Min(0.25, result.UnsupportedFeatures.Count * 0.07);

        if (result.IsPartial)
        {
            confidence -= 0.1;
        }

        if (result.Tables.Count == 0)
        {
            confidence -= 0.2;
        }

        return Math.Max(0, Math.Round(confidence, 2));
    }

    private sealed record ConditionSegment(string Expression, string? LogicalOperator);

    private sealed record JoinSegment(string JoinType, string TableSegment, string Condition, bool IsPartial);

    private sealed record JoinMatch(int Index, string Keyword);
}
