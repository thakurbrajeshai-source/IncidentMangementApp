using IncidentManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IncidentManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _svc;
    public AuthController(AuthService svc) { _svc = svc; }

    public record RequestOtpRequest(string Mobile);
    public record VerifyOtpRequest(string Mobile, string Otp,
        string? FirstName = null, string? LastName = null, string? Email = null);

    /// <summary>Step 1: request an OTP for the given mobile. In dev, the response
    /// also includes the code so the UI can autofill it for testing.</summary>
    [HttpPost("request-otp")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestOtpRequest req, CancellationToken ct)
    {
        try
        {
            var r = await _svc.RequestOtpAsync(req.Mobile, ct);
            return Ok(new { mobile = r.Mobile, devOtp = r.DevOtp });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Step 2: exchange OTP for a JWT. First-time callers must supply
    /// FirstName/LastName/Email — the server creates a Reporter account.</summary>
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest req, CancellationToken ct)
    {
        try
        {
            var r = await _svc.VerifyOtpAsync(req.Mobile, req.Otp,
                req.FirstName, req.LastName, req.Email, ct);
            return Ok(new
            {
                accessToken = r.AccessToken,
                isNewUser = r.IsNewUser,
                user = new
                {
                    id = r.User.Id,
                    firstName = r.User.FirstName,
                    lastName = r.User.LastName,
                    fullName = r.User.FullName,
                    mobile = r.User.Mobile,
                    email = r.User.Email,
                    role = r.User.Role.ToString(),
                }
            });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { error = ex.Message }); }
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var u = User;
        return Ok(new
        {
            id = u.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            firstName = u.FindFirst("firstName")?.Value,
            lastName = u.FindFirst("lastName")?.Value,
            mobile = u.FindFirst(ClaimTypes.MobilePhone)?.Value,
            role = u.FindFirst(ClaimTypes.Role)?.Value,
        });
    }
}
