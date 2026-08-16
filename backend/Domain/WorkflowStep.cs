namespace IncidentManagement.Api.Domain;

/// <summary>
/// One API call in a workflow. Url/headers/body may contain placeholders
/// (<c>{{input.fieldName}}</c>, <c>{{stepN.response.fieldPath}}</c>) resolved
/// server-side at run time. Headers/Body are JSON blobs; AuthConfig is stored
/// encrypted at rest (see Infrastructure/AuthConfigProtector).
/// </summary>
public class WorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    /// <summary>1-based execution order. Steps run sequentially.</summary>
    public int StepOrder { get; set; }

    public string Name { get; set; } = "";
    public string HttpMethod { get; set; } = "GET";

    /// <summary>URL template, e.g. "https://api.example.com/leads/{{input.leadId}}".</summary>
    public string UrlTemplate { get; set; } = "";

    /// <summary>JSON object (string->string) of extra headers. Placeholder-resolved at run time.</summary>
    public string HeadersJson { get; set; } = "{}";

    /// <summary>Raw JSON body template (may contain placeholders). Empty for GET/DELETE.</summary>
    public string BodyTemplate { get; set; } = "";

    public WorkflowAuthType AuthType { get; set; } = WorkflowAuthType.None;

    /// <summary>Encrypted JSON payload (token / basic creds / api-key header), see AuthConfigProtector.</summary>
    public string AuthConfigEncrypted { get; set; } = "";
}
