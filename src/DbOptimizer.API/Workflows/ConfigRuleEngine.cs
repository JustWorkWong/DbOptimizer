namespace DbOptimizer.API.Workflows;

/* =========================
 * 配置规则引擎接口
 * ========================= */
internal interface IConfigRuleEngine
{
    IReadOnlyList<ConfigRecommendation> AnalyzeConfig(DbConfigSnapshot snapshot);
}

/* =========================
 * 配置规则接口
 * 每个规则独立判断一个配置参数是否需要优化
 * ========================= */
internal interface IConfigRule
{
    string RuleName { get; }
    string[] ApplicableParameters { get; }
    DatabaseOptimizationEngine[] ApplicableDatabases { get; }

    ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot);
}

/* =========================
 * 规则式配置分析引擎
 * 基于预定义规则库分析数据库配置，生成优化建议
 * ========================= */
internal sealed class ConfigRuleEngine(IEnumerable<IConfigRule> rules) : IConfigRuleEngine
{
    private readonly IReadOnlyList<IConfigRule> _rules = rules.ToList();

    public IReadOnlyList<ConfigRecommendation> AnalyzeConfig(DbConfigSnapshot snapshot)
    {
        var databaseEngine = snapshot.DatabaseType.ToLowerInvariant() switch
        {
            "mysql" => DatabaseOptimizationEngine.MySql,
            "postgresql" => DatabaseOptimizationEngine.PostgreSql,
            _ => DatabaseOptimizationEngine.Unknown
        };

        if (databaseEngine == DatabaseOptimizationEngine.Unknown)
        {
            return Array.Empty<ConfigRecommendation>();
        }

        var recommendations = new List<ConfigRecommendation>();

        foreach (var parameter in snapshot.Parameters)
        {
            foreach (var rule in _rules)
            {
                if (!rule.ApplicableDatabases.Contains(databaseEngine))
                {
                    continue;
                }

                if (!rule.ApplicableParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var recommendation = rule.Evaluate(parameter, snapshot.Metrics, snapshot);
                if (recommendation is not null)
                {
                    recommendations.Add(recommendation);
                }
            }
        }

        return recommendations;
    }
}

/* =========================
 * MySQL InnoDB Buffer Pool 规则
 * 建议设置为物理内存的 70-80%
 * ========================= */
internal sealed class MySqlBufferPoolRule : IConfigRule
{
    public string RuleName => "MySqlBufferPoolSize";
    public string[] ApplicableParameters => ["innodb_buffer_pool_size"];
    public DatabaseOptimizationEngine[] ApplicableDatabases => [DatabaseOptimizationEngine.MySql];

    public ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot)
    {
        if (!long.TryParse(parameter.Value, out var currentBytes))
        {
            return null;
        }

        var totalMemoryBytes = metrics.TotalMemoryBytes;
        if (totalMemoryBytes <= 0)
        {
            return null;
        }

        var recommendedBytes = (long)(totalMemoryBytes * 0.75);
        var currentRatio = (double)currentBytes / totalMemoryBytes;

        if (currentRatio >= 0.70 && currentRatio <= 0.80)
        {
            return null;
        }

        var confidence = currentRatio < 0.50 ? 0.92 : 0.78;
        var impact = currentRatio < 0.50 ? "High" : "Medium";

        var evidenceRefs = new List<string>
        {
            $"当前值: {FormatBytes(currentBytes)} ({currentRatio:P1})",
            $"物理内存: {FormatBytes(totalMemoryBytes)}",
            $"推荐范围: 70-80% 物理内存"
        };

        return new ConfigRecommendation
        {
            ParameterName = parameter.Name,
            CurrentValue = parameter.Value,
            RecommendedValue = recommendedBytes.ToString(),
            Reasoning = $"InnoDB Buffer Pool 是 MySQL 最重要的内存缓存区域，建议设置为物理内存的 70-80%。当前设置为 {currentRatio:P1}，" +
                       (currentRatio < 0.70 ? "过小会导致频繁磁盘 I/O" : "过大可能影响系统稳定性"),
            Confidence = confidence,
            Impact = impact,
            RequiresRestart = true,
            EvidenceRefs = evidenceRefs,
            RuleName = RuleName
        };
    }

    private static string FormatBytes(long bytes)
    {
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        return $"{gb:F2} GB";
    }
}

/* =========================
 * MySQL Max Connections 规则
 * 根据 CPU 核心数和活跃连接数调整
 * ========================= */
internal sealed class MySqlMaxConnectionsRule : IConfigRule
{
    public string RuleName => "MySqlMaxConnections";
    public string[] ApplicableParameters => ["max_connections"];
    public DatabaseOptimizationEngine[] ApplicableDatabases => [DatabaseOptimizationEngine.MySql];

    public ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot)
    {
        if (!int.TryParse(parameter.Value, out var currentMaxConnections))
        {
            return null;
        }

        var cpuCores = metrics.CpuCores;
        var activeConnections = metrics.ActiveConnections;

        if (cpuCores <= 0)
        {
            return null;
        }

        var recommendedMaxConnections = Math.Max(100, cpuCores * 50);
        var utilizationRatio = (double)activeConnections / currentMaxConnections;

        if (currentMaxConnections >= recommendedMaxConnections * 0.8 &&
            currentMaxConnections <= recommendedMaxConnections * 1.2)
        {
            return null;
        }

        var confidence = 0.75;
        var impact = "Medium";

        if (utilizationRatio > 0.8)
        {
            confidence = 0.88;
            impact = "High";
        }

        var evidenceRefs = new List<string>
        {
            $"当前最大连接数: {currentMaxConnections}",
            $"活跃连接数: {activeConnections} ({utilizationRatio:P1})",
            $"CPU 核心数: {cpuCores}",
            $"推荐值: {recommendedMaxConnections} (CPU 核心数 × 50)"
        };

        return new ConfigRecommendation
        {
            ParameterName = parameter.Name,
            CurrentValue = parameter.Value,
            RecommendedValue = recommendedMaxConnections.ToString(),
            Reasoning = $"根据 CPU 核心数 ({cpuCores}) 和当前活跃连接数 ({activeConnections})，建议设置为 {recommendedMaxConnections}。" +
                       (utilizationRatio > 0.8 ? "当前连接数使用率较高，建议增加最大连接数" : "当前设置可能不够优化"),
            Confidence = confidence,
            Impact = impact,
            RequiresRestart = false,
            EvidenceRefs = evidenceRefs,
            RuleName = RuleName
        };
    }
}

/* =========================
 * PostgreSQL Shared Buffers 规则
 * 建议设置为物理内存的 25%
 * ========================= */
internal sealed class PostgreSqlSharedBuffersRule : IConfigRule
{
    public string RuleName => "PostgreSqlSharedBuffers";
    public string[] ApplicableParameters => ["shared_buffers"];
    public DatabaseOptimizationEngine[] ApplicableDatabases => [DatabaseOptimizationEngine.PostgreSql];

    public ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot)
    {
        var currentBytes = ParsePostgreSqlMemoryValue(parameter.Value);
        if (currentBytes <= 0)
        {
            return null;
        }

        var totalMemoryBytes = metrics.TotalMemoryBytes;
        if (totalMemoryBytes <= 0)
        {
            return null;
        }

        var recommendedBytes = (long)(totalMemoryBytes * 0.25);
        var currentRatio = (double)currentBytes / totalMemoryBytes;

        if (currentRatio >= 0.20 && currentRatio <= 0.30)
        {
            return null;
        }

        var confidence = currentRatio < 0.15 ? 0.90 : 0.76;
        var impact = currentRatio < 0.15 ? "High" : "Medium";

        var evidenceRefs = new List<string>
        {
            $"当前值: {FormatBytes(currentBytes)} ({currentRatio:P1})",
            $"物理内存: {FormatBytes(totalMemoryBytes)}",
            $"推荐范围: 20-30% 物理内存"
        };

        return new ConfigRecommendation
        {
            ParameterName = parameter.Name,
            CurrentValue = parameter.Value,
            RecommendedValue = FormatPostgreSqlMemoryValue(recommendedBytes),
            Reasoning = $"Shared Buffers 是 PostgreSQL 的主要缓存区域，建议设置为物理内存的 25%。当前设置为 {currentRatio:P1}，" +
                       (currentRatio < 0.20 ? "过小会导致频繁磁盘 I/O" : "过大可能与操作系统缓存冲突"),
            Confidence = confidence,
            Impact = impact,
            RequiresRestart = true,
            EvidenceRefs = evidenceRefs,
            RuleName = RuleName
        };
    }

    private static long ParsePostgreSqlMemoryValue(string value)
    {
        value = value.Trim();

        if (long.TryParse(value, out var bytes))
        {
            return bytes * 8192;
        }

        var match = System.Text.RegularExpressions.Regex.Match(value, @"^(\d+)\s*(GB|MB|kB)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return 0;
        }

        var number = long.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value.ToUpperInvariant();

        return unit switch
        {
            "GB" => number * 1024 * 1024 * 1024,
            "MB" => number * 1024 * 1024,
            "KB" => number * 1024,
            _ => number * 8192
        };
    }

    private static string FormatPostgreSqlMemoryValue(long bytes)
    {
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        return $"{gb:F0}GB";
    }

    private static string FormatBytes(long bytes)
    {
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        return $"{gb:F2} GB";
    }
}

/* =========================
 * PostgreSQL Work Mem 规则
 * 根据查询复杂度和并发数调整
 * ========================= */
internal sealed class PostgreSqlWorkMemRule : IConfigRule
{
    public string RuleName => "PostgreSqlWorkMem";
    public string[] ApplicableParameters => ["work_mem"];
    public DatabaseOptimizationEngine[] ApplicableDatabases => [DatabaseOptimizationEngine.PostgreSql];

    public ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot)
    {
        var currentBytes = ParsePostgreSqlMemoryValue(parameter.Value);
        if (currentBytes <= 0)
        {
            return null;
        }

        var totalMemoryBytes = metrics.TotalMemoryBytes;
        var maxConnections = metrics.MaxConnections;

        if (totalMemoryBytes <= 0 || maxConnections <= 0)
        {
            return null;
        }

        var recommendedBytes = Math.Min(
            totalMemoryBytes / (maxConnections * 3),
            64 * 1024 * 1024
        );

        var ratio = (double)currentBytes / recommendedBytes;

        if (ratio >= 0.8 && ratio <= 1.2)
        {
            return null;
        }

        var confidence = ratio < 0.5 || ratio > 2.0 ? 0.82 : 0.68;
        var impact = ratio < 0.5 || ratio > 2.0 ? "Medium" : "Low";

        var evidenceRefs = new List<string>
        {
            $"当前值: {FormatBytes(currentBytes)}",
            $"推荐值: {FormatBytes(recommendedBytes)}",
            $"最大连接数: {maxConnections}",
            $"计算公式: min(总内存 / (最大连接数 × 3), 64MB)"
        };

        return new ConfigRecommendation
        {
            ParameterName = parameter.Name,
            CurrentValue = parameter.Value,
            RecommendedValue = FormatPostgreSqlMemoryValue(recommendedBytes),
            Reasoning = $"Work Mem 控制排序和哈希操作的内存使用。当前设置为 {FormatBytes(currentBytes)}，" +
                       (ratio < 0.8 ? "过小可能导致磁盘临时文件" : "过大可能在高并发时耗尽内存") +
                       $"。建议根据最大连接数 ({maxConnections}) 调整为 {FormatBytes(recommendedBytes)}",
            Confidence = confidence,
            Impact = impact,
            RequiresRestart = false,
            EvidenceRefs = evidenceRefs,
            RuleName = RuleName
        };
    }

    private static long ParsePostgreSqlMemoryValue(string value)
    {
        value = value.Trim();

        if (long.TryParse(value, out var bytes))
        {
            return bytes * 1024;
        }

        var match = System.Text.RegularExpressions.Regex.Match(value, @"^(\d+)\s*(GB|MB|kB)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return 0;
        }

        var number = long.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value.ToUpperInvariant();

        return unit switch
        {
            "GB" => number * 1024 * 1024 * 1024,
            "MB" => number * 1024 * 1024,
            "KB" => number * 1024,
            _ => number * 1024
        };
    }

    private static string FormatPostgreSqlMemoryValue(long bytes)
    {
        var mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F0}MB";
    }

    private static string FormatBytes(long bytes)
    {
        var mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F2} MB";
    }
}

/* =========================
 * MySQL Query Cache 规则
 * MySQL 8.0+ 已移除 Query Cache，建议禁用或移除
 * ========================= */
internal sealed class MySqlQueryCacheRule : IConfigRule
{
    public string RuleName => "MySqlQueryCache";
    public string[] ApplicableParameters => ["query_cache_size", "query_cache_type"];
    public DatabaseOptimizationEngine[] ApplicableDatabases => [DatabaseOptimizationEngine.MySql];

    public ConfigRecommendation? Evaluate(ConfigParameter parameter, SystemMetrics metrics, DbConfigSnapshot snapshot)
    {
        var version = snapshot.Metrics.DatabaseVersion;
        var majorVersion = ExtractMajorVersion(version);

        if (majorVersion >= 8)
        {
            if (parameter.Name.Equals("query_cache_type", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Value.Equals("0", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Value.Equals("OFF", StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigRecommendation
                {
                    ParameterName = parameter.Name,
                    CurrentValue = parameter.Value,
                    RecommendedValue = "0",
                    Reasoning = $"MySQL {majorVersion}.x 已移除 Query Cache 功能，建议禁用此参数以避免警告",
                    Confidence = 0.95,
                    Impact = "Low",
                    RequiresRestart = false,
                    EvidenceRefs = [$"MySQL 版本: {version}", "Query Cache 在 MySQL 8.0+ 已废弃"],
                    RuleName = RuleName
                };
            }
        }

        return null;
    }

    private static int ExtractMajorVersion(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version, @"^(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}
