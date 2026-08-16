using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IncidentManagement.Api.Controllers;

/// <summary>
/// Workflow builder + runner (Admin/Resolver only, per PRD section 6a).
/// Reporters never call these endpoints — they only see rendered workflow output
/// inline in their ticket thread via /api/incidents/{id}/workflow-outputs.
/// </summary>
[ApiController]
[Route("api/workflows")]
[Authorize(Roles = "Resolver,Admin")]
public class WorkflowsController : ControllerBase
{
    private readonly WorkflowService _svc;
    private readonly WorkflowExecutionService _exec;
    public WorkflowsController(WorkflowService svc, WorkflowExecutionService exec) { _svc = svc; _exec = exec; }

    private Guid Uid => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // ----- Definition CRUD --------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var w = await _svc.GetAsync(id, ct);
        return w is null ? NotFound() : Ok(w);
    }

    [HttpPost]
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
    public async Task<IActionResult> Update(Guid id, [FromBody] WorkflowService.SaveRequest req, CancellationToken ct)
    {
        try { await _svc.UpdateAsync(id, req, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ----- Runs -------------------------------------------------------------

    public record RunRequest(Guid? IncidentId, Dictionary<string, string>? Inputs);

    [HttpPost("{id:guid}/run")]
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
    public async Task<IActionResult> Runs(CancellationToken ct) => Ok(await _exec.ListRunsAsync(ct));

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> RunDetail(Guid runId, CancellationToken ct)
    {
        var d = await _exec.GetRunDetailAsync(runId, ct);
        return d is null ? NotFound() : Ok(d);
    }
}
