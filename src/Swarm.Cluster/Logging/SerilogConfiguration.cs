using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Swarm.Cluster.Logging;

public static class SerilogConfiguration
{
    public static Logger CreateLogger(IConfiguration configuration)
    {
        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Swarm.Cluster")
            .CreateLogger();
    }
}
