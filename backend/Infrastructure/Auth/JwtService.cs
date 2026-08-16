using IncidentManagement.Api.Domain;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IncidentManagement.Api.Infrastructure.Auth;

/// <summary>
/// Issues short-lived JWTs for the mobile+OTP login flow. Role is encoded
/// as a "role" claim so the standard [Authorize(Roles="...")] attribute works.
/// </summary>
public class JwtService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _minutes;
    private readonly SigningCredentials _key;

    public JwtService(IConfiguration cfg)
    {
        _issuer = cfg["Jwt:Issuer"]!;
        _audience = cfg["Jwt:Audience"]!;
        _minutes = cfg.GetValue("Jwt:AccessTokenMinutes", 720);
        var raw = cfg["Jwt:Key"]!;
        _key = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(raw)),
            SecurityAlgorithms.HmacSha256);
    }

    public string Issue(User u)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, u.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.MobilePhone, u.Mobile),
            new(ClaimTypes.Name, u.FullName),
            new(ClaimTypes.Role, u.Role.ToString()),
            new("firstName", u.FirstName),
            new("lastName", u.LastName),
        };
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_minutes),
            signingCredentials: _key);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
