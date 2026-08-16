namespace IncidentManagement.Api.Domain;

/// <summary>
/// Append-only history of who has been on a ticket and how they got there.
/// We never overwrite CurrentAssigneeId without writing a row here, so the
/// "who handled this" question is always answerable from the audit log.
/// </summary>
public class IncidentAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid ResolverId { get; set; }
    public User Resolver { get; set; } = null!;
    public AssignmentType AssignmentType { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
