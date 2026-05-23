using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RabbitMQ.Client;
using Serilog;
using Swarm.Cluster.Data;
using Swarm.Cluster.GrpcServices;
using Swarm.Cluster.Logging;
using Swarm.Cluster.Middleware;
using Swarm.Cluster.Services;
  
var builder = WebApplication.CreateBuilder(args);

Log.Logger = SerilogConfiguration.CreateLogger(builder.Configuration);

builder.Host.UseSerilog(Log.Logger);

// Add services
builder.Services.AddDbContext<ClusterDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// builder.Services.AddStackExchangeRedisCache(options =>
// {
//     var redisConfig = builder.Configuration.GetSection("Redis");
//     options.builder.Configuration = redisConfig["Connection"];
// });

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

builder.Services.AddScoped<NodeService>();
builder.Services.AddScoped<TaskDispatchService>();
builder.Services.AddSingleton<LogConsumerService>();
builder.Services.AddHostedService<HeartbeatBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogConsumerService>());
builder.Services.AddHostedService<TaskResultConsumerService>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddGrpc();
builder.Services.AddEndpointsApiExplorer();

builder.WebHost.ConfigureKestrel(options =>
{
    // Port 5000 — pure HTTP/2, gRPC only (no TLS, no upgrade negotiation)
    options.ListenLocalhost(5000, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    // Port 5001 — HTTP/1.1 for REST API and Swagger
    options.ListenLocalhost(5001, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
    // Port 5002 — HTTPS with HTTP/1.1+HTTP/2
    options.ListenLocalhost(5002, o =>
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
// app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();

app.MapGet("/health", () => new { status = "ok" })
    .WithName("Health")
    .WithOpenApi()
    .AllowAnonymous();

Log.Information("Cluster application started successfully");
await app.RunAsync();
