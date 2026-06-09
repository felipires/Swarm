using System.Collections.Concurrent;
using Serilog.Sinks.RabbitMQ;
using Serilog.Events;
using Serilog;
using Serilog.Core;

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
            .Enrich.With<SecretRedactionEnricher>()
            .Enrich.WithProperty("Application", "Swarm.Node");
    }
}

public class DynamicRabbitMqSink : ILogEventSink
{
    private readonly object _lock = new();
    private Logger? _rabbitLogger;
    private bool _configured;
    private readonly ConcurrentQueue<LogEvent> _buffer = new();

    public void Configure(IConfiguration configuration)
    {
        lock (_lock)
        {
            if (_configured)
                return;

            var rabbitMqConfig = configuration.GetSection("RabbitMQ");
            var nodeId = configuration["NodeId"] ?? "unknown";

            var hostname = rabbitMqConfig["Hostname"] ?? "localhost";
            var port = ushort.Parse(rabbitMqConfig["Port"] ?? "5672");
            var username = rabbitMqConfig["Username"] ?? "guest";
            var password = rabbitMqConfig["Password"] ?? "guest";

            _rabbitLogger = new LoggerConfiguration()
                .Enrich.WithProperty("NodeId", nodeId)
                .WriteTo.RabbitMQ((clientCfg, sinkCfg) =>
                {
                    clientCfg.Username = username;
                    clientCfg.Password = password;
                    clientCfg.Hostnames = [hostname];
                    clientCfg.Port = port;
                    clientCfg.Exchange = "";
                    clientCfg.RoutingKey = "logs";
                    clientCfg.DeliveryMode = RabbitMQDeliveryMode.Durable;
                })
                .CreateLogger();

            _configured = true;

            FlushBuffer();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        if (_rabbitLogger is not null)
            _rabbitLogger.Write(logEvent);
        else
            _buffer.Enqueue(logEvent);
    }

    private void FlushBuffer()
    {
        if (_rabbitLogger is null)
            return;

        while (_buffer.TryDequeue(out var evt))
        {
            _rabbitLogger.Write(evt);
        }
    }

    public void Dispose()
    {
        _rabbitLogger?.Dispose();
    }
}
