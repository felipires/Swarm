using System;

namespace Swarm.Cluster.Models;

public class Log
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string Level { get; set; } = null!;
    public string MessageTemplate { get; set; } = null!;
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; } // JSON serialized properties
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; }
}
