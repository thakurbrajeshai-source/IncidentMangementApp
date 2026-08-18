namespace IncidentManagement.Api.Domain;

public class WorkflowRunCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public int Count { get; set; } = 0;
}
