namespace IncidentManagement.Api.Domain;

public class WorkflowIncidentAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AttachedById { get; set; }
    public User AttachedBy { get; set; } = null!;
    public bool VisibleInComments { get; set; } = true;
    public DateTime AttachedAt { get; set; } = DateTime.UtcNow;
}
