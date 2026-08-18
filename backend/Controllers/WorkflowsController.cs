using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IncidentManagement.Api.Controllers;

/// <summary>
/// Workflow builder + runner (Admin/Resolver only, per PRD section 6a).
/// Reporters trigger workflows via /api/incidents/{id}/run-workflow on their own tickets.
/// </summary>
[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowService _svc;
    private readonly WorkflowExecutionService _exec;
    public WorkflowsController(WorkflowService svc, WorkflowExecutionService exec) { _svc = svc; _exec = exec; }

    private Guid Uid => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string Role => User.FindFirst(ClaimTypes.Role)!.Value;

    // ----- Definition CRUD --------------------------------------------------

    [HttpGet]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var w = await _svc.GetAsync(id, ct);
        return w is null ? NotFound() : Ok(w);
    }

    [HttpPost]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Create([FromBody] WorkflowService.SaveRequest req, CancellationToken ct)
    {
        try
        {
            var w = await _svc.CreateAsync(Uid, req, ct);
            return CreatedAtAction(nameof(Get), new { id = w.Id }, new { id = w.Id });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] WorkflowService.SaveRequest req, CancellationToken ct)
    {
        try { await _svc.UpdateAsync(id, req, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ----- Runs -------------------------------------------------------------

    public record RunRequest(Guid? IncidentId, Dictionary<string, string>? Inputs);

    [HttpPost("{id:guid}/run")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Run(Guid id, [FromBody] RunRequest? req, CancellationToken ct)
    {
        try
        {
            var runId = await _exec.RunAsync(id, Uid, req?.IncidentId, req?.Inputs ?? new(), ct);
            return Ok(new { runId, status = "Running" });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("runs")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Runs(CancellationToken ct) => Ok(await _exec.ListRunsAsync(ct));

    [HttpGet("runs/{runId:guid}")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> RunDetail(Guid runId, CancellationToken ct)
    {
        var d = await _exec.GetRunDetailAsync(runId, ct);
        return d is null ? NotFound() : Ok(d);
    }

    // ----- Category assignment (default check per PRD 6a) -------------------

    [HttpPut("{id:guid}/categories")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> SetCategories(Guid id, [FromBody] SetCategoriesRequest req, CancellationToken ct)
    {
        try
        {
            await _svc.SetCategoriesAsync(id, req.CategoryIds, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    public record SetCategoriesRequest(List<int> CategoryIds);

    [HttpGet("available")]
    [Authorize]
    public async Task<IActionResult> AvailableWorkflows(CancellationToken ct)
        => Ok(await _exec.GetAvailableWorkflowsAsync(ct));

    // ----- Attach workflow to incident (Resolver/Admin) ---------------------

    [HttpPost("attach")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> AttachAndRun([FromBody] AttachRequest req, CancellationToken ct)
    {
        try
        {
            var runId = await _exec.AttachAndRunAsync(req.IncidentId, req.WorkflowId, Uid, req.Inputs ?? new(), ct);
            return Ok(new { runId, status = "Running" });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record AttachRequest(Guid IncidentId, Guid WorkflowId, Dictionary<string, string>? Inputs);

    // ----- Visibility toggle (Resolver/Admin show/hide in comments) ----------

    [HttpPut("visibility")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> SetVisibility([FromBody] VisibilityRequest req, CancellationToken ct)
    {
        try
        {
            await _exec.SetWorkflowVisibilityAsync(req.IncidentId, req.WorkflowId, req.Visible, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    public record VisibilityRequest(Guid IncidentId, Guid WorkflowId, bool Visible);
}
