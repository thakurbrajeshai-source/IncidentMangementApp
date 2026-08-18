using System.Text.Json;
using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Auth;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// CRUD for workflow definitions (builder feature — Admin/Resolver only).
/// Steps' auth configs are encrypted at rest via IAuthConfigProtector.
/// </summary>
public class WorkflowService
{
    private readonly AppDbContext _db;
    private readonly IAuthConfigProtector _protector;

    public static readonly string[] AllowedMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

    public WorkflowService(AppDbContext db, IAuthConfigProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public record StepDto(string? Id, int StepOrder, string Name, string HttpMethod, string UrlTemplate,
        Dictionary<string, string>? Headers, string BodyTemplate, string AuthType,
        Dictionary<string, string>? AuthConfig);

    public record InputDto(string? Id, string FieldName, string Label, string Type, bool Required);

    public record SaveRequest(string Name, string Description, bool IsActive,
        List<InputDto>? Inputs, List<StepDto>? Steps);

    public async Task<List<object>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Workflows
            .Include(w => w.CreatedBy)
            .Include(w => w.Categories).ThenInclude(wc => wc.Category)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(w => (object)new
        {
            id = w.Id,
            name = w.Name,
            description = w.Description,
            isActive = w.IsActive,
            createdAt = w.CreatedAt,
            createdById = w.CreatedById,
            createdByFullName = w.CreatedBy.FullName,
            stepCount = _db.WorkflowSteps.Count(s => s.WorkflowId == w.Id),
            inputCount = _db.WorkflowInputs.Count(i => i.WorkflowId == w.Id),
            categories = w.Categories.Select(wc => new { id = wc.CategoryId, name = wc.Category.Name }).ToList(),
        }).ToList();
    }

    public async Task<object?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var w = await _db.Workflows
            .Include(w => w.CreatedBy)
            .Include(w => w.Steps)
            .Include(w => w.Inputs)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return null;

        return new
        {
            id = w.Id,
            name = w.Name,
            description = w.Description,
            isActive = w.IsActive,
            createdAt = w.CreatedAt,
            createdById = w.CreatedById,
            createdByFullName = w.CreatedBy.FullName,
            inputs = w.Inputs.OrderBy(i => i.FieldName).Select(i => new
            {
                id = i.Id, fieldName = i.FieldName, label = i.Label, type = i.Type, required = i.Required,
            }),
            steps = w.Steps.OrderBy(s => s.StepOrder).Select(s => new
            {
                id = s.Id, stepOrder = s.StepOrder, name = s.Name, httpMethod = s.HttpMethod,
                urlTemplate = s.UrlTemplate,
                headers = ParseObject(s.HeadersJson),
                bodyTemplate = s.BodyTemplate,
                authType = s.AuthType.ToString(),
                authConfig = ParseObject(_protector.Unprotect(s.AuthConfigEncrypted)),
            }),
        };
    }

    public async Task<Workflow> CreateAsync(Guid actorId, SaveRequest req, CancellationToken ct = default)
    {
        Validate(req);
        var w = new Workflow
        {
            Name = req.Name.Trim(),
            Description = req.Description?.Trim() ?? "",
            CreatedById = actorId,
            CreatedAt = DateTime.UtcNow,
            IsActive = req.IsActive,
        };
        ApplySteps(w, req);
        ApplyInputs(w, req);
        _db.Workflows.Add(w);
        await _db.SaveChangesAsync(ct);
        return w;
    }

    public async Task UpdateAsync(Guid id, SaveRequest req, CancellationToken ct = default)
    {
        var w = await _db.Workflows
            .Include(x => x.Steps)
            .Include(x => x.Inputs)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Workflow not found.");
        Validate(req);

        // Replace steps/inputs wholesale. Steps/inputs carry a unique (WorkflowId,
        // StepOrder|FieldName) index, so delete existing rows via ExecuteDelete.
        await _db.WorkflowSteps.Where(s => s.WorkflowId == id).ExecuteDeleteAsync(ct);
        await _db.WorkflowInputs.Where(i => i.WorkflowId == id).ExecuteDeleteAsync(ct);

        // ExecuteDelete bypasses the change tracker, so the previously loaded
        // steps/inputs are still tracked as Unchanged. Detach everything and
        // re-load the workflow so the replacement steps are seen as Added
        // (otherwise EF tries to UPDATE the new rows and hits a 0-affected-row
        // concurrency error).
        _db.ChangeTracker.Clear();
        w = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Workflow not found.");

        w.Name = req.Name.Trim();
        w.Description = req.Description?.Trim() ?? "";
        w.IsActive = req.IsActive;

        ApplySteps(w, req);
        ApplyInputs(w, req);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var w = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Workflow not found.");
        // Runs keep their step-result audit trail (WorkflowRuns -> Workflow is Restrict,
        // so block deleting a workflow that has runs rather than losing history).
        if (await _db.WorkflowRuns.AnyAsync(r => r.WorkflowId == id, ct))
            throw new InvalidOperationException(
                "This workflow has run history. Delete its runs first (or deactivate it instead).");
        _db.Workflows.Remove(w);
        await _db.SaveChangesAsync(ct);
    }

    // ----- Helpers ----------------------------------------------------------

    private static void Validate(SaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ArgumentException("Workflow name is required.");
        var steps = req.Steps ?? new List<StepDto>();
        if (steps.Count == 0) throw new ArgumentException("A workflow needs at least one step.");

        foreach (var s in steps)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) throw new ArgumentException("Every step needs a name.");
            var method = s.HttpMethod?.Trim().ToUpperInvariant() ?? "";
            if (!AllowedMethods.Contains(method)) throw new ArgumentException($"Unsupported HTTP method '{s.HttpMethod}'.");
            if (string.IsNullOrWhiteSpace(s.UrlTemplate)) throw new ArgumentException($"Step '{s.Name}' is missing a URL.");
            if (!Uri.TryCreate(s.UrlTemplate, UriKind.Absolute, out _)
                && !s.UrlTemplate.Contains("{{", StringComparison.Ordinal))
                throw new ArgumentException($"Step '{s.Name}' URL must be absolute (or use placeholders).");
            if (!string.IsNullOrWhiteSpace(s.BodyTemplate))
            {
                try { _ = JsonDocument.Parse(s.BodyTemplate); }
                catch (JsonException) { throw new ArgumentException($"Step '{s.Name}' body_template is not valid JSON."); }
            }
            if (s.AuthType != null && !Enum.TryParse<WorkflowAuthType>(s.AuthType, true, out _))
                throw new ArgumentException($"Step '{s.Name}' has an unknown auth type '{s.AuthType}'.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in req.Inputs ?? new List<InputDto>())
        {
            if (string.IsNullOrWhiteSpace(i.FieldName)) throw new ArgumentException("Every input needs a field name.");
            if (!seen.Add(i.FieldName)) throw new ArgumentException($"Duplicate input field '{i.FieldName}'.");
            if (string.IsNullOrWhiteSpace(i.Label)) throw new ArgumentException($"Input '{i.FieldName}' needs a label.");
            if (i.Type is not ("text" or "number" or "date")) throw new ArgumentException($"Input '{i.FieldName}' has unknown type '{i.Type}'.");
        }
    }

    private void ApplySteps(Workflow w, SaveRequest req)
    {
        var steps = req.Steps ?? new List<StepDto>();
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var authType = Enum.TryParse<WorkflowAuthType>(s.AuthType, true, out var at) ? at : WorkflowAuthType.None;
            // Add directly to the DbSet (not w.Steps) so the new entities are
            // always tracked as Added; adding to a re-queried (tracked) parent's
            // collection can leave them tracked as Modified (0-row UPDATE).
            _db.WorkflowSteps.Add(new WorkflowStep
            {
                WorkflowId = w.Id,
                StepOrder = i + 1,
                Name = s.Name.Trim(),
                HttpMethod = s.HttpMethod.Trim().ToUpperInvariant(),
                UrlTemplate = s.UrlTemplate.Trim(),
                HeadersJson = JsonSerializer.Serialize(s.Headers ?? new Dictionary<string, string>()),
                BodyTemplate = s.BodyTemplate ?? "",
                AuthType = authType,
                AuthConfigEncrypted = _protector.Protect(JsonSerializer.Serialize(s.AuthConfig ?? new Dictionary<string, string>())),
            });
        }
    }

    private void ApplyInputs(Workflow w, SaveRequest req)
    {
        foreach (var i in req.Inputs ?? new List<InputDto>())
        {
            _db.WorkflowInputs.Add(new WorkflowInput
            {
                WorkflowId = w.Id,
                FieldName = i.FieldName.Trim(),
                Label = i.Label.Trim(),
                Type = string.IsNullOrWhiteSpace(i.Type) ? "text" : i.Type,
                Required = i.Required,
            });
        }
    }

    private static Dictionary<string, string> ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return dict ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    // ----- Category assignment (default check per PRD 6a) -------------------

    public async Task SetCategoriesAsync(Guid workflowId, List<int> categoryIds, CancellationToken ct = default)
    {
        var w = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == workflowId, ct)
            ?? throw new KeyNotFoundException("Workflow not found.");

        await _db.WorkflowCategories.Where(wc => wc.WorkflowId == workflowId).ExecuteDeleteAsync(ct);

        foreach (var catId in categoryIds.Distinct())
        {
            _db.WorkflowCategories.Add(new WorkflowCategory
            {
                WorkflowId = workflowId,
                CategoryId = catId,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<object?> GetDefaultWorkflowForCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        var wc = await _db.WorkflowCategories
            .Include(wc => wc.Workflow).ThenInclude(w => w.CreatedBy)
            .Include(wc => wc.Workflow).ThenInclude(w => w.Inputs)
            .FirstOrDefaultAsync(wc => wc.CategoryId == categoryId, ct);
        if (wc is null) return null;

        var w = wc.Workflow;
        return new
        {
            id = w.Id,
            name = w.Name,
            description = w.Description,
            isActive = w.IsActive,
            inputs = w.Inputs.OrderBy(i => i.FieldName).Select(i => new
            {
                fieldName = i.FieldName, label = i.Label, type = i.Type, required = i.Required,
            }),
        };
    }

    public async Task<List<object>> GetCategoryOptionsAsync(CancellationToken ct = default)
    {
        var cats = await _db.Categories.OrderBy(c => c.Id).ToListAsync(ct);
        return cats.Select(c => (object)new { id = c.Id, name = c.Name }).ToList();
    }
}
