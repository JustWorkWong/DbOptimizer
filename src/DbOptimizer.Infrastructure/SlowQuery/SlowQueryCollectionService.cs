using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DbOptimizer.Core.Models;
using DbOptimizer.Core.Models;

namespace DbOptimizer.Infrastructure.SlowQuery;

/* =========================
 * 慢查询采集后台服务
 * 职责：
 * 1) 定时执行慢查询采集任务（IHostedService）
 * 2) 配置：CollectionIntervalMinutes, EnabledDatabases
 * 3) 调用 SlowQueryCollector + SlowQueryNormalizer
 * 4) 保存到 slow_queries 表（通过 Repository）
 * ========================= */
public sealed class SlowQueryCollectionService(
    ISlowQueryCollector collector,
    ISlowQueryNormalizer normalizer,
    ISlowQueryRepository repository,
    SlowQueryCollectionOptions options,
    ILogger<SlowQueryCollectionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("慢查询采集服务已禁用");
            return;
        }

        logger.LogInformation(
            "慢查询采集服务已启动。Interval={IntervalMinutes}分钟, EnabledDatabases={EnabledDatabases}",
            options.IntervalMinutes,
            string.Join(",", options.EnabledDatabases));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.IntervalMinutes), stoppingToken);
                await CollectAllDatabasesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("慢查询采集服务正在停止");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "慢查询采集任务执行失败");
            }
        }
    }

    private async Task CollectAllDatabasesAsync(CancellationToken cancellationToken)
    {
        foreach (var databaseConfig in options.EnabledDatabases)
        {
            try
            {
                await CollectDatabaseAsync(databaseConfig, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "数据库慢查询采集失败。DatabaseId={DatabaseId}",
                    databaseConfig.DatabaseId);
            }
        }
    }

    private async Task CollectDatabaseAsync(
        DatabaseConfig databaseConfig,
        CancellationToken cancellationToken)
    {
        var databaseType = ParseDatabaseType(databaseConfig.DatabaseType);
        var rawQueries = await collector.CollectAsync(
            databaseConfig.DatabaseId,
            databaseType,
            cancellationToken);

        logger.LogInformation(
            "采集到 {Count} 条慢查询。DatabaseId={DatabaseId}",
            rawQueries.Count,
            databaseConfig.DatabaseId);

        foreach (var raw in rawQueries)
        {
            try
            {
                var normalized = normalizer.Normalize(
                    raw,
                    databaseConfig.DatabaseId,
                    databaseConfig.DatabaseType);

                await repository.SaveAsync(normalized, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "慢查询保存失败。DatabaseId={DatabaseId}, Sql={Sql}",
                    databaseConfig.DatabaseId,
                    raw.SqlText[..Math.Min(100, raw.SqlText.Length)]);
            }
        }
    }

    private static DatabaseOptimizationEngine ParseDatabaseType(string databaseType)
    {
        return databaseType.ToLowerInvariant() switch
        {
            "mysql" => DatabaseOptimizationEngine.MySql,
            "postgresql" => DatabaseOptimizationEngine.PostgreSql,
            _ => throw new NotSupportedException($"不支持的数据库类型: {databaseType}")
        };
    }
}

/* =========================
 * 慢查询采集配置
 * ========================= */
public sealed class SlowQueryCollectionOptions
{
    public const string SectionName = "SlowQueryCollection";

    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 5;
    public int SlowThresholdMs { get; init; } = 1000;
    public int MaxCollectionCount { get; init; } = 100;
    public IReadOnlyList<DatabaseConfig> EnabledDatabases { get; init; } = Array.Empty<DatabaseConfig>();
}

public sealed class DatabaseConfig
{
    public required string DatabaseId { get; init; }
    public required string DatabaseType { get; init; }
}
