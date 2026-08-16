using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Auth;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Two-step login: request-otp issues the test code (or calls a real provider in
/// production), verify-otp exchanges it for a JWT. On first login a Reporter
/// account is created from the request body (FirstName, LastName, Email, Mobile).
/// Resolver and Admin accounts are NOT created here — they are provisioned by an
/// existing admin via the UserDirectoryService (per PRD section 4).
/// </summary>
public class AuthService
{
    private readonly AppDbContext _db;
    private readonly IOtpSender _otp;
    private readonly JwtService _jwt;
    private readonly TestOtpSender? _testOtp; // for the "echo the fixed code" response in dev
    private readonly bool _isDevMode;

    public AuthService(AppDbContext db, IOtpSender otp, JwtService jwt, IConfiguration cfg)
    {
        _db = db;
        _otp = otp;
        _jwt = jwt;
        _testOtp = otp as TestOtpSender;
        _isDevMode = cfg.GetValue("Auth:UseTestOtp", true);
    }

    public record RequestOtpResult(string Mobile, string? DevOtp);

    public async Task<RequestOtpResult> RequestOtpAsync(string mobile, CancellationToken ct = default)
    {
        var norm = Normalize(mobile);
        if (norm is null) throw new ArgumentException("Invalid mobile number. Use 10 digits or +E.164.");
        var code = _testOtp?.FixedOtp ?? "123456";
        await _otp.SendAsync(norm, code, ct);
        // In dev we return the code in the response so the UI can autofill it.
        // In production this should be null.
        return new RequestOtpResult(norm, _isDevMode ? code : null);
    }

    public record VerifyResult(string AccessToken, User User, bool IsNewUser);

    public async Task<VerifyResult> VerifyOtpAsync(
        string mobile, string otp, string? firstName, string? lastName, string? email, CancellationToken ct = default)
    {
        var norm = Normalize(mobile) ?? throw new ArgumentException("Invalid mobile number.");
        var expected = _testOtp?.FixedOtp ?? "123456";
        if (!string.Equals(otp.Trim(), expected, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Invalid OTP.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Mobile == norm, ct);
        var isNew = user is null;

        if (isNew)
        {
            // First-time login = Reporter self-registration. Resolver/Admin never
            // come through this path; they're provisioned by an admin.
            
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                // Step 1: User called verify without firstName/lastName
                // Return isNewUser=true so frontend shows registration form
                // Create a placeholder user just for token generation (not persisted)
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Mobile = norm,
                    FirstName = "[pending]",
                    LastName = "[pending]",
                    Role = UserRole.Reporter,
                    Status = UserStatus.Active,
                };
            }
            else
            {
                // Step 2: User called verify WITH firstName/lastName
                // Validate and create the actual user account
                if (firstName.Trim().Length < 2)
                    throw new ArgumentException("First name must be at least 2 characters.");
                if (lastName.Trim().Length < 2)
                    throw new ArgumentException("Last name must be at least 2 characters.");

                user = new User
                {
                    Mobile = norm,
                    FirstName = firstName.Trim(),
                    LastName = lastName.Trim(),
                    Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                    Role = UserRole.Reporter,
                    Status = UserStatus.Active,
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);
                isNew = false; // Account created, so return isNewUser=false to redirect to dashboard
            }
        }

        var token = _jwt.Issue(user!);
        return new VerifyResult(token, user!, isNew);
    }

    /// <summary>Strips spaces and non-digit chars, accepts +CC and 10-digit Indian
    /// formats, returns the digits-only normalized form (without the +).</summary>
    public static string? Normalize(string mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;
        var s = mobile.Trim().Replace(" ", "").Replace("-", "");
        if (s.StartsWith('+')) s = s[1..];
        // strip leading 91 if 12 digits and starts with 91
        if (s.Length == 12 && s.StartsWith("91") && s[2..].All(char.IsDigit))
            s = s[2..];
        if (s.Length != 10 || !s.All(char.IsDigit)) return null;
        return s;
    }
}
