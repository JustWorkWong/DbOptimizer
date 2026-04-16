using Microsoft.Extensions.Configuration;
using System.Globalization;
using Aspire.Hosting.MySql;
using Aspire.Hosting.Postgres;
using Aspire.Hosting.Redis;

var builder = DistributedApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var postgresPort = GetRequiredPort("DbOptimizer:Databases:PostgreSql:Port");
var mySqlPort = GetRequiredPort("DbOptimizer:Databases:MySql:Port");
var redisPort = GetRequiredPort("DbOptimizer:Databases:Redis:Port");
const int pgAdminPort = 15050;
const int phpMyAdminPort = 15051;
const int redisInsightPort = 15540;

var postgresUser = builder.AddParameterFromConfiguration(
    "postgres-username",
    "DbOptimizer:Databases:PostgreSql:Username");
var postgresPassword = builder.AddParameterFromConfiguration(
    "postgres-password",
    "DbOptimizer:Databases:PostgreSql:Password",
    secret: true);
var mySqlPassword = builder.AddParameterFromConfiguration(
    "mysql-password",
    "DbOptimizer:Databases:MySql:Password",
    secret: true);

var postgresInitDirectory = Path.Combine(builder.AppHostDirectory, "DatabaseInit", "postgresql");
var mySqlInitDirectory = Path.Combine(builder.AppHostDirectory, "DatabaseInit", "mysql");

var postgres = builder.AddPostgres("postgres", userName: postgresUser, password: postgresPassword, port: postgresPort)
    .WithDataVolume()
    .WithInitFiles(postgresInitDirectory)
    .WithPgAdmin(pgAdmin =>
    {
        pgAdmin.WithHostPort(pgAdminPort);
    });

var postgresDatabaseName = GetRequiredValue("DbOptimizer:Databases:PostgreSql:Database");
var postgresDb = postgres.AddDatabase("dboptimizer-postgres", postgresDatabaseName);

var mySql = builder.AddMySql("mysql", password: mySqlPassword, port: mySqlPort)
    .WithDataVolume()
    .WithInitFiles(mySqlInitDirectory)
    .WithPhpMyAdmin(phpMyAdmin =>
    {
        phpMyAdmin.WithHostPort(phpMyAdminPort);
    });

var mySqlDatabaseName = GetRequiredValue("DbOptimizer:Databases:MySql:Database");
var mySqlDb = mySql.AddDatabase("dboptimizer-mysql", mySqlDatabaseName);

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithHostPort(redisPort)
    .WithRedisInsight(redisInsight =>
    {
        redisInsight.WithHostPort(redisInsightPort);
    });

var api = builder.AddProject<Projects.DbOptimizer_API>("api")
    .WithReference(postgresDb)
    .WithReference(mySqlDb)
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(postgresDb)
    .WaitFor(mySqlDb)
    .WaitFor(redis);

builder.AddProject<Projects.DbOptimizer_AgentRuntime>("agentruntime")
    .WithReference(postgresDb)
    .WithReference(mySqlDb)
    .WithReference(redis)
    .WaitFor(postgresDb)
    .WaitFor(mySqlDb)
    .WaitFor(redis);

builder.AddViteApp("web", "../DbOptimizer.Web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();

return;

int GetRequiredPort(string key)
{
    var value = GetRequiredValue(key);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort) || parsedPort <= 0)
    {
        throw new InvalidOperationException($"Configuration value '{key}' must be a positive integer.");
    }

    return parsedPort;
}

string GetRequiredValue(string key)
{
    var value = builder.Configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required configuration value: {key}");
    }

    return value;
}
