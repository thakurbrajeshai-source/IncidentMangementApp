namespace IncidentManagement.Api.Domain;

public class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-friendly ref like "INC-1042". Auto-generated at create time
    /// by reading MAX(stripped ref) + 1. Format: INC-{number}.</summary>
    public string TicketRef { get; set; } = "";

    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Description { get; set; } = "";

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    /// <summary>The primary resolver currently responsible. Null when unassigned
    /// (status=Open) or when Rejected/Closed (no active owner).</summary>
    public Guid? CurrentAssigneeId { get; set; }
    public User? CurrentAssignee { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Admin who rejected the ticket (audit for the Rejection Log report).</summary>
    public Guid? RejectedById { get; set; }
    public User? RejectedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? RejectedAt { get; set; }

    /// <summary>Number of times a Reporter has reverted a Resolved ticket back to In Progress.
    /// Per project owner: the original resolver stays assigned across reopens. The
    /// <see cref="IncidentAssignments"/> history captures any later reassignments.</summary>
    public int RevertCount { get; set; } = 0;

    // Navigation
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<IncidentAssignment> Assignments { get; set; } = new List<IncidentAssignment>();
}
