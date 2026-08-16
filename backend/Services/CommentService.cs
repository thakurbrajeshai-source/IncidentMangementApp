using IncidentManagement.Api.Domain;
using IncidentManagement.Api.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace IncidentManagement.Api.Services;

/// <summary>
/// Comments and @mentions. Tagging a user creates both a Comment row and an
/// IncidentAssignments row (AssignmentType=Tagged) so they show up in the
/// thread's participant list, and a Notification row so they get a ping.
/// </summary>
public class CommentService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifs;
    public CommentService(AppDbContext db, NotificationService notifs) { _db = db; _notifs = notifs; }

    public async Task<Comment> AddAsync(Guid incidentId, string authorRole, Guid authorId, string message,
        IEnumerable<Guid>? taggedUserIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Comment message is required.");

        var inc = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, ct)
            ?? throw new KeyNotFoundException($"Incident {incidentId} not found.");

        // Role-based visibility (spec permissions table): a Reporter can only
        // comment on their own tickets; a Resolver on tickets they're assigned
        // to or @tagged into (participant); an Admin anywhere before Closed.
        var isParticipant = authorRole switch
        {
            "Admin" => inc.Status != IncidentStatus.Closed,
            "Reporter" => inc.ReporterId == authorId,
            "Resolver" => inc.CurrentAssigneeId == authorId
                || await _db.IncidentAssignments.AnyAsync(a => a.IncidentId == inc.Id && a.ResolverId == authorId, ct),
            _ => false,
        };
        if (!isParticipant)
            throw new UnauthorizedAccessException("You are not a participant in this ticket.");

        var tagList = (taggedUserIds ?? Array.Empty<Guid>()).Distinct().Where(id => id != authorId).ToList();
        var c = new Comment
        {
            IncidentId = inc.Id,
            AuthorId = authorId,
            Message = message.Trim(),
            TaggedUserIds = string.Join(';', tagList),
            CreatedAt = DateTime.UtcNow,
        };
        _db.Comments.Add(c);

        // Tagged users become participants without taking over the ticket.
        foreach (var tid in tagList)
        {
            _db.IncidentAssignments.Add(new IncidentAssignment
            {
                IncidentId = inc.Id, ResolverId = tid,
                AssignmentType = AssignmentType.Tagged, AssignedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        // Notify the tagged users + the other participants of the thread.
        var participants = await _db.Comments
            .Where(x => x.IncidentId == inc.Id)
            .Select(x => x.AuthorId).Distinct().ToListAsync(ct);
        if (inc.CurrentAssigneeId is Guid aid) participants.Add(aid);
        participants.Add(inc.ReporterId);
        var pingSet = participants.Concat(tagList).Where(id => id != authorId).Distinct().ToList();
        await _notifs.BroadcastAsync(pingSet, NotificationType.NewComment, inc,
            title: $"New comment on {inc.TicketRef}",
            body: Truncate(message, 120), ct: ct);

        return c;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
