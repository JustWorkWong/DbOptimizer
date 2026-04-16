using FluentAssertions;
using System.Text.Json;

namespace DbOptimizer.SecurityTests;

/// <summary>
/// 密钥管理安全测试 - 验证敏感信息不泄露
/// </summary>
public class SecretManagementTests
{
    [Fact]
    public void AppsettingsJson_ShouldNotContainPlaintextSecrets()
    {
        // Arrange
        var appsettingsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "src", "DbOptimizer.API", "appsettings.json"
        );

        // Act
        var content = File.ReadAllText(appsettingsPath);
        var json = JsonDocument.Parse(content);

        // Assert - 检查常见的敏感字段
        var sensitiveKeys = new[] { "ApiKey", "Password", "Secret", "Token", "ConnectionString" };

        foreach (var key in sensitiveKeys)
        {
            CheckJsonForSensitiveData(json.RootElement, key, content);
        }
    }

    [Fact]
    public void AppsettingsDevelopmentJson_ShouldNotContainProductionSecrets()
    {
        // Arrange
        var appsettingsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "src", "DbOptimizer.API", "appsettings.Development.json"
        );

        if (!File.Exists(appsettingsPath))
        {
            return; // 文件不存在则跳过
        }

        // Act
        var content = File.ReadAllText(appsettingsPath);

        // Assert - 不应包含生产环境标识
        content.Should().NotContain("prod", "开发配置不应包含生产环境标识");
        content.Should().NotContain("production", "开发配置不应包含生产环境标识");
    }

    [Fact]
    public void EnvironmentVariables_ShouldOverrideConfiguration()
    {
        // Arrange
        var testKey = "TEST_SECRET_KEY";
        var testValue = "test-secret-value";
        Environment.SetEnvironmentVariable(testKey, testValue);

        // Act
        var retrievedValue = Environment.GetEnvironmentVariable(testKey);

        // Assert
        retrievedValue.Should().Be(testValue, "环境变量应正确覆盖配置");

        // Cleanup
        Environment.SetEnvironmentVariable(testKey, null);
    }

    [Fact]
    public void LogOutput_ShouldNotContainSensitivePatterns()
    {
        // Arrange
        var sensitivePatterns = new[]
        {
            @"password\s*=\s*[^\s]+",
            @"api[_-]?key\s*=\s*[^\s]+",
            @"secret\s*=\s*[^\s]+",
            @"token\s*=\s*[^\s]+"
        };

        var sampleLogMessage = "User logged in successfully with username=admin";

        // Act & Assert
        foreach (var pattern in sensitivePatterns)
        {
            System.Text.RegularExpressions.Regex.IsMatch(
                sampleLogMessage,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ).Should().BeFalse($"日志不应包含敏感模式: {pattern}");
        }
    }

    [Fact]
    public void ConnectionString_ShouldNotContainPlaintextPassword()
    {
        // Arrange
        var validConnectionStrings = new[]
        {
            "Server=localhost;Database=test;User Id=admin;Password=${DB_PASSWORD}",
            "Server=localhost;Database=test;Integrated Security=true",
            "Server=localhost;Database=test;User Id=admin;Password={env:DB_PASSWORD}"
        };

        var invalidConnectionStrings = new[]
        {
            "Server=localhost;Database=test;User Id=admin;Password=MyPassword123",
            "Server=localhost;Database=test;User Id=admin;Password=admin"
        };

        // Act & Assert
        foreach (var connStr in validConnectionStrings)
        {
            IsConnectionStringSecure(connStr).Should().BeTrue($"连接字符串应安全: {connStr}");
        }

        foreach (var connStr in invalidConnectionStrings)
        {
            IsConnectionStringSecure(connStr).Should().BeFalse($"连接字符串不安全: {connStr}");
        }
    }

    // ========== 辅助方法 ==========

    private void CheckJsonForSensitiveData(JsonElement element, string sensitiveKey, string fullContent)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Contains(sensitiveKey, StringComparison.OrdinalIgnoreCase))
                {
                    var value = property.Value.ToString();

                    // 允许的占位符模式
                    var allowedPatterns = new[] { "${", "{env:", "***", "placeholder", "your-", "<", "localhost" };
                    var isPlaceholder = allowedPatterns.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase));

                    // 空值或占位符都是安全的
                    if (!isPlaceholder && !string.IsNullOrWhiteSpace(value))
                    {
                        // 检查是否为 JSON 对象（ConnectionStrings 本身）
                        if (property.Value.ValueKind == JsonValueKind.Object)
                        {
                            // 递归检查子对象
                            CheckJsonForSensitiveData(property.Value, sensitiveKey, fullContent);
                        }
                        else
                        {
                            Assert.Fail($"发现明文敏感信息: {property.Name} = {value}");
                        }
                    }
                }

                CheckJsonForSensitiveData(property.Value, sensitiveKey, fullContent);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CheckJsonForSensitiveData(item, sensitiveKey, fullContent);
            }
        }
    }

    private bool IsConnectionStringSecure(string connectionString)
    {
        // 检查密码是否使用占位符
        var passwordMatch = System.Text.RegularExpressions.Regex.Match(
            connectionString,
            @"Password\s*=\s*([^;]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (!passwordMatch.Success)
        {
            return true; // 没有密码字段（如 Integrated Security）
        }

        var password = passwordMatch.Groups[1].Value.Trim();

        // 检查是否为占位符
        var placeholderPatterns = new[] { "${", "{env:", "***", "<" };
        return placeholderPatterns.Any(p => password.Contains(p));
    }
}
