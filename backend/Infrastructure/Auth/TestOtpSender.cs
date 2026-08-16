namespace IncidentManagement.Api.Infrastructure.Auth;

/// <summary>
/// Phase 1 OTP sender: always returns the same fixed code (configurable,
/// defaults to "123456"). Writes the code to the log so dev/test users can see
/// what would have been sent. Marked clearly as NOT FOR PRODUCTION.
/// </summary>
public class TestOtpSender : IOtpSender
{
    private readonly ILogger<TestOtpSender> _log;
    private readonly string _fixedOtp;

    public TestOtpSender(ILogger<TestOtpSender> log, IConfiguration cfg)
    {
        _log = log;
        _fixedOtp = cfg["Auth:TestOtp"] ?? "123456";
    }

    public Task<bool> SendAsync(string mobileE164, string otp, CancellationToken ct = default)
    {
        // In test mode the caller passes the fixed OTP; we just acknowledge.
        _log.LogWarning("[TEST OTP SENDER] Would send OTP to {Mobile}: {Otp}", mobileE164, otp);
        return Task.FromResult(true);
    }

    public string FixedOtp => _fixedOtp;
}
