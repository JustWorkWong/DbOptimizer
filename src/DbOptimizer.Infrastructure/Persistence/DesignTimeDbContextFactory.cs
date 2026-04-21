using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DbOptimizer.Infrastructure.Persistence;

/* =========================
 * 设计时 DbContext 工厂
 * 设计目标：
 * 1) 为 dotnet ef 命令提供稳定上下文构建入口
 * 2) 避免依赖 Aspire 运行时注入连接串
 * 3) 仅用于迁移生成与工具链，不参与业务运行
 * ========================= */
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DbOptimizerDbContext>
{
    public DbOptimizerDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var connectionString = ResolveDesignTimeConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<DbOptimizerDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DbOptimizerDbContext(optionsBuilder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var currentDirectory = Directory.GetCurrentDirectory();
        var apiProjectDirectory = ResolveApiProjectDirectory(currentDirectory);

        return new ConfigurationBuilder()
            .SetBasePath(currentDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddJsonFile(Path.Combine(apiProjectDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(apiProjectDirectory, $"appsettings.{environmentName}.json"), optional: true)
            .AddJsonFile(Path.Combine(apiProjectDirectory, "appsettings.Local.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string ResolveApiProjectDirectory(string currentDirectory)
    {
        var directory = new DirectoryInfo(currentDirectory);

        while (directory is not null)
        {
            var srcCandidate = Path.Combine(directory.FullName, "src", "DbOptimizer.API");
            if (Directory.Exists(srcCandidate))
            {
                return srcCandidate;
            }

            var siblingCandidate = Path.Combine(directory.FullName, "DbOptimizer.API");
            if (Directory.Exists(siblingCandidate))
            {
                return siblingCandidate;
            }

            directory = directory.Parent;
        }

        return currentDirectory;
    }

    private static string ResolveDesignTimeConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("PostgreSqlDesignTime")
            ?? configuration.GetConnectionString("dboptimizer-postgres")
            ?? configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException(
                "Missing design-time PostgreSQL connection string. Configure ConnectionStrings:PostgreSqlDesignTime or environment variable ConnectionStrings__PostgreSqlDesignTime.");
    }
}
