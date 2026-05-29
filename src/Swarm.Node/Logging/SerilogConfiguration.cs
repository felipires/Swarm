using Serilog;
using Serilog.Sinks.RabbitMQ;

namespace Swarm.Node.Logging;

public static class SerilogConfiguration
{
    public static LoggerConfiguration Configure(IConfiguration configuration)
    {
        var nodeId = configuration["NodeId"] ?? "unknown";

        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.With<SecretRedactionEnricher>() // P4-2a — scrub :secret values
            .Enrich.WithProperty("Application", "Swarm.Node")
            .Enrich.WithProperty("NodeId", nodeId);
    }

    public static void AddRabbitMQSink(IConfiguration configuration)
    {
        var rabbitMqConfig = configuration.GetSection("RabbitMQ");
        
        var hostname = rabbitMqConfig["Hostname"] ?? "localhost";
        var port = ushort.Parse(rabbitMqConfig["Port"] ?? "5672");
        var username = rabbitMqConfig["Username"] ?? "guest";
        var password = rabbitMqConfig["Password"] ?? "guest";

        Log.Logger = Configure(configuration)
            .WriteTo.RabbitMQ((clientCfg, sinkCfg) =>
            {
                // Configure connection details
                clientCfg.Username = username;
                clientCfg.Password = password;
                clientCfg.Hostnames = [hostname];
                clientCfg.Port = port;
                clientCfg.Exchange = "";
                clientCfg.RoutingKey = "logs";
                clientCfg.DeliveryMode = RabbitMQDeliveryMode.Durable;
            })
            .CreateLogger();
    }
}
