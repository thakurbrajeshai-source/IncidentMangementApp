using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace IncidentManagement.Api.Hubs;

/// <summary>
/// Real-time push channel for in-app notifications (Phase 1).
/// Auth: the JWT can come from the Authorization header OR the access_token
/// query string (handled in Program.cs JwtBearer config), so the same token
/// that authenticates REST calls also authenticates the WebSocket.
/// </summary>
[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var sub = Context.User!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"u:{userId}");
        await base.OnConnectedAsync();
    }
}
