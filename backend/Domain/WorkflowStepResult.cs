namespace IncidentManagement.Api.Domain;

/// <summary>
/// Audit record of one step within a run. RequestPayload is the fully
/// placeholder-resolved request; ResponsePayload is the raw response body.
/// IMPORTANT: ResponsePayload is stored for audit only and is NEVER returned to
/// any UI — every consumer goes through WorkflowOutputRenderer (see
/// Services/WorkflowOutputRenderer.cs) to get a flattened table instead.
/// </summary>
public class WorkflowStepResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public WorkflowRun Run { get; set; } = null!;
    public Guid StepId { get; set; }

    /// <summary>Snapshot of the step at run time (steps may be edited later).</summary>
    public string StepName { get; set; } = "";
    public int StepOrder { get; set; }

    /// <summary>Resolved request, JSON: { method, url, headers, body } — for audit.</summary>
    public string RequestPayload { get; set; } = "";

    /// <summary>Raw response body string. Audit-only; never exposed to the UI raw.</summary>
    public string ResponsePayload { get; set; } = "";

    public int? StatusCode { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
