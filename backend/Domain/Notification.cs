namespace IncidentManagement.Api.Domain;

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public NotificationType Type { get; set; }

    /// <summary>Optional FK to the related incident. Null for non-ticket notifications.</summary>
    public Guid? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
