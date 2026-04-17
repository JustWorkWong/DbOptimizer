using DbOptimizer.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.SlowQuery;

/* =========================
 * 慢查询数据清洗器
 * 职责：
 * 1) 提取 SQL 指纹（去除常量、归一化空格）
 * 2) 计算 QueryHash（用于去重）
 * 3) 提取表名、操作类型
 * 4) 复用 LightweightSqlParser 进行解析
 * ========================= */
public sealed class SlowQueryNormalizer(ISqlParser sqlParser) : ISlowQueryNormalizer
{
    private static readonly Regex NumberLiteralRegex = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex StringLiteralRegex = new(@"'[^']*'", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public NormalizedSlowQuery Normalize(RawSlowQuery raw, string databaseId, string databaseType)
    {
        var fingerprint = ExtractFingerprint(raw.SqlText);
        var queryHash = ComputeHash(fingerprint);
        var parsed = sqlParser.Parse(raw.SqlText, databaseType);

        return new NormalizedSlowQuery
        {
            SqlFingerprint = fingerprint,
            QueryHash = queryHash,
            OriginalSql = raw.SqlText.Length > 10000 ? raw.SqlText[..10000] : raw.SqlText,
            ExecutionTime = raw.ExecutionTime,
            ExecutedAt = raw.ExecutedAt,
            DatabaseId = databaseId,
            DatabaseType = databaseType,
            Tables = parsed.Tables.Select(t => t.TableName).ToList(),
            QueryType = parsed.QueryType,
            RowsExamined = raw.RowsExamined ?? 0,
            RowsSent = raw.RowsSent ?? 0
        };
    }

    private static string ExtractFingerprint(string sql)
    {
        var normalized = sql.Trim();
        normalized = StringLiteralRegex.Replace(normalized, "?");
        normalized = NumberLiteralRegex.Replace(normalized, "?");
        normalized = WhitespaceRegex.Replace(normalized, " ");
        return normalized;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public interface ISlowQueryNormalizer
{
    NormalizedSlowQuery Normalize(RawSlowQuery raw, string databaseId, string databaseType);
}
