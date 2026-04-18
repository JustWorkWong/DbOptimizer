using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;
using DbOptimizer.Infrastructure.Persistence;

namespace DbOptimizer.BackendE2ETests;

/// <summary>
/// 测试基类，提供 Testcontainers 和 WebApplicationFactory
/// </summary>
public abstract class E2ETestBase : IAsyncLifetime
{
    protected PostgreSqlContainer PostgresContainer { get; private set; } = null!;
    protected RedisContainer RedisContainer { get; private set; } = null!;
    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // 启动 PostgreSQL 容器
        PostgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("dboptimizer_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await PostgresContainer.StartAsync();

        // 启动 Redis 容器
        RedisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await RedisContainer.StartAsync();

        // 创建 WebApplicationFactory
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // 替换数据库连接字符串
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<DbOptimizerDbContext>));

                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<DbOptimizerDbContext>(options =>
                    {
                        options.UseNpgsql(PostgresContainer.GetConnectionString());
                    });

                    // 替换 Redis 连接
                    var redisDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IConnectionMultiplexer));

                    if (redisDescriptor != null)
                    {
                        services.Remove(redisDescriptor);
                    }

                    services.AddSingleton<IConnectionMultiplexer>(
                        ConnectionMultiplexer.Connect(RedisContainer.GetConnectionString()));
                });
            });

        Client = Factory.CreateClient();

        // 运行数据库迁移
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbOptimizerDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await PostgresContainer.DisposeAsync();
        await RedisContainer.DisposeAsync();
    }
}
