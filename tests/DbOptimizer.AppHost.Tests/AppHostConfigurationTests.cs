using Microsoft.Extensions.Configuration;
using Xunit;

namespace DbOptimizer.AppHost.Tests;

public sealed class AppHostConfigurationTests : IDisposable
{
    private readonly string _originalApiPort = Environment.GetEnvironmentVariable("DbOptimizer__Endpoints__ApiPort") ?? string.Empty;
    private readonly bool _hadOriginalApiPort = Environment.GetEnvironmentVariable("DbOptimizer__Endpoints__ApiPort") is not null;

    [Fact]
    public void AddConfigurationSources_LoadsJsonFilesAndHonorsEnvironmentOverrides()
    {
        var tempDirectory = CreateTempDirectory();

        File.WriteAllText(
            Path.Combine(tempDirectory, "appsettings.json"),
            """
            {
              "DbOptimizer": {
                "Endpoints": {
                  "ApiPort": 15069,
                  "WebPort": 10817
                }
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(tempDirectory, "appsettings.Development.json"),
            """
            {
              "DbOptimizer": {
                "Endpoints": {
                  "ApiPort": 16000
                }
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(tempDirectory, "appsettings.Local.json"),
            """
            {
              "DbOptimizer": {
                "Databases": {
                  "PostgreSql": {
                    "Password": "postgres"
                  }
                }
              }
            }
            """);

        Environment.SetEnvironmentVariable("DbOptimizer__Endpoints__ApiPort", "17000");

        var configuration = new ConfigurationManager();

        AppHostConfiguration.AddConfigurationSources(configuration, tempDirectory, "Development");

        Assert.Equal("17000", configuration["DbOptimizer:Endpoints:ApiPort"]);
        Assert.Equal("10817", configuration["DbOptimizer:Endpoints:WebPort"]);
        Assert.Equal("postgres", configuration["DbOptimizer:Databases:PostgreSql:Password"]);
    }

    [Fact]
    public void GetRequiredPort_ThrowsWhenValueIsMissing()
    {
        var configuration = new ConfigurationManager();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppHostConfiguration.GetRequiredPort(configuration, "DbOptimizer:Endpoints:ApiPort"));

        Assert.Equal("Missing required configuration value: DbOptimizer:Endpoints:ApiPort", exception.Message);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            "DbOptimizer__Endpoints__ApiPort",
            _hadOriginalApiPort ? _originalApiPort : null);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dboptimizer-apphost-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
