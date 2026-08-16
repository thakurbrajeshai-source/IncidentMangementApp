using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IncidentManagement.Api.Controllers;

[ApiController]
[Route("api/incidents/{incidentId:guid}/comments")]
[Authorize]
public class CommentsController : ControllerBase
{
    private readonly CommentService _svc;
    public CommentsController(CommentService svc) { _svc = svc; }

    private Guid Uid => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string Role => User.FindFirst(ClaimTypes.Role)!.Value;

    public record CommentCreateRequest(string Message, Guid[]? TaggedUserIds = null);

    [HttpPost]
    public async Task<IActionResult> Add(Guid incidentId, [FromBody] CommentCreateRequest req, CancellationToken ct)
    {
        try
        {
            var c = await _svc.AddAsync(incidentId, Role, Uid, req.Message, req.TaggedUserIds, ct);
            return Ok(c);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
    }
}
