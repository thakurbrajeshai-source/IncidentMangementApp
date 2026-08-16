namespace IncidentManagement.Api.Domain;

/// <summary>
/// A run-time field the runner fills in when starting a run. Referenced in
/// step templates as <c>{{input.fieldName}}</c>.
/// </summary>
public class WorkflowInput
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    /// <summary>Placeholder key (no spaces; matches the {{input.X}} syntax).</summary>
    public string FieldName { get; set; } = "";

    /// <summary>Human label shown in the run dialog.</summary>
    public string Label { get; set; } = "";

    /// <summary>text | number | date — drives the input control in the run dialog.</summary>
    public string Type { get; set; } = "text";

    public bool Required { get; set; }
}
