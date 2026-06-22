using System.Reflection;
using Serilog;
using Swarm.Node.BackgroundServices;
using Swarm.Node.Configuration;
using Swarm.Node.Data;
using Swarm.Node.Handlers;
using Swarm.Node.Logging;
using Swarm.Sdk.Abstractions;
using Swarm.Sdk.DependencyInjection;
using Swarm.Node.Services;
using Grpc.Net.Client;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var dynamicRabbitSink = new DynamicRabbitMqSink();

Log.Logger = SerilogConfiguration.Configure(configuration)
    .WriteTo.Sink(dynamicRabbitSink)
    .CreateBootstrapLogger();

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Configure options
        services.Configure<DataConfiguration>(configuration.GetSection("Database"));

        // Replace default logging with Serilog
        services.AddSingleton(dynamicRabbitSink);
        services.AddSerilog(Log.Logger);

        // Configure gRPC channel
        services.AddSingleton(s => {
            var clusterUrl = configuration["ClusterUrl"]
                ?? throw new InvalidOperationException("ClusterUrl is not configured");

            var channelOptions = new GrpcChannelOptions();

            if (clusterUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            }
            else
            {
                var httpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                channelOptions.HttpHandler = httpHandler;
            }

            return GrpcChannel.ForAddress(clusterUrl, channelOptions);
        });

        // Tag state (D6) — static layer is filled below; overlay layer is
        // refreshed by HeartBeatService on every heartbeat response.
        var tagState = new NodeTagState();
        tagState.SetStatic(TagDiscovery.Discover(configuration));
        services.AddSingleton(tagState);

        // Built-in task handlers (P1-5).
        services.AddTaskHandler<DefaultPassthroughHandler>();
        services.AddTaskHandler<HttpHandlerV1>();
        services.AddTaskHandler<SqlHandlerV1>();
        services.AddTaskHandler<WebhookHandlerV1>();

        // Add services
        services.AddSingleton<BackgroundMaestro>();
        services.AddSingleton<AppDbConnection>();
        services.AddSingleton<EnvSecretsStore>();
        services.AddSingleton<PlaintextConfigStore>();
        services.AddSingleton<NodeMetricsCollector>();
        services.AddSingleton<RegistrationService>();
        services.AddSingleton<HeartBeatService>();
        services.AddSingleton<HandlerRegistry>();
        services.AddSingleton<TaskExecutorService>();

        // Add hosted services
        services.AddHostedService<StartupService>();
        services.AddHostedService<NodeWorker>();
        services.AddHostedService<PluginWatcherService>();
    });

var host = builder.Build();

await host.RunAsync();

Log.CloseAndFlush();
