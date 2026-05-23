using Serilog;
using Swarm.Node.Data;
using Swarm.Node.Logging;
using Swarm.Node.Services;
using Grpc.Net.Client;
using Swarm.Node.BackgroundServices;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

Log.Logger = SerilogConfiguration.Configure(configuration).CreateBootstrapLogger();

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

            var isDevelopment = configuration["ASPNETCORE_ENVIRONMENT"] == "Development";
            var channelOptions = new GrpcChannelOptions();

            if (isDevelopment)
            {
                var httpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                channelOptions.HttpHandler = httpHandler;
            }

            return GrpcChannel.ForAddress(clusterUrl, channelOptions);
        });
                
        // Add services
        services.AddSingleton<BackgroundMaestro>();
        services.AddSingleton<AppDbConnection>();        
        services.AddScoped<RegistrationService>();
        services.AddScoped<HeartBeatService>();
        
        // Add hosted services
        services.AddHostedService<StartupService>();
        services.AddHostedService<NodeWorker>();
    });

var host = builder.Build();

await host.RunAsync();

Log.CloseAndFlush();