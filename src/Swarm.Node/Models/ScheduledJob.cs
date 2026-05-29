namespace Swarm.Node.Models;

public class ScheduledJob
{
    public Guid Id { get; set; }
    public Guid TaskDefinitionId { get; set; }
    public string CronExpression { get; set; } = null!;
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
