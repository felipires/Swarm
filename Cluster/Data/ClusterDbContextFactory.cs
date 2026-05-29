using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Swarm.Cluster.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling so migration commands
/// don't have to boot the full host (which currently has DI-validation
/// failures that roadmap item P0-1 will resolve). Runtime DI still goes
/// through <c>builder.Services.AddDbContext</c> in <c>Program.cs</c>.
/// </summary>
public class ClusterDbContextFactory : IDesignTimeDbContextFactory<ClusterDbContext>
{
    public ClusterDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=swarm;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ClusterDbContext(options);
    }
}
