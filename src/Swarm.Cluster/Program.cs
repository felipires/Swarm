using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Npgsql;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;
using Swarm.Cluster.Data;
using Swarm.Cluster.GrpcServices;
using Swarm.Cluster.Logging;
using Swarm.Cluster.Middleware;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Metrics;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = SerilogConfiguration.CreateLogger(builder.Configuration);

builder.Host.UseSerilog(Log.Logger);

// One Npgsql data source shared by EF Core (request-scoped DbContext) and any
// singleton service that needs a raw connection (e.g. LogConsumerService).
// Per Npgsql guidance, a single NpgsqlDataSource per app is correct and
// singleton-safe — it manages its own pool internally.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));

builder.Services.AddDbContext<ClusterDbContext>((sp, options) =>
    options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

// P5-1: Redis — IConnectionMultiplexer for raw list/TTL ops in NodeMetricsStore.
// AbortOnConnectFail=false: multiplexer creation succeeds even when Redis is
// unreachable at startup; operations fail at call-site, where NodeMetricsStore
// already catches and logs the exception (best-effort, never fails a heartbeat).
var redisConfig = ConfigurationOptions.Parse(
    builder.Configuration["Redis:Connection"] ?? "localhost:6379");
redisConfig.AbortOnConnectFail = false;
redisConfig.ConnectTimeout = 1000;   // 1s connect attempt
redisConfig.SyncTimeout = 1000;      // 1s per command — fail fast when Redis is down

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));
builder.Services.AddSingleton<NodeMetricsStore>();

// RabbitMQ shared connection
builder.Services.AddSingleton<IConnection>(_ =>
{
    var cfg = builder.Configuration.GetSection("RabbitMQ");
    return new ConnectionFactory
    {
        HostName = cfg["Hostname"] ?? "localhost",
        Port = cfg.GetValue<int>("Port", 5672),
        UserName = cfg["Username"] ?? "guest",
        Password = cfg["Password"] ?? "guest",
        VirtualHost = cfg["VirtualHost"] ?? "/",
        DispatchConsumersAsync = true
    }.CreateConnection();
});

// P3-3: tag-containment matcher. The app runs on Postgres (UseNpgsql above),
// so the GIN-indexed `@>` implementation is the production binding. The
// in-memory LINQ matcher is the test/non-Postgres path, wired directly by tests.
builder.Services.AddScoped<Swarm.Cluster.Services.Tags.ITagMatchStrategy,
    Swarm.Cluster.Services.Tags.PostgresJsonbTagMatcher>();
builder.Services.AddSingleton<ClusterEnvCrypto>();
builder.Services.AddScoped<NodeService>();
builder.Services.AddScoped<EntityVersionService>();
builder.Services.AddScoped<TaskDispatchService>();
builder.Services.AddScoped<Swarm.Cluster.Validation.DispatchValidator>();

// Pipelines (P1-1): PipelineService + PipelineRunExecutor are scoped (each
// request / each advancement gets its own DbContext). InProcessStepAdvancer
// is the singleton bridge — it owns the Channel and creates a fresh scope
// per work item.
builder.Services.AddScoped<Swarm.Cluster.Services.Pipelines.PipelineService>();
builder.Services.AddScoped<Swarm.Cluster.Services.Pipelines.PipelineRunExecutor>();
builder.Services.AddSingleton<Swarm.Cluster.Services.Pipelines.InProcessStepAdvancer>();
builder.Services.AddSingleton<Swarm.Cluster.Services.Pipelines.IStepAdvancer>(
    sp => sp.GetRequiredService<Swarm.Cluster.Services.Pipelines.InProcessStepAdvancer>());
builder.Services.AddHostedService(
    sp => sp.GetRequiredService<Swarm.Cluster.Services.Pipelines.InProcessStepAdvancer>());

// P1-3 cron-driven scheduler. ScheduleService is the scoped CRUD facade;
// SchedulerService is the singleton sweep loop that fires due schedules
// through PipelineService.StartRunAsync.
builder.Services.AddScoped<Swarm.Cluster.Services.Pipelines.ScheduleService>();
builder.Services.AddHostedService<Swarm.Cluster.Services.Pipelines.SchedulerService>();

builder.Services.AddSingleton<LogConsumerService>();
builder.Services.AddHostedService<HeartbeatBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogConsumerService>());
builder.Services.AddHostedService<TaskResultConsumerService>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<TaskClaimsConsumerService>();
builder.Services.AddHostedService<RetrySchedulerService>();
builder.Services.AddHostedService<LogRetentionService>();
builder.Services.AddHostedService<TaggedRouteRetentionService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();

builder.WebHost.ConfigureKestrel(options =>
{
    // Port 5000 — pure HTTP/2, gRPC only (no TLS, no upgrade negotiation)
    options.ListenAnyIP(5000, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    // Port 5001 — HTTP/1.1 for REST API and Swagger
    options.ListenAnyIP(5001, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
    // Port 5002 — HTTPS with HTTP/1.1+HTTP/2
    options.ListenAnyIP(5002, o =>
    {
        o.UseHttps();
        o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddSwaggerGen(gen =>
{
    gen.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Description = "API Key needed to access the endpoints."
    });
    gen.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            []
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
    });
});

var app = builder.Build();

// P3-2: global ApiError translator. Must come before any endpoint mapping
// so exceptions from controllers/middleware run through it.
app.UseSwarmErrorHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGrpcService<NodesGrpcService>();

// app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => new { status = "ok" })
    .WithName("Health")
    .WithOpenApi()
    .AllowAnonymous();

Log.Information("Cluster application started successfully");
await app.RunAsync();
