using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace DbOptimizer.PerformanceTests;

/// <summary>
/// Workflow 性能基准测试 - 使用 BenchmarkDotNet 进行精确测量
/// 运行方式: dotnet run -c Release --project tests/DbOptimizer.PerformanceTests
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WorkflowPerformanceBenchmark
{
    // 模拟数据
    private List<string> _sqlQueries = null!;
    private Dictionary<string, object> _configData = null!;

    [GlobalSetup]
    public void Setup()
    {
        // 准备测试数据
        _sqlQueries = Enumerable.Range(0, 100)
            .Select(i => $"SELECT * FROM users WHERE id = {i}")
            .ToList();

        _configData = new Dictionary<string, object>
        {
            ["max_connections"] = 100,
            ["buffer_pool_size"] = "1G",
            ["query_cache_size"] = "64M"
        };
    }

    [Benchmark]
    public async Task SqlParsing_100Queries()
    {
        // 模拟 SQL 解析
        foreach (var sql in _sqlQueries)
        {
            await Task.Run(() => ParseSql(sql));
        }
    }

    [Benchmark]
    public async Task ConfigAnalysis_SingleRun()
    {
        // 模拟配置分析
        await Task.Run(() => AnalyzeConfig(_configData));
    }

    [Benchmark]
    public async Task DataNormalization_100Records()
    {
        // 模拟数据清洗
        var records = Enumerable.Range(0, 100)
            .Select(i => (object)new { Id = i, Query = _sqlQueries[i % _sqlQueries.Count] })
            .ToList();

        await Task.Run(() => NormalizeData(records));
    }

    // ========== 模拟方法 ==========

    private string ParseSql(string sql)
    {
        // 简单的 SQL 解析模拟
        var parts = sql.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", parts.Take(3));
    }

    private Dictionary<string, string> AnalyzeConfig(Dictionary<string, object> config)
    {
        // 简单的配置分析模拟
        return config.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToString() ?? string.Empty
        );
    }

    private List<object> NormalizeData(List<object> records)
    {
        // 简单的数据清洗模拟
        return records.Select(r => new { Normalized = r.ToString() }).Cast<object>().ToList();
    }
}

/// <summary>
/// Benchmark 入口点 - 仅在 Release 模式下运行
/// 注意: 此类仅用于手动运行 Benchmark，不会在 dotnet test 中执行
/// </summary>
public class BenchmarkProgram
{
    // 注释掉 Main 方法，避免与测试项目冲突
    // 手动运行 Benchmark 时取消注释
    /*
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<WorkflowPerformanceBenchmark>();
    }
    */
}
