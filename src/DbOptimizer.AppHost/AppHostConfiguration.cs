using Microsoft.Extensions.Configuration;
using System.Globalization;

public static class AppHostConfiguration
{
    public static void AddConfigurationSources(
        ConfigurationManager configuration,
        string appHostDirectory,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(appHostDirectory))
        {
            throw new ArgumentException("AppHost directory must be provided.", nameof(appHostDirectory));
        }

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            throw new ArgumentException("Environment name must be provided.", nameof(environmentName));
        }

        configuration.SetBasePath(appHostDirectory);
        configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        configuration.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
        configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
        configuration.AddEnvironmentVariables();
    }

    public static int GetRequiredPort(IConfiguration configuration, string key)
    {
        var value = GetRequiredValue(configuration, key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort <= 0)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be a positive integer.");
        }

        return parsedPort;
    }

    public static string GetRequiredValue(IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required configuration value: {key}");
        }

        return value;
    }
}
