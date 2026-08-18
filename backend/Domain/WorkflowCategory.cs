namespace IncidentManagement.Api.Domain;

public class WorkflowCategory
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
