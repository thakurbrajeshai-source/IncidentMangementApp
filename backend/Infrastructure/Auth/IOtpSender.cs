namespace IncidentManagement.Api.Infrastructure.Auth;

/// <summary>
/// Abstraction over OTP delivery. The controller depends only on this interface;
/// swapping providers (MSG91, Twilio, Kaleyra, Gupshup) is a DI registration
/// change in Program.cs, not a code change in any caller.
///
/// Phase 1 (now): TestOtpSender writes to logs and returns the same code for everyone.
/// Phase 2: implement SmsOtpSender (or WhatsAppOtpSender) against the same contract,
///          pick a vendor that covers BOTH SMS and WhatsApp Business API so the
///          notification phase 3 (PRD section 5) doesn't need a second integration.
/// </summary>
public interface IOtpSender
{
    /// <summary>Sends the OTP to the given mobile number. Returns true if the
    /// request was accepted by the provider (does NOT guarantee delivery).</summary>
    Task<bool> SendAsync(string mobileE164, string otp, CancellationToken ct = default);
}
