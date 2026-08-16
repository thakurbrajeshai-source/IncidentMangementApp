using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Database;
using IncidentManagement.Api.Infrastructure.RealTime;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Writes a Notifications row + pushes it live via INotificationDispatcher.
/// The DB row is what makes the bell/list show items even when the user was
/// offline at the time; the dispatcher is what makes the bell badge light up
/// in real time when the tab is open.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _push;
    public NotificationService(AppDbContext db, INotificationDispatcher push) { _db = db; _push = push; }

    public async Task BroadcastAsync(
        IEnumerable<Guid> userIds, NotificationType type, Incident? incident,
        string title, string body, CancellationToken ct = default)
    {
        var distinct = userIds.Distinct().ToList();
        var now = DateTime.UtcNow;
        var rows = distinct.Select(uid => new Notification
        {
            UserId = uid, Type = type, Incident = incident, IncidentId = incident?.Id,
            Title = title, Body = body, CreatedAt = now,
        }).ToList();
        _db.Notifications.AddRange(rows);
        await _db.SaveChangesAsync(ct);
        // Push live (best-effort; offline users see the row in the bell next time they open)
        foreach (var n in rows)
            try { await _push.PushAsync(n.UserId, n, ct); }
            catch { /* swallow; row still saved */ }
    }

    public Task<List<Notification>> ListAsync(Guid userId, bool unreadOnly, CancellationToken ct = default)
    {
        var q = _db.Notifications
            .Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return q.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
