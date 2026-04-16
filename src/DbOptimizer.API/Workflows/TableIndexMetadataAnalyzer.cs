using System.Text.Json;
using System.Text.RegularExpressions;

namespace DbOptimizer.API.Workflows;

internal interface ITableIndexMetadataAnalyzer
{
    TableIndexMetadata Analyze(string tableName, IndexMetadataInvocationResult invocationResult);
}

internal sealed class TableIndexMetadataAnalyzer : ITableIndexMetadataAnalyzer
{
    public TableIndexMetadata Analyze(string tableName, IndexMetadataInvocationResult invocationResult)
    {
        var metadata = new TableIndexMetadata
        {
            TableName = tableName,
            RawText = invocationResult.RawText,
            UsedFallback = invocationResult.UsedFallback
        };

        if (string.IsNullOrWhiteSpace(invocationResult.RawText))
        {
            metadata.Warnings.Add($"表 {tableName} 的索引元数据为空。");
            return metadata;
        }

        if (!TryParseJsonDocument(invocationResult.RawText, out var document))
        {
            metadata.Warnings.Add($"表 {tableName} 的索引元数据不是标准 JSON，当前仅保留原始文本。");
            return metadata;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                metadata.Warnings.Add($"表 {tableName} 的索引元数据不是 JSON 数组。");
                return metadata;
            }

            if (document.RootElement.GetArrayLength() == 0)
            {
                metadata.Warnings.Add($"表 {tableName} 当前未识别到已有索引。");
                return metadata;
            }

            if (TryAnalyzeMySqlRows(tableName, document.RootElement, metadata))
            {
                return metadata;
            }

            if (TryAnalyzePostgreSqlRows(tableName, document.RootElement, metadata))
            {
                return metadata;
            }
        }

        metadata.Warnings.Add($"表 {tableName} 的索引元数据格式无法识别，当前仅保留原始文本。");
        return metadata;
    }

    private static bool TryAnalyzeMySqlRows(string tableName, JsonElement rows, TableIndexMetadata metadata)
    {
        var grouped = new Dictionary<string, ExistingIndexDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("INDEX_NAME", out var indexNameElement))
            {
                continue;
            }

            var indexName = indexNameElement.GetString() ?? string.Empty;
            if (!grouped.TryGetValue(indexName, out var definition))
            {
                definition = new ExistingIndexDefinition
                {
                    IndexName = indexName,
                    TableName = tableName,
                    IsUnique = GetInt(row, "NON_UNIQUE") == 0
                };
                grouped[indexName] = definition;
            }

            var columnName = GetString(row, "COLUMN_NAME");
            if (!string.IsNullOrWhiteSpace(columnName))
            {
                definition.Columns.Add(columnName);
            }

            definition.RawDefinition = row.GetRawText();
        }

        if (grouped.Count == 0)
        {
            return false;
        }

        metadata.ExistingIndexes = grouped.Values.ToList();
        return true;
    }

    private static bool TryAnalyzePostgreSqlRows(string tableName, JsonElement rows, TableIndexMetadata metadata)
    {
        var results = new List<ExistingIndexDefinition>();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("indexname", out var indexNameElement) ||
                !row.TryGetProperty("indexdef", out var indexDefElement))
            {
                continue;
            }

            var indexDef = indexDefElement.GetString() ?? string.Empty;
            results.Add(new ExistingIndexDefinition
            {
                IndexName = indexNameElement.GetString() ?? string.Empty,
                TableName = tableName,
                Columns = ExtractColumnsFromIndexDef(indexDef),
                IsUnique = indexDef.Contains("UNIQUE INDEX", StringComparison.OrdinalIgnoreCase),
                RawDefinition = indexDef
            });
        }

        if (results.Count == 0)
        {
            return false;
        }

        metadata.ExistingIndexes = results;
        return true;
    }

    private static List<string> ExtractColumnsFromIndexDef(string indexDef)
    {
        var match = Regex.Match(indexDef, @"\((?<columns>.+)\)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return new List<string>();
        }

        return match.Groups["columns"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(column => column.Trim('"', '`', ' '))
            .ToList();
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

    private static string? GetString(JsonElement row, string propertyName)
    {
        return row.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
    }

    private static int GetInt(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return int.TryParse(property.ToString(), out var parsed) ? parsed : 0;
    }
}
