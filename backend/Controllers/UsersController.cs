using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IncidentManagement.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly UserDirectoryService _svc;
    public UsersController(UserDirectoryService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? role, CancellationToken ct)
    {
        UserRole? r = role?.ToLowerInvariant() switch
        {
            "reporter" => UserRole.Reporter,
            "resolver" => UserRole.Resolver,
            "admin"    => UserRole.Admin,
            _ => null,
        };
        var users = await _svc.ListAsync(r, ct);
        return Ok(users.Select(u => new {
            id = u.Id, firstName = u.FirstName, lastName = u.LastName, fullName = u.FullName,
            mobile = u.Mobile, email = u.Email, role = u.Role.ToString(), status = u.Status.ToString(),
        }));
    }

    public record CreateStaffRequest(string Mobile, string FirstName, string LastName,
        string? Email, string Role);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateStaffRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            return BadRequest(new { error = "Role must be Resolver or Admin." });
        try
        {
            var u = await _svc.CreateStaffAsync(req.Mobile, req.FirstName, req.LastName, req.Email, role, ct);
            return Ok(new { id = u.Id });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/disable")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        try { await _svc.DisableAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
