var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithHostPort(5432);

var db = postgres.AddDatabase("dboptimizer");

var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithHostPort(6379);

var api = builder.AddProject<Projects.DbOptimizer_API>("api")
    .WithReference(db)
    .WithReference(redis)
    .WithHttpHealthCheck("/health")
    .WaitFor(db)
    .WaitFor(redis);

builder.AddProject<Projects.DbOptimizer_AgentRuntime>("agentruntime")
    .WithReference(db)
    .WithReference(redis)
    .WaitFor(db)
    .WaitFor(redis);

builder.AddViteApp("web", "../DbOptimizer.Web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
