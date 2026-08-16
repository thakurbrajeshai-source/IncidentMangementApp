using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Auth;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Sequential execution engine for workflow runs (PRD section 6a).
///
/// - Resolves {{input.X}} / {{stepN.response.path}} placeholders per step, in order.
/// - Stores the resolved request + raw response per step for audit (request/response
///   payloads never leave the backend as-is; consumers get WorkflowOutputRenderer tables).
/// - On a non-2xx or network error: stops the run, marks it Failed, and records which
///   step failed and why.
/// - Runs linked to an incident surface their rendered tables in that ticket's thread.
/// </summary>
public class WorkflowExecutionService
{
    private readonly AppDbContext _db;
    private readonly IAuthConfigProtector _protector;
    private readonly HttpClient _http;
    private readonly NotificationService _notifs;

    public WorkflowExecutionService(
        AppDbContext db, IAuthConfigProtector protector, HttpClient http, NotificationService notifs)
    {
        _db = db;
        _protector = protector;
        _http = http;
        _notifs = notifs;
    }

    public record RunSummaryDto(Guid Id, Guid WorkflowId, string WorkflowName, string Status,
        DateTime StartedAt, DateTime? CompletedAt, Guid? IncidentId, string? IncidentTicketRef,
        string TriggeredByFullName, int? FailedStepOrder, string? ErrorMessage);

    public record RunStepOutputDto(int StepOrder, string StepName, int? StatusCode, bool Succeeded,
        string? ErrorMessage, DateTime ExecutedAt, WorkflowOutputRenderer.RenderedTable Table);

    public record RunDetailDto(Guid Id, Guid WorkflowId, string WorkflowName, string Status,
        DateTime StartedAt, DateTime? CompletedAt, Guid? IncidentId, string? IncidentTicketRef,
        string TriggeredByFullName, int? FailedStepOrder, string? ErrorMessage, List<RunStepOutputDto> Steps);

    public record IncidentRunOutputDto(Guid RunId, string WorkflowName, string Status,
        DateTime StartedAt, string TriggeredByFullName, List<RunStepOutputDto> Steps);

    // ----- Execution --------------------------------------------------------

    public async Task<Guid> RunAsync(Guid workflowId, Guid triggeredBy, Guid? incidentId,
        Dictionary<string, string> inputs, CancellationToken ct = default)
    {
        var workflow = await _db.Workflows
            .Include(w => w.Steps)
            .Include(w => w.Inputs)
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new KeyNotFoundException("Workflow not found.");

        if (!workflow.IsActive) throw new InvalidOperationException("This workflow is inactive.");
        if (workflow.Steps.Count == 0) throw new InvalidOperationException("This workflow has no steps.");

        foreach (var input in workflow.Inputs.Where(i => i.Required))
            if (!inputs.TryGetValue(input.FieldName, out var v) || string.IsNullOrWhiteSpace(v))
                throw new ArgumentException($"Missing required input '{input.Label}'.");

        var run = new WorkflowRun
        {
            WorkflowId = workflow.Id,
            TriggeredById = triggeredBy,
            IncidentId = incidentId,
            Status = WorkflowRunStatus.Running,
            StartedAt = DateTime.UtcNow,
        };
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        try
        {
            var inputNodes = inputs.ToDictionary(
                kv => kv.Key, kv => (JsonNode?)JsonValue.Create(kv.Value), StringComparer.Ordinal);
            var responses = new Dictionary<int, JsonNode?>();

            foreach (var step in workflow.Steps.OrderBy(s => s.StepOrder))
            {
                var result = await ExecuteStepAsync(run.Id, step, inputNodes, responses, ct);
                _db.WorkflowStepResults.Add(result);
                await _db.SaveChangesAsync(ct);

                if (!result.Succeeded)
                {
                    run.Status = WorkflowRunStatus.Failed;
                    run.CompletedAt = DateTime.UtcNow;
                    run.FailedStepOrder = step.StepOrder;
                    run.ErrorMessage = result.ErrorMessage ?? $"Step {step.StepOrder} failed.";
                    await _db.SaveChangesAsync(ct);
                    await NotifyAsync(run, workflow, ct);
                    return run.Id;
                }

                responses[step.StepOrder] = TryParseJson(result.ResponsePayload);
            }

            run.Status = WorkflowRunStatus.Success;
            run.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await NotifyAsync(run, workflow, ct);
            return run.Id;
        }
        catch (Exception ex)
        {
            run.Status = WorkflowRunStatus.Failed;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);
            await NotifyAsync(run, workflow, ct);
            return run.Id;
        }
    }

    private async Task<WorkflowStepResult> ExecuteStepAsync(Guid runId, WorkflowStep step,
        Dictionary<string, JsonNode?> inputs, Dictionary<int, JsonNode?> responses, CancellationToken ct)
    {
        var result = new WorkflowStepResult
        {
            RunId = runId,
            StepId = step.Id,
            StepName = step.Name,
            StepOrder = step.StepOrder,
            ExecutedAt = DateTime.UtcNow,
        };

        try
        {
            var lookup = WorkflowPlaceholderResolver.Lookup(inputs, responses);
            var url = WorkflowPlaceholderResolver.ResolveText(step.UrlTemplate, lookup);
            var headers = ParseObject(step.HeadersJson);
            foreach (var (k, v) in headers) headers[k] = WorkflowPlaceholderResolver.ResolveText(v, lookup);
            var bodyNode = WorkflowPlaceholderResolver.ResolveBody(step.BodyTemplate, lookup);

            var req = new HttpRequestMessage(new HttpMethod(step.HttpMethod.ToUpperInvariant()), new Uri(url, UriKind.Absolute));
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            ApplyAuth(req, step);
            if (bodyNode is not null)
                req.Content = new StringContent(bodyNode.ToJsonString(), Encoding.UTF8, "application/json");

            result.RequestPayload = JsonSerializer.Serialize(new
            {
                method = step.HttpMethod,
                url,
                headers,
                body = bodyNode?.ToJsonString(),
            });

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            var resp = await _http.SendAsync(req, timeoutCts.Token);
            var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

            result.StatusCode = (int)resp.StatusCode;
            result.ResponsePayload = body.Length > 100_000 ? body[..100_000] : body;
            if (resp.IsSuccessStatusCode)
            {
                result.Succeeded = true;
            }
            else
            {
                result.Succeeded = false;
                result.ErrorMessage = $"Step {step.StepOrder} failed with HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}";
            }
        }
        catch (Exception ex)
        {
            result.Succeeded = false;
            result.ErrorMessage = $"Step {step.StepOrder} error: {ex.Message}";
        }

        return result;
    }

    private void ApplyAuth(HttpRequestMessage req, WorkflowStep step)
    {
        if (step.AuthType == WorkflowAuthType.None) return;
        var cfg = ParseObject(_protector.Unprotect(step.AuthConfigEncrypted));
        switch (step.AuthType)
        {
            case WorkflowAuthType.Bearer:
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.GetValueOrDefault("token")}");
                break;
            case WorkflowAuthType.Basic:
                var creds = Encoding.UTF8.GetBytes($"{cfg.GetValueOrDefault("username")}:{cfg.GetValueOrDefault("password")}");
                req.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(creds));
                break;
            case WorkflowAuthType.ApiKey:
                var header = string.IsNullOrWhiteSpace(cfg.GetValueOrDefault("header")) ? "X-API-Key" : cfg["header"];
                req.Headers.TryAddWithoutValidation(header, cfg.GetValueOrDefault("value"));
                break;
        }
    }

    private async Task NotifyAsync(WorkflowRun run, Workflow workflow, CancellationToken ct)
    {
        // The reporter of a linked ticket gets pinged so they know to look at their thread.
        if (run.IncidentId is not Guid incidentId) return;
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident is null) return;

        await _notifs.BroadcastAsync(new[] { incident.ReporterId }, NotificationType.WorkflowRunComplete, incident,
            title: $"Workflow ran on {incident.TicketRef}",
            body: $"\"{workflow.Name}\" {(run.Status == WorkflowRunStatus.Success ? "completed" : "failed")} — see the ticket thread.",
            ct: ct);
    }

    // ----- Reads ------------------------------------------------------------

    public async Task<List<RunSummaryDto>> ListRunsAsync(CancellationToken ct = default)
    {
        var runs = await _db.WorkflowRuns
            .Include(r => r.Workflow)
            .Include(r => r.Incident)
            .Include(r => r.TriggeredBy)
            .OrderByDescending(r => r.StartedAt)
            .Take(200)
            .ToListAsync(ct);
        return runs.Select(ToSummary).ToList();
    }

    public async Task<RunDetailDto?> GetRunDetailAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _db.WorkflowRuns
            .Include(r => r.Workflow)
            .Include(r => r.Incident)
            .Include(r => r.TriggeredBy)
            .Include(r => r.StepResults)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return null;

        var summary = ToSummary(run);
        return new RunDetailDto(
            summary.Id, summary.WorkflowId, summary.WorkflowName, summary.Status,
            summary.StartedAt, summary.CompletedAt, summary.IncidentId, summary.IncidentTicketRef,
            summary.TriggeredByFullName, summary.FailedStepOrder, summary.ErrorMessage,
            run.StepResults.OrderBy(s => s.StepOrder).Select(ToStepOutput).ToList());
    }

    /// <summary>Rendered step tables for every run attached to an incident —
    /// used to render workflow output inline in the ticket thread.</summary>
    public async Task<List<IncidentRunOutputDto>> GetIncidentOutputsAsync(Guid incidentId, CancellationToken ct = default)
    {
        var runs = await _db.WorkflowRuns
            .Where(r => r.IncidentId == incidentId)
            .Include(r => r.Workflow)
            .Include(r => r.TriggeredBy)
            .Include(r => r.StepResults)
            .OrderBy(r => r.StartedAt)
            .ToListAsync(ct);
        return runs.Select(r => new IncidentRunOutputDto(
            r.Id,
            r.Workflow.Name,
            r.Status.ToString(),
            r.StartedAt,
            r.TriggeredBy.FullName,
            r.StepResults.OrderBy(s => s.StepOrder).Select(ToStepOutput).ToList())).ToList();
    }

    private static RunSummaryDto ToSummary(WorkflowRun r) => new(
        r.Id, r.WorkflowId, r.Workflow.Name, r.Status.ToString(),
        r.StartedAt, r.CompletedAt, r.IncidentId, r.Incident?.TicketRef,
        r.TriggeredBy.FullName, r.FailedStepOrder, r.ErrorMessage);

    private static RunStepOutputDto ToStepOutput(WorkflowStepResult s) => new(
        s.StepOrder, s.StepName, s.StatusCode, s.Succeeded, s.ErrorMessage, s.ExecutedAt,
        WorkflowOutputRenderer.Render(s.ResponsePayload));

    private static JsonNode? TryParseJson(string raw)
    {
        try { return JsonNode.Parse(raw); }
        catch (JsonException) { return null; }
    }

    private static Dictionary<string, string> ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
