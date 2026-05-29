namespace Swarm.Cluster.Models.Dto;

public record class RequestNodeRegistrationResponse
{
    public required string NodeId { get; set; }
    public required string NodeName { get; set; }
    public required RemoteParametersResponse QueueParameters { get; set; }
}

public record class RemoteParametersResponse
{
    public required string QueueHost { get; set; }
    public required string QueuePort { get; set; }
    public required string QueueUserName { get; set; }
    public required string QueuePassword { get; set; }
}
