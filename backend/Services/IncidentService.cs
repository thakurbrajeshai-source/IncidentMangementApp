using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Core business logic for incident state transitions. All status changes in
/// the system go through this service so the lifecycle rules from PRD section 2
/// stay in one place. Controllers are thin: they parse input, call one of these
/// methods, return the result.
///
/// Lifecycle:
///   Open --(self-pick | assign)--> InProgress
///   InProgress --(mark resolved)--> Resolved  --(reporter confirm | auto-close)--> Closed
///                                              --(reporter revert)--> Reopened --(auto)--> InProgress
///   Open|InProgress --(admin reject, reason required)--> Rejected
/// </summary>
public class IncidentService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifs;
    public IncidentService(AppDbContext db, NotificationService notifs) { _db = db; _notifs = notifs; }

    // ----- Reads ------------------------------------------------------------

    public Task<List<Incident>> ListForReporterAsync(Guid reporterId, CancellationToken ct = default)
        => _db.Incidents
            .Include(i => i.Category)
            .Include(i => i.CurrentAssignee)
            .Where(i => i.ReporterId == reporterId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<List<Incident>> ListUnassignedAsync(CancellationToken ct = default)
        => _db.Incidents
            .Include(i => i.Category)
            .Include(i => i.Reporter)
            .Where(i => i.Status == IncidentStatus.Open && i.CurrentAssigneeId == null)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<List<Incident>> ListForResolverAsync(Guid resolverId, CancellationToken ct = default)
        => _db.Incidents
            .Include(i => i.Category)
            .Include(i => i.Reporter)
            .Where(i => i.CurrentAssigneeId == resolverId
                     && (i.Status == IncidentStatus.InProgress
                      || i.Status == IncidentStatus.Resolved
                      || i.Status == IncidentStatus.Reopened))
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<List<Incident>> ListAllAsync(CancellationToken ct = default)
        => _db.Incidents
            .Include(i => i.Category)
            .Include(i => i.Reporter)
            .Include(i => i.CurrentAssignee)
            .Include(i => i.RejectedBy)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public Task<Incident?> GetAsync(Guid id, CancellationToken ct = default)
        => _db.Incidents
            .Include(i => i.Category)
            .Include(i => i.Reporter)
            .Include(i => i.CurrentAssignee)
            .Include(i => i.RejectedBy)
            .Include(i => i.Comments).ThenInclude(c => c.Author)
            .Include(i => i.Assignments).ThenInclude(a => a.Resolver)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    // ----- Status counts (admin dashboard) ---------------------------------

    public async Task<StatusCounts> GetStatusCountsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Incidents.GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Get(IncidentStatus s) => rows.FirstOrDefault(r => r.Status == s)?.Count ?? 0;
        return new StatusCounts(
            Open:       Get(IncidentStatus.Open),
            InProgress: Get(IncidentStatus.InProgress) + Get(IncidentStatus.Reopened),
            Closed:     Get(IncidentStatus.Closed),
            Reverted:   await _db.Incidents.CountAsync(i => i.RevertCount > 0, ct)
        );
    }

    public record StatusCounts(int Open, int InProgress, int Closed, int Reverted);

    // ----- Commands ---------------------------------------------------------

    public async Task<Incident> CreateAsync(Guid reporterId, int categoryId, string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.");

        var ticketRef = await NextTicketRefAsync(ct);

        var incident = new Incident
        {
            TicketRef = ticketRef,
            ReporterId = reporterId,
            CategoryId = categoryId,
            Description = description.Trim(),
            Status = IncidentStatus.Open,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(ct);

        // Notify all resolvers + admins (the "unassigned pool" watcher feed).
        var targets = await _db.Users
            .Where(u => u.Status == UserStatus.Active && (u.Role == UserRole.Resolver || u.Role == UserRole.Admin))
            .Select(u => u.Id).ToListAsync(ct);
        await _notifs.BroadcastAsync(targets, NotificationType.TicketCreated, incident,
            title: $"New ticket {ticketRef}",
            body: Truncate(incident.Description, 120), ct: ct);

        return incident;
    }

    public async Task SelfPickAsync(Guid incidentId, Guid resolverId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status != IncidentStatus.Open)
            throw new InvalidOperationException("Only Open tickets can be self-picked.");
        if (i.CurrentAssigneeId != null)
            throw new InvalidOperationException("Ticket is already assigned.");

        i.Status = IncidentStatus.InProgress;
        i.CurrentAssigneeId = resolverId;
        _db.IncidentAssignments.Add(new IncidentAssignment
        {
            IncidentId = i.Id, ResolverId = resolverId,
            AssignmentType = AssignmentType.SelfPicked, AssignedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        await _notifs.BroadcastAsync(new[] { i.ReporterId }, NotificationType.TicketAssigned, i,
            title: $"{i.TicketRef} picked up", body: "A resolver is now working on your ticket.", ct: ct);
    }

    public async Task AssignAsync(Guid incidentId, Guid resolverId, Guid adminId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status is not (IncidentStatus.Open or IncidentStatus.InProgress or IncidentStatus.Reopened))
            throw new InvalidOperationException($"Cannot assign a ticket in status {i.Status}.");

        var changed = i.CurrentAssigneeId != resolverId;
        i.Status = IncidentStatus.InProgress;
        i.CurrentAssigneeId = resolverId;
        _db.IncidentAssignments.Add(new IncidentAssignment
        {
            IncidentId = i.Id, ResolverId = resolverId,
            AssignmentType = AssignmentType.AdminAssigned, AssignedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        if (changed)
            await _notifs.BroadcastAsync(new[] { resolverId }, NotificationType.TicketAssigned, i,
                title: $"Assigned: {i.TicketRef}", body: Truncate(i.Description, 120), ct: ct);
    }

    public async Task MarkResolvedAsync(Guid incidentId, Guid actorUserId, bool isAdmin, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status is not (IncidentStatus.InProgress or IncidentStatus.Reopened))
            throw new InvalidOperationException($"Cannot mark a {i.Status} ticket as resolved.");
        if (!isAdmin && i.CurrentAssigneeId != actorUserId)
            throw new UnauthorizedAccessException("Only the assigned resolver (or an admin) can mark this ticket resolved.");

        i.Status = IncidentStatus.Resolved;
        i.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _notifs.BroadcastAsync(new[] { i.ReporterId }, NotificationType.TicketResolved, i,
            title: $"{i.TicketRef} marked resolved",
            body: "Please confirm the fix or reopen if the issue persists.", ct: ct);
    }

    public async Task ConfirmAsync(Guid incidentId, Guid reporterId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.ReporterId != reporterId)
            throw new UnauthorizedAccessException("Only the reporter can confirm a ticket.");
        if (i.Status != IncidentStatus.Resolved)
            throw new InvalidOperationException($"Only Resolved tickets can be confirmed (current: {i.Status}).");

        i.Status = IncidentStatus.Closed;
        i.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (i.CurrentAssigneeId is Guid aid)
            await _notifs.BroadcastAsync(new[] { aid }, NotificationType.TicketClosed, i,
                title: $"{i.TicketRef} confirmed", body: "Reporter confirmed the fix.", ct: ct);
    }

    /// <summary>Admin force-closes a ticket at any point before it is already
    /// Closed/Rejected (spec: admin is the only role that can unilaterally
    /// close the loop). Resolved/Open/InProgress tickets can be force-closed.</summary>
    public async Task ForceCloseAsync(Guid incidentId, Guid adminId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status is IncidentStatus.Closed or IncidentStatus.Rejected)
            throw new InvalidOperationException($"Cannot force-close a {i.Status} ticket.");

        i.Status = IncidentStatus.Closed;
        i.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (i.CurrentAssigneeId is Guid aid)
            await _notifs.BroadcastAsync(new[] { aid }, NotificationType.TicketClosed, i,
                title: $"{i.TicketRef} closed by admin", body: "Admin closed this ticket.", ct: ct);
    }

    /// <summary>Background sweep: closes Resolved tickets the reporter never
    /// confirmed within <see cref="Incident:AutoCloseAfterHours"/> (default 48h).
    /// Idempotent — no-op unless the ticket is actually still Resolved.</summary>
    public async Task AutoCloseAsync(Guid incidentId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status != IncidentStatus.Resolved) return;

        i.Status = IncidentStatus.Closed;
        i.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (i.CurrentAssigneeId is Guid aid)
            await _notifs.BroadcastAsync(new[] { aid }, NotificationType.TicketClosed, i,
                title: $"{i.TicketRef} auto-closed",
                body: "No confirmation received; ticket auto-closed after the configured timeout.", ct: ct);
    }

    /// <summary>Reporter reverts a Resolved ticket because the fix didn't actually work.
    /// Per project owner: the original resolver stays assigned. We bump revert_count
    /// and set status back to InProgress. Any later reassignment is captured as a
    /// new IncidentAssignments row with AssignmentType=Reassigned.</summary>
    public async Task ReopenAsync(Guid incidentId, Guid reporterId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.ReporterId != reporterId)
            throw new UnauthorizedAccessException("Only the reporter can reopen their ticket.");
        if (i.Status != IncidentStatus.Resolved)
            throw new InvalidOperationException($"Only Resolved tickets can be reopened (current: {i.Status}).");

        i.Status = IncidentStatus.Reopened; // transient; UI / report query normalize to InProgress
        i.RevertCount += 1;
        await _db.SaveChangesAsync(ct);
        if (i.CurrentAssigneeId is Guid aid)
            await _notifs.BroadcastAsync(new[] { aid }, NotificationType.TicketReopened, i,
                title: $"{i.TicketRef} reopened",
                body: "Reporter says it's not fixed. Please look again.", ct: ct);
    }

    public async Task RejectAsync(Guid incidentId, string reason, Guid adminId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.");
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status is IncidentStatus.Closed or IncidentStatus.Rejected)
            throw new InvalidOperationException($"Cannot reject a {i.Status} ticket.");

        i.Status = IncidentStatus.Rejected;
        i.RejectionReason = reason.Trim();
        i.CurrentAssigneeId = null;
        i.RejectedById = adminId;
        i.RejectedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _notifs.BroadcastAsync(new[] { i.ReporterId }, NotificationType.TicketRejected, i,
            title: $"{i.TicketRef} rejected", body: reason, ct: ct);
    }

    /// <summary>Resolver or admin reassigns a ticket to a different resolver.
    /// The original assignee stays in the assignment history; the new resolver
    /// gets a fresh IncidentAssignments row with AssignmentType=Reassigned.</summary>
    public async Task ReassignAsync(Guid incidentId, Guid newResolverId, Guid actorUserId, CancellationToken ct = default)
    {
        var i = await LoadOrThrow(incidentId, ct);
        if (i.Status is IncidentStatus.Closed or IncidentStatus.Rejected)
            throw new InvalidOperationException($"Cannot reassign a {i.Status} ticket.");
        if (i.CurrentAssigneeId == newResolverId) return;

        i.CurrentAssigneeId = newResolverId;
        if (i.Status == IncidentStatus.Open) i.Status = IncidentStatus.InProgress;
        _db.IncidentAssignments.Add(new IncidentAssignment
        {
            IncidentId = i.Id, ResolverId = newResolverId,
            AssignmentType = AssignmentType.Reassigned, AssignedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        await _notifs.BroadcastAsync(new[] { newResolverId }, NotificationType.TicketAssigned, i,
            title: $"Reassigned: {i.TicketRef}", body: Truncate(i.Description, 120), ct: ct);
    }

    // ----- Helpers ----------------------------------------------------------

    private async Task<Incident> LoadOrThrow(Guid id, CancellationToken ct)
    {
        var i = await _db.Incidents.FirstOrDefaultAsync(x => x.Id == id, ct);
        return i ?? throw new KeyNotFoundException($"Incident {id} not found.");
    }

    private async Task<string> NextTicketRefAsync(CancellationToken ct)
    {
        // Cheap-and-cheerful: MAX the numeric portion and +1. Wraps racey under
        // extreme concurrency, but unique index on TicketRef guarantees no duplicates.
        var last = await _db.Incidents
            .Where(i => i.TicketRef.StartsWith("INC-"))
            .Select(i => i.TicketRef)
            .ToListAsync(ct);
        var maxN = last.Select(r => int.TryParse(r.AsSpan(4), out var n) ? n : 0).DefaultIfEmpty(1000).Max();
        return $"INC-{maxN + 1}";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
