using IncidentManagement.Api.Domain;

namespace IncidentManagement.Api.Infrastructure.RealTime;

/// <summary>
/// Sends a notification to a user. The DB row is written by the caller
/// (so it shows up in the bell/list even when the user is offline); this
/// interface only handles the live push. Phase 1 = SignalR. Phase 2 + 3
/// (web push, WhatsApp templates) wrap the same interface so callers stay
/// unchanged.
/// </summary>
public interface INotificationDispatcher
{
    Task PushAsync(Guid userId, Notification n, CancellationToken ct = default);
}
