namespace IncidentManagement.Api.Domain;

/// <summary>
/// One execution of a workflow. Status running -> success|failed. If IncidentId
/// is set, the rendered step tables surface in that ticket's thread (visible to
/// the reporter, the assigned resolver, and admins) — nobody sees raw JSON.
/// </summary>
public class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public Guid TriggeredById { get; set; }
    public User TriggeredBy { get; set; } = null!;

    /// <summary>Optional link to an incident — when set, rendered output is posted into its thread.</summary>
    public Guid? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public WorkflowRunStatus Status { get; set; } = WorkflowRunStatus.Running;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Human-readable failure reason (e.g. "Step 2 failed with HTTP 500").</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>1-based step order where the run stopped, if it failed.</summary>
    public int? FailedStepOrder { get; set; }

    // Navigation
    public ICollection<WorkflowStepResult> StepResults { get; set; } = new List<WorkflowStepResult>();
}
