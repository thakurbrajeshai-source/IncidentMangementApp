namespace IncidentManagement.Api.Domain;

/// <summary>
/// A named chain of API steps (Admin/Resolver can build and run these, Reporter
/// only ever sees the rendered output when a run is attached to their ticket).
/// </summary>
public class Workflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowInput> Inputs { get; set; } = new List<WorkflowInput>();
    public ICollection<WorkflowRun> Runs { get; set; } = new List<WorkflowRun>();
    public ICollection<WorkflowCategory> Categories { get; set; } = new List<WorkflowCategory>();
}
