using Aspire.Hosting.MySql;
using Aspire.Hosting.Postgres;
using Aspire.Hosting.Redis;

var builder = DistributedApplication.CreateBuilder(args);

AppHostConfiguration.AddConfigurationSources(
    builder.Configuration,
    builder.AppHostDirectory,
    builder.Environment.EnvironmentName);

var apiPort = AppHostConfiguration.GetRequiredPort(builder.Configuration, "DbOptimizer:Endpoints:ApiPort");
var webPort = AppHostConfiguration.GetRequiredPort(builder.Configuration, "DbOptimizer:Endpoints:WebPort");
var postgresPort = AppHostConfiguration.GetRequiredPort(builder.Configuration, "DbOptimizer:Databases:PostgreSql:Port");
var mySqlPort = AppHostConfiguration.GetRequiredPort(builder.Configuration, "DbOptimizer:Databases:MySql:Port");
var redisPort = AppHostConfiguration.GetRequiredPort(builder.Configuration, "DbOptimizer:Databases:Redis:Port");
const int pgAdminPort = 15050;
const int phpMyAdminPort = 15051;
const int redisInsightPort = 15540;

var postgresUsername = AppHostConfiguration.GetRequiredValue(builder.Configuration, "DbOptimizer:Databases:PostgreSql:Username");
var postgresPasswordValue = AppHostConfiguration.GetRequiredValue(builder.Configuration, "DbOptimizer:Databases:PostgreSql:Password");
var mySqlPasswordValue = AppHostConfiguration.GetRequiredValue(builder.Configuration, "DbOptimizer:Databases:MySql:Password");

var postgresUser = builder.AddParameter("postgres-username", postgresUsername);
var postgresPassword = builder.AddParameter("postgres-password", postgresPasswordValue, secret: true);
var mySqlPassword = builder.AddParameter("mysql-password", mySqlPasswordValue, secret: true);

var postgresInitDirectory = Path.Combine(builder.AppHostDirectory, "DatabaseInit", "postgresql");
var mySqlInitDirectory = Path.Combine(builder.AppHostDirectory, "DatabaseInit", "mysql");

var postgres = builder.AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithDataVolume()
    .WithEnvironment("POSTGRES_USER", postgresUsername)
    .WithEnvironment("POSTGRES_PASSWORD", postgresPassword)
    .WithInitFiles(postgresInitDirectory)
    .WithPgAdmin(pgAdmin =>
    {
        pgAdmin.WithHostPort(pgAdminPort);
    });

var postgresDatabaseName = AppHostConfiguration.GetRequiredValue(builder.Configuration, "DbOptimizer:Databases:PostgreSql:Database");
var postgresDb = postgres.AddDatabase("dboptimizer-postgres", postgresDatabaseName);

var mySql = builder.AddMySql("mysql", password: mySqlPassword)
    .WithDataVolume()
    .WithEnvironment("MYSQL_ROOT_PASSWORD", mySqlPassword)
    .WithInitFiles(mySqlInitDirectory)
    .WithPhpMyAdmin(phpMyAdmin =>
    {
        phpMyAdmin.WithHostPort(phpMyAdminPort);
    });

var mySqlDatabaseName = AppHostConfiguration.GetRequiredValue(builder.Configuration, "DbOptimizer:Databases:MySql:Database");
var mySqlDb = mySql.AddDatabase("dboptimizer-mysql", mySqlDatabaseName);

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithRedisInsight(redisInsight =>
    {
        redisInsight.WithHostPort(redisInsightPort);
    });

var api = builder.AddProject<Projects.DbOptimizer_API>("api", options =>
    {
        options.ExcludeLaunchProfile = true;
    })
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(
        port: apiPort,
        name: "http",
        isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(postgresDb)
    .WaitFor(mySqlDb)
    .WaitFor(redis)
    .WithReference(postgresDb)
    .WithReference(mySqlDb)
    .WithReference(redis);

builder.AddViteApp("web", "../DbOptimizer.Web")
    .WithHttpEndpoint(port: webPort, name: "web-http", isProxied: false)
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("http"));

builder.Build().Run();

return;
