using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IncidentManagement.Api.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize]
public class IncidentsController : ControllerBase
{
    private readonly IncidentService _svc;
    private readonly WorkflowExecutionService _workflowExec;
    public IncidentsController(IncidentService svc, WorkflowExecutionService workflowExec)
    {
        _svc = svc;
        _workflowExec = workflowExec;
    }

    private Guid Uid => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string Role => User.FindFirst(ClaimTypes.Role)!.Value;

    // ----- List (role-aware) ------------------------------------------------

    /// <summary>Returns the right list for the caller's role:
    /// Reporter -> their own tickets; Resolver -> unassigned pool + their own;
    /// Admin -> everything. We do server-side filtering so a reporter literally
    /// cannot see another reporter's tickets (defense in depth on top of role checks).</summary>
    [HttpGet]
    public async Task<IActionResult> List(string? scope, CancellationToken ct)
    {
        switch (Role)
        {
            case "Reporter":
                return Ok(await _svc.ListForReporterAsync(Uid, ct));
            case "Resolver":
                if (scope == "pool") return Ok(await _svc.ListUnassignedAsync(ct));
                if (scope == "mine") return Ok(await _svc.ListForResolverAsync(Uid, ct));
                // combined default
                var pool = await _svc.ListUnassignedAsync(ct);
                var mine = await _svc.ListForResolverAsync(Uid, ct);
                return Ok(new { pool, mine });
            case "Admin":
                return Ok(await _svc.ListAllAsync(ct));
            default:
                return Forbid();
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var i = await _svc.GetAsync(id, ct);
        if (i is null) return NotFound();
        // Role-scoped visibility check
        if (Role == "Reporter" && i.ReporterId != Uid) return Forbid();
        if (Role == "Resolver"
            && i.CurrentAssigneeId != Uid
            && i.Status != Domain.IncidentStatus.Open) return Forbid();
        return Ok(i);
    }

    // ----- Reporter actions -------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IncidentCreateRequest req, CancellationToken ct)
    {
        try
        {
            var i = await _svc.CreateAsync(Uid, req.CategoryId, req.Description, ct);
            return CreatedAtAction(nameof(Get), new { id = i.Id }, i);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record IncidentCreateRequest(int CategoryId, string Description);

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        try { await _svc.ConfirmAsync(id, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        try { await _svc.ReopenAsync(id, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ----- Resolver actions -------------------------------------------------

    [HttpPost("{id:guid}/self-pick")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> SelfPick(Guid id, CancellationToken ct)
    {
        try { await _svc.SelfPickAsync(id, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = "Resolver,Admin")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken ct)
    {
        try { await _svc.MarkResolvedAsync(id, Uid, Role == "Admin", ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/force-close")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ForceClose(Guid id, CancellationToken ct)
    {
        try { await _svc.ForceCloseAsync(id, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ----- Admin actions ----------------------------------------------------

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest req, CancellationToken ct)
    {
        try { await _svc.AssignAsync(id, req.ResolverId, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record AssignRequest(Guid ResolverId);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest req, CancellationToken ct)
    {
        try { await _svc.RejectAsync(id, req.Reason, Uid, ct); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record RejectRequest(string Reason);

    [HttpPost("{id:guid}/reassign")]
    [Authorize(Roles = "Admin,Resolver")]
    public async Task<IActionResult> Reassign(Guid id, [FromBody] ReassignRequest req, CancellationToken ct)
    {
        try { await _svc.ReassignAsync(id, req.ResolverId, Uid, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public record ReassignRequest(Guid ResolverId);

    // ----- Dashboard counts -------------------------------------------------

    [HttpGet("status-counts")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> StatusCounts(CancellationToken ct)
        => Ok(await _svc.GetStatusCountsAsync(ct));

    /// <summary>Rendered workflow output for a ticket's thread (PRD section 6a).
    /// Same visibility rule as comments: reporter -> own tickets, resolver ->
    /// assigned/@tagged/open-pool, admin -> anything before Closed. Only the
    /// flattened tables are returned — raw step responses never leave the API.</summary>
    [HttpGet("{id:guid}/workflow-outputs")]
    public async Task<IActionResult> WorkflowOutputs(Guid id, CancellationToken ct)
    {
        var i = await _svc.GetAsync(id, ct);
        if (i is null) return NotFound();
        if (Role == "Reporter" && i.ReporterId != Uid) return Forbid();
        if (Role == "Resolver" && i.CurrentAssigneeId != Uid && i.Status != Domain.IncidentStatus.Open) return Forbid();
        return Ok(await _workflowExec.GetIncidentOutputsAsync(id, ct));
    }
}
