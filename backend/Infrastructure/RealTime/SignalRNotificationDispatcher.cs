using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IncidentManagement.Api.Infrastructure.RealTime;

public class SignalRNotificationDispatcher : INotificationDispatcher
{
    private readonly IHubContext<NotificationsHub> _hub;

    public SignalRNotificationDispatcher(IHubContext<NotificationsHub> hub)
    {
        _hub = hub;
    }

    public Task PushAsync(Guid userId, Notification n, CancellationToken ct = default)
    {
        var group = $"u:{userId}";
        return _hub.Clients.Group(group).SendAsync("notification", new
        {
            id = n.Id,
            type = n.Type.ToString(),
            title = n.Title,
            body = n.Body,
            incidentId = n.IncidentId,
            createdAt = n.CreatedAt,
        }, ct);
    }
}
