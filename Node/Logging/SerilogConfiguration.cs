using Serilog;
using Serilog.Core;
using Serilog.Sinks.RabbitMQ;

namespace Swarm.Node.Logging;

public static class SerilogConfiguration
{
    public static LoggerConfiguration Configure(IConfiguration configuration)
    {
        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Swarm.Node");
    }

    public static void AddRabbitMQSink(IConfiguration configuration)
    {
        var rabbitMqConfig = configuration.GetSection("RabbitMQ");
        
        var hostname = rabbitMqConfig["Hostname"] ?? "localhost";
        var port = ushort.Parse(rabbitMqConfig["Port"] ?? "5672");
        var username = rabbitMqConfig["Username"] ?? "guest";
        var password = rabbitMqConfig["Password"] ?? "guest";

        var clientConfig = new RabbitMQClientConfiguration { };
        var sinkConfig = new RabbitMQSinkConfiguration { };

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
