namespace Swarm.Node.Models.Dto;

public record class RegistrationRequestResponse
{
    public string NodeId { get; set; }
    public string NodeName { get; set; }
    public RemoteParametersResponse QueueParameters { get; set; }
}

public record class RemoteParametersResponse
{
    public string QueueHost { get; set; }
    public string QueuePort { get; set; }
    public string QueueUserName { get; set; }
    public string QueuePassword { get; set; }
}
