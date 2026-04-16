using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DbOptimizer.PerformanceTests;

/// <summary>
/// 测试专用的 WebApplicationFactory - 使用内存数据库
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // 添加测试配置 - 匹配 Program.cs 的配置路径
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DbOptimizer:ConnectionStrings:PostgreSql"] = "Host=localhost;Port=5432;Database=test;Username=test;Password=test",
                ["DbOptimizer:ConnectionStrings:MySql"] = "Server=localhost;Port=3306;Database=test;Uid=test;Pwd=test",
                ["ConnectionStrings:dboptimizer-postgres"] = "Host=localhost;Port=5432;Database=test;Username=test;Password=test",
                ["ConnectionStrings:PostgreSql"] = "Host=localhost;Port=5432;Database=test;Username=test;Password=test",
                ["AI:Provider"] = "Mock",
                ["AI:ApiKey"] = "test-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            // 可以在这里替换服务为 Mock 实现
        });
    }
}
