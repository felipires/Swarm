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

Log.Logger = SerilogConfiguration.Configure(configuration).CreateBootstrapLogger();

// Replace bootstrap logger with full logger including RabbitMQ sink
SerilogConfiguration.AddRabbitMQSink(configuration);

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Configure options
        services.Configure<DataConfiguration>(configuration.GetSection("Database"));

        // Replace default logging with Serilog
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
        services.AddSingleton<NodeMetricsCollector>();
        services.AddSingleton<RegistrationService>();
        services.AddSingleton<HeartBeatService>();
        services.AddSingleton<TaskExecutorService>();

        // Optional plugin scan: load ITaskHandler implementations from a directory.
        // TODO P4-3: trust model — Assembly.LoadFrom on an unsanitized path is unsafe
        // once Nodes run in shared environments. Revisit when the trust model lands.
        var pluginPath = configuration["Swarm:PluginsPath"];
        Log.Information("PluginsPath: {pluginPath} - {Exists} - {pwd}", pluginPath, Directory.Exists(pluginPath), Directory.GetCurrentDirectory());
        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            var bootstrap = Log.Logger;
            var loaded = 0;
            foreach (var dll in Directory.GetFiles(pluginPath, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetTypes()
                        .Where(t => typeof(ITaskHandler).IsAssignableFrom(t)
                                    && !t.IsAbstract
                                    && t.GetConstructor(Type.EmptyTypes) is not null))
                    {
                        services.AddSingleton(typeof(ITaskHandler), type);
                        loaded++;
                        bootstrap.Information("Loaded plugin handler {Type} from {Dll}", type.FullName, dll);
                    }
                }
                catch (Exception ex)
                {
                    bootstrap.Error(ex, "Failed to load plugin assembly {Dll}", dll);
                }
            }
            bootstrap.Information("Plugin scan complete: {Count} handler(s) loaded from {Path}", loaded, pluginPath);
        }

        // Add hosted services
        services.AddHostedService<StartupService>();
        services.AddHostedService<NodeWorker>();
    });

var host = builder.Build();

await host.RunAsync();

Log.CloseAndFlush();
