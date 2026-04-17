using DbOptimizer.Core.Models;
namespace DbOptimizer.Infrastructure.Workflows;

/* =========================
 * SQL 解析结果 DTO
 * 目标：
 * 1) 结构稳定，可直接写入 WorkflowContext 并持久化到 Checkpoint
 * 2) 尽量给后续 ExecutionPlan / IndexAdvisor 预留扩展位
 * 3) 对复杂 SQL 允许部分解析，靠 Warning/UnsupportedFeatures 表达不确定性
 * ========================= */
internal sealed class ParsedSqlResult
{
    public int SchemaVersion { get; set; } = 1;

    public string ParseStrategy { get; set; } = "lightweight-regex-v1";

    public string Dialect { get; set; } = "Unknown";

    public string QueryType { get; set; } = "Unknown";

    public bool IsPartial { get; set; }

    public double Confidence { get; set; }

    public string RawSql { get; set; } = string.Empty;

    public string NormalizedSql { get; set; } = string.Empty;

    public ParsedSqlFeatureFlags FeatureFlags { get; set; } = new();

    public List<ParsedTableReference> Tables { get; set; } = new();

    public List<ParsedColumnReference> Columns { get; set; } = new();

    public List<ParsedJoinClause> Joins { get; set; } = new();

    public List<ParsedWherePredicate> WhereConditions { get; set; } = new();

    public List<ParsedExpressionReference> GroupBy { get; set; } = new();

    public List<ParsedSortExpression> OrderBy { get; set; } = new();

    public List<string> OpaqueExpressions { get; set; } = new();

    public List<string> UnresolvedReferences { get; set; } = new();

    public List<string> UnsupportedFeatures { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

internal sealed class ParsedSqlFeatureFlags
{
    public bool HasCte { get; set; }

    public bool HasSubquery { get; set; }

    public bool HasGroupBy { get; set; }

    public bool HasHaving { get; set; }

    public bool HasOrderBy { get; set; }

    public bool HasDistinct { get; set; }

    public bool HasWindowFunction { get; set; }

    public bool HasMultiStatement { get; set; }
}

internal sealed class ParsedTableReference
{
    public string TableName { get; set; } = string.Empty;

    public string? Schema { get; set; }

    public string? Alias { get; set; }

    public string Role { get; set; } = "Base";

    public string SourceFragment { get; set; } = string.Empty;

    public double Confidence { get; set; } = 1;
}

internal sealed class ParsedColumnReference
{
    public string ColumnName { get; set; } = string.Empty;

    public string? TableAlias { get; set; }

    public string Source { get; set; } = "Select";

    public string Expression { get; set; } = string.Empty;

    public string? OutputAlias { get; set; }

    public double Confidence { get; set; } = 1;
}

internal sealed class ParsedJoinClause
{
    public string JoinType { get; set; } = "INNER";

    public string TableName { get; set; } = string.Empty;

    public string? Schema { get; set; }

    public string? Alias { get; set; }

    public string Condition { get; set; } = string.Empty;

    public List<ParsedColumnReference> ConditionColumns { get; set; } = new();

    public bool IsPartial { get; set; }

    public double Confidence { get; set; } = 1;
}

internal sealed class ParsedWherePredicate
{
    public string Expression { get; set; } = string.Empty;

    public string? LogicalOperator { get; set; }

    public string LeftExpression { get; set; } = string.Empty;

    public string Operator { get; set; } = string.Empty;

    public string? RightExpression { get; set; }

    public string? TableAlias { get; set; }

    public string? ColumnName { get; set; }

    public double Confidence { get; set; } = 1;
}

internal class ParsedExpressionReference
{
    public string Expression { get; set; } = string.Empty;

    public string? TableAlias { get; set; }

    public string? ColumnName { get; set; }

    public double Confidence { get; set; } = 1;
}

internal sealed class ParsedSortExpression : ParsedExpressionReference
{
    public string Direction { get; set; } = "ASC";
}

internal sealed class SqlParserInput
{
    public string SqlText { get; set; } = string.Empty;

    public string? DatabaseDialect { get; set; }
}
