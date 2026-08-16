namespace IncidentManagement.Api.Domain;

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public string Message { get; set; } = "";

    /// <summary>User ids @mentioned in the message body. JSON-encoded list of Guid
    /// strings; in MS SQL this maps to nvarchar(max). We keep it as a delimited
    /// string ("guid1;guid2") for SQLite dev simplicity — same shape, just a
    /// provider-agnostic serialization. The real backing column for production
    /// (SQL Server) can stay nvarchar(max) or be promoted to a separate
    /// CommentMentions join table later without touching API contracts.</summary>
    public string TaggedUserIds { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
